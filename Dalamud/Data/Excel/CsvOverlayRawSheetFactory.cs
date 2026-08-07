using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text.Parse;
using Lumina.Text.ReadOnly;

#pragma warning disable SA1600 // Internal raw-sheet implementation.

namespace Dalamud.Data.Excel;

internal sealed class CsvOverlayRawSheetFactory
{
    private const int ExcelDataHeaderSize = 32;
    private const int ExcelDataOffsetSize = 8;

    private static readonly MacroStringParseOptions StrictMacroOptions = new()
    {
        ExceptionMode = MacroStringParseExceptionMode.Throw,
    };

    private readonly LuminaRawSheetFactory rawSheetFactory = new();

    public static bool IsCompatible(ExcelHeaderFile header, CsvOverlaySheetDefinition definition) =>
        header.Header.Variant == ExcelVariant.Default &&
        definition.Variant == "default" &&
        header.ColumnDefinitions.Length == definition.ColumnTypes.Count &&
        header.ColumnDefinitions
              .Select(column => column.Type == ExcelColumnDataType.String)
              .SequenceEqual(definition.ColumnTypes.Select(type => type == "str"));

    public RawExcelSheet Create(
        ExcelModule module,
        ExcelHeaderFile header,
        Language language,
        RawExcelSheet baseSheet,
        CsvOverlaySheet overlay)
    {
        var physicalColumns = header.ColumnDefinitions;
        var rows = overlay.Rows.ToDictionary(row => row.RowId);
        var rewrittenRows = new HashSet<uint>();
        var pages = this.rawSheetFactory.ClonePageData(baseSheet).ToArray();
        for (var i = 0; i < pages.Length; i++)
        {
            if (pages[i] is { } page)
            {
                pages[i] = RewritePage(
                    page,
                    header.Header.DataOffset,
                    physicalColumns,
                    rows,
                    rewrittenRows);
            }
        }

        var missingRow = rows.Keys.Except(rewrittenRows).Order().FirstOrDefault();
        if (rewrittenRows.Count != rows.Count)
        {
            throw new InvalidDataException(
                $"CSV overlay sheet '{overlay.Definition.Name}' references missing row {missingRow}.");
        }

        return this.rawSheetFactory.Create(
            module,
            header,
            language,
            this.rawSheetFactory.GetColumnHash(baseSheet),
            pages);
    }

    private static byte[] RewritePage(
        byte[] source,
        ushort fixedDataSize,
        IReadOnlyList<ExcelColumnDefinition> physicalColumns,
        IReadOnlyDictionary<uint, CsvOverlayRow> overlays,
        ISet<uint> rewrittenRows)
    {
        var data = source.AsSpan();
        var indexSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, sizeof(uint))));
        var rowCount = indexSize / ExcelDataOffsetSize;
        if (rowCount == 0)
            return source.ToArray();

        var rows = new PageRow[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            var indexOffset = ExcelDataHeaderSize + (i * ExcelDataOffsetSize);
            var rowId = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(indexOffset, sizeof(uint)));
            var rowOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(indexOffset + sizeof(uint), sizeof(uint))));
            var rowSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(rowOffset, sizeof(uint))));
            rows[i] = new PageRow(rowId, rowOffset, rowSize + 6, indexOffset);
        }

        var physicalRows = rows.OrderBy(row => row.Offset).ToArray();
        var firstRowOffset = physicalRows[0].Offset;
        using var output = new MemoryStream(source.Length);
        output.Write(source, 0, firstRowOffset);
        var newOffsets = new Dictionary<int, uint>();
        var sourceCursor = firstRowOffset;
        foreach (var row in physicalRows)
        {
            output.Write(source, sourceCursor, row.Offset - sourceCursor);
            newOffsets.Add(row.IndexOffset, checked((uint)output.Position));
            if (overlays.TryGetValue(row.RowId, out var overlay))
            {
                var rewritten = RewriteRow(
                    source.AsSpan(row.Offset, row.Length),
                    fixedDataSize,
                    physicalColumns,
                    overlay);
                output.Write(rewritten);
                rewrittenRows.Add(row.RowId);
            }
            else
            {
                output.Write(source, row.Offset, row.Length);
            }

            sourceCursor = row.Offset + row.Length;
        }

        output.Write(source, sourceCursor, source.Length - sourceCursor);
        var result = output.ToArray();
        foreach (var row in rows)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(row.IndexOffset + sizeof(uint), sizeof(uint)),
                newOffsets[row.IndexOffset]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(12, sizeof(uint)),
            checked((uint)(result.Length - firstRowOffset)));
        return result;
    }

    private static byte[] RewriteRow(
        ReadOnlySpan<byte> source,
        ushort fixedDataSize,
        IReadOnlyList<ExcelColumnDefinition> physicalColumns,
        CsvOverlayRow overlay)
    {
        var rowSize = BinaryPrimitives.ReadUInt32BigEndian(source[..sizeof(uint)]);
        var encodedCells = new List<(ExcelColumnDefinition Column, byte[] Value)>(overlay.Cells.Count);
        var appendedSize = 0;
        foreach (var cell in overlay.Cells)
        {
            var column = physicalColumns[cell.Column];
            var bytes = EncodeCell(cell);
            appendedSize = checked(appendedSize + bytes.Length + 1);
            encodedCells.Add((column, bytes));
        }

        var result = new byte[checked(source.Length + appendedSize)];
        source.CopyTo(result);
        var originalStringDataSize = checked((int)rowSize - fixedDataSize);
        var appendOffset = source.Length;
        foreach (var (column, value) in encodedCells)
        {
            var stringOffset = checked((uint)(originalStringDataSize + appendOffset - source.Length));
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(6 + column.Offset, sizeof(uint)),
                stringOffset);
            value.CopyTo(result.AsSpan(appendOffset));
            appendOffset += value.Length;
            result[appendOffset++] = 0;
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(0, sizeof(uint)),
            checked(rowSize + (uint)appendedSize));
        return result;
    }

    private static byte[] EncodeCell(CsvOverlayCell cell)
    {
        if (!cell.IsMacroString)
        {
            if (cell.Value.IndexOfAny(['\0', '\u0002', '\u0003']) >= 0)
                throw new InvalidDataException("The CSV overlay UTF-8 cell contains a reserved control character.");
            return Encoding.UTF8.GetBytes(cell.Value);
        }

        var value = ReadOnlySeString.FromMacroString(cell.Value, StrictMacroOptions);
        ReadOnlySpan<byte> bytes = value;
        if (bytes.Contains((byte)0))
            throw new InvalidDataException("The CSV overlay cell contains an embedded null byte.");
        return bytes.ToArray();
    }

    private readonly record struct PageRow(uint RowId, int Offset, int Length, int IndexOffset);
}
