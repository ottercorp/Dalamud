using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;

#pragma warning disable SA1600 // Internal Lumina reflection implementation.

namespace Dalamud.Data.Excel;

internal sealed class LuminaRawSheetFactory
{
    private const int ExcelDataHeaderSize = 32;
    private const int ExcelDataOffsetSize = 8;
    private const int MaxUnusedLookupItemCount = 0x10000;

    private readonly ConstructorInfo excelPageConstructor;
    private readonly ConstructorInfo rowLookupConstructor;
    private readonly Type rowLookupType;
    private readonly FieldInfo pagesField;
    private readonly FieldInfo pageDataField;
    private readonly FieldInfo rowLookupTableField;
    private readonly FieldInfo subrowDataOffsetField;
    private readonly FieldInfo rowIndexLookupDictionaryField;
    private readonly FieldInfo rowIndexLookupArrayField;
    private readonly FieldInfo rowIndexLookupArrayOffsetField;
    private readonly FieldInfo columnHashField;
    private readonly FieldInfo moduleField;
    private readonly FieldInfo languageField;
    private readonly FieldInfo columnsField;
    private readonly FieldInfo countField;

    public LuminaRawSheetFactory()
    {
        var fields = typeof(RawExcelSheet).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        this.pagesField = fields.Single(field => field.FieldType == typeof(ExcelPage[]));
        this.rowLookupTableField = fields.Single(
            field => field.FieldType.IsArray &&
                     field.FieldType.GetElementType()?.DeclaringType == typeof(RawExcelSheet));
        this.rowLookupType = this.rowLookupTableField.FieldType.GetElementType()!;
        this.rowLookupConstructor = this.rowLookupType.GetConstructor(
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                        null,
                                        [typeof(uint), typeof(uint), typeof(ushort), typeof(ushort)],
                                        null)
                                    ?? throw new MissingMethodException("Lumina row lookup constructor was not found.");
        this.subrowDataOffsetField = fields.Single(
            field => field.FieldType == typeof(ushort) && !field.Name.Contains("BackingField", StringComparison.Ordinal));
        this.rowIndexLookupDictionaryField = fields.Single(
            field => field.FieldType == typeof(FrozenDictionary<int, int>));
        this.rowIndexLookupArrayField = fields.Single(field => field.FieldType == typeof(int[]));
        this.columnHashField = GetBackingField(
            typeof(RawExcelSheet).GetProperty(
                "ColumnHash",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!);
        this.rowIndexLookupArrayOffsetField = fields.Single(
            field => field.FieldType == typeof(uint) && field != this.columnHashField);
        this.moduleField = GetBackingField(typeof(RawExcelSheet).GetProperty(nameof(RawExcelSheet.Module))!);
        this.languageField = GetBackingField(typeof(RawExcelSheet).GetProperty(nameof(RawExcelSheet.Language))!);
        this.columnsField = GetBackingField(typeof(RawExcelSheet).GetProperty(nameof(RawExcelSheet.Columns))!);
        this.countField = GetBackingField(typeof(RawExcelSheet).GetProperty(nameof(RawExcelSheet.Count))!);
        this.excelPageConstructor = typeof(ExcelPage).GetConstructor(
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                        null,
                                        [typeof(RawExcelSheet), typeof(byte[]), typeof(ushort)],
                                        null)
                                    ?? throw new MissingMethodException("Lumina Excel page constructor was not found.");
        this.pageDataField = typeof(ExcelPage)
                             .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             .Single(field => field.FieldType == typeof(byte[]));
    }

    public IReadOnlyList<byte[]?> ClonePageData(RawExcelSheet sheet)
    {
        var pages = (ExcelPage[])this.pagesField.GetValue(sheet)!;
        var result = new byte[]?[pages.Length];
        for (var i = 0; i < pages.Length; i++)
        {
            if (pages[i] is { } page)
                result[i] = ((byte[])this.pageDataField.GetValue(page)!).ToArray();
        }

        return result;
    }

    public uint GetColumnHash(RawExcelSheet sheet) => (uint)this.columnHashField.GetValue(sheet)!;

    public RawExcelSheet Create(
        ExcelModule module,
        ExcelHeaderFile header,
        Language language,
        uint columnHash,
        IReadOnlyList<byte[]?> pageData)
    {
        if (pageData.Count > ushort.MaxValue)
            throw new InvalidDataException("An Excel sheet contains too many EXDF pages.");

        var sheet = (RawExcelSheet)RuntimeHelpers.GetUninitializedObject(typeof(RawExcelSheet));
        this.moduleField.SetValue(sheet, module);
        this.languageField.SetValue(sheet, language);
        this.columnsField.SetValue(sheet, header.ColumnDefinitions);
        this.columnHashField.SetValue(sheet, columnHash);
        this.subrowDataOffsetField.SetValue(sheet, (ushort)0);

        var pages = new ExcelPage[pageData.Count];
        var rows = new List<ParsedRow>(checked((int)header.Header.RowCount));
        for (ushort pageIndex = 0; pageIndex < pageData.Count; pageIndex++)
        {
            var data = pageData[pageIndex];
            if (data is null)
                continue;

            ValidatePageAndReadRows(data, pageIndex, rows);
            pages[pageIndex] = (ExcelPage)this.excelPageConstructor.Invoke(
                [sheet, data, header.Header.DataOffset]);
        }

        rows.Sort(static (left, right) => left.RowId.CompareTo(right.RowId));
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i - 1].RowId == rows[i].RowId)
                throw new InvalidDataException($"An Excel sheet contains duplicate row {rows[i].RowId}.");
        }

        var rowLookupTable = Array.CreateInstance(this.rowLookupType, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            rowLookupTable.SetValue(
                this.rowLookupConstructor.Invoke([row.RowId, row.Offset, row.PageIndex, row.SubrowCount]),
                i);
        }

        BuildRowIndex(rows, out var lookupArray, out var lookupOffset, out var lookupDictionary);
        this.pagesField.SetValue(sheet, pages);
        this.rowLookupTableField.SetValue(sheet, rowLookupTable);
        this.rowIndexLookupArrayField.SetValue(sheet, lookupArray);
        this.rowIndexLookupArrayOffsetField.SetValue(sheet, lookupOffset);
        this.rowIndexLookupDictionaryField.SetValue(sheet, lookupDictionary);
        this.countField.SetValue(sheet, rows.Count);
        return sheet;
    }

    private static void ValidatePageAndReadRows(
        byte[] pageData,
        ushort pageIndex,
        List<ParsedRow> rows)
    {
        var data = pageData.AsSpan();
        if (data.Length < ExcelDataHeaderSize || !data[..4].SequenceEqual("EXDF"u8))
            throw new InvalidDataException("An Excel sheet contains an invalid EXD page.");

        var indexSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, sizeof(uint)));
        if (indexSize % ExcelDataOffsetSize != 0 ||
            indexSize > int.MaxValue ||
            ExcelDataHeaderSize + (int)indexSize > data.Length)
        {
            throw new InvalidDataException("An Excel sheet contains an invalid EXD row index.");
        }

        var rowCount = checked((int)(indexSize / ExcelDataOffsetSize));
        for (var i = 0; i < rowCount; i++)
        {
            var indexOffset = ExcelDataHeaderSize + (i * ExcelDataOffsetSize);
            var rowId = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(indexOffset, sizeof(uint)));
            var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(indexOffset + sizeof(uint), sizeof(uint)));
            if (dataOffset > data.Length - 6)
                throw new InvalidDataException("An Excel sheet contains an invalid EXD row offset.");

            var rowOffset = checked((int)dataOffset);
            var rowSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(rowOffset, sizeof(uint)));
            if (rowSize > data.Length - rowOffset - 6)
                throw new InvalidDataException("An Excel sheet contains an invalid EXD row size.");

            rows.Add(new ParsedRow(rowId, dataOffset + 6, pageIndex, 1));
        }
    }

    private static void BuildRowIndex(
        IReadOnlyList<ParsedRow> rows,
        out int[] lookupArray,
        out uint lookupOffset,
        out FrozenDictionary<int, int> lookupDictionary)
    {
        if (rows.Count == 0)
        {
            lookupArray = [];
            lookupOffset = 0;
            lookupDictionary = FrozenDictionary<int, int>.Empty;
            return;
        }

        lookupOffset = rows[0].RowId;
        var slotCount = (ulong)rows[^1].RowId - lookupOffset + 1;
        var unusedSlotCount = slotCount - (ulong)rows.Count;
        if (unusedSlotCount <= MaxUnusedLookupItemCount && slotCount <= int.MaxValue)
        {
            lookupArray = new int[(int)slotCount];
            lookupArray.AsSpan().Fill(-1);
            for (var i = 0; i < rows.Count; i++)
                lookupArray[checked((int)(rows[i].RowId - lookupOffset))] = i;
            lookupDictionary = FrozenDictionary<int, int>.Empty;
            return;
        }

        lookupArray = new int[MaxUnusedLookupItemCount];
        lookupArray.AsSpan().Fill(-1);
        var splitIndex = 0;
        var lastArrayIndex = 0;
        for (; splitIndex < rows.Count; splitIndex++)
        {
            var index = rows[splitIndex].RowId - lookupOffset;
            if (index >= lookupArray.Length)
                break;

            lookupArray[checked((int)index)] = splitIndex;
            lastArrayIndex = checked((int)index);
        }

        Array.Resize(ref lookupArray, lastArrayIndex + 1);
        lookupDictionary = rows
                           .Skip(splitIndex)
                           .Select((row, offset) => new KeyValuePair<int, int>(
                               checked((int)row.RowId),
                               splitIndex + offset))
                           .ToFrozenDictionary();
    }

    private static FieldInfo GetBackingField(PropertyInfo property) =>
        property.DeclaringType?.GetField(
            $"<{property.Name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(property.DeclaringType?.FullName, property.Name);

    private readonly record struct ParsedRow(uint RowId, uint Offset, ushort PageIndex, ushort SubrowCount);
}
