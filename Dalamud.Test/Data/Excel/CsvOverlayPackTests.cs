using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using Dalamud.Data.Excel;

using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text.ReadOnly;

using Xunit;

namespace Dalamud.Test.Data.Excel;

public sealed class CsvOverlayPackTests
{
    private const string ValidPackJson = """
        {
          "formatVersion": 1,
          "gameVersion": "2026.06.18.0000.0000",
          "language": "English",
          "profileVersion": 3,
          "variant": "lite",
          "sheets": [
            {
              "name": "TestSheet",
              "entry": "sheets/TestSheet.json",
              "columnTypes": [ "str", "uint32", "uint32", "bit&01" ],
              "variant": "default"
            }
          ]
        }
        """;

    private const string ValidSheetJson = """
        {
          "formatVersion": 1,
          "rows": [
            {
              "rowId": 42,
              "cells": [
                { "column": 0, "value": "English" }
              ]
            }
          ],
          "macroRows": [
            {
              "rowId": 42,
              "cells": [
                { "column": 2, "value": "<icon(1)>" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Read_ValidPack_ReturnsPhysicalSchemaAndRows()
    {
        using var stream = CreatePack(ValidPackJson, ValidSheetJson);

        var pack = CsvOverlayPack.Read(stream);
        var sheet = Assert.Single(pack.Sheets).Value;

        Assert.Equal("2026.06.18.0000.0000", pack.Manifest.GameVersion);
        Assert.Equal(["str", "uint32", "uint32", "bit&01"], sheet.Definition.ColumnTypes);
        var row = Assert.Single(sheet.Rows);
        Assert.Collection(
            row.Cells.OrderBy(cell => cell.Column),
            cell =>
            {
                Assert.Equal("English", cell.Value);
                Assert.False(cell.IsMacroString);
            },
            cell =>
            {
                Assert.Equal("<icon(1)>", cell.Value);
                Assert.True(cell.IsMacroString);
            });
    }

    [Fact]
    public void Read_UnsupportedMetadata_Throws()
    {
        using var stream = CreatePack(
            ValidPackJson.Replace("\"language\": \"English\"", "\"language\": \"Japanese\""),
            ValidSheetJson);

        Assert.Throws<InvalidDataException>(() => CsvOverlayPack.Read(stream));
    }

    [Fact]
    public void Read_MissingSheet_Throws()
    {
        using var stream = CreatePack(ValidPackJson, ValidSheetJson, "sheets/OtherSheet.json");

        Assert.Throws<InvalidDataException>(() => CsvOverlayPack.Read(stream));
    }

    [Fact]
    public void Create_ReplacesOneStringAndPreservesOtherPayloadAndNumericBytes()
    {
        var header = CreateHeader(ExcelVariant.Default);
        var sourcePage = CreatePage();
        var module = (ExcelModule)RuntimeHelpers.GetUninitializedObject(typeof(ExcelModule));
        var rawFactory = new LuminaRawSheetFactory();
        var baseSheet = rawFactory.Create(
            module,
            header,
            Language.ChineseSimplified,
            0x12345678,
            [sourcePage.ToArray()]);
        var overlay = CreateOverlay(["str", "uint32", "str", "str"]);

        var result = new CsvOverlayRawSheetFactory().Create(
            module,
            header,
            Language.English,
            baseSheet,
            overlay);
        var row = CreateRawRow(result);
        var resultPage = Assert.Single(rawFactory.ClonePageData(result))!;
        const int rowOffset = 40;
        const int rowDataOffset = rowOffset + 6;
        const int fixedDataSize = 16;
        var sourceRowSize = BinaryPrimitives.ReadUInt32BigEndian(sourcePage.AsSpan(rowOffset, sizeof(uint)));
        var resultRowSize = BinaryPrimitives.ReadUInt32BigEndian(resultPage.AsSpan(rowOffset, sizeof(uint)));
        var originalStringBytes = sourcePage.AsSpan(
            rowDataOffset + fixedDataSize,
            checked((int)sourceRowSize - fixedDataSize));

        Assert.Equal("English", row.ReadStringColumn(0).ExtractText());
        Assert.Equal("保留中文", row.ReadStringColumn(2).ExtractText());
        Assert.Equal(sourcePage.AsSpan(rowDataOffset + 4, sizeof(uint)), resultPage.AsSpan(rowDataOffset + 4, sizeof(uint)));
        Assert.Equal(sourcePage.AsSpan(rowDataOffset + 8, sizeof(uint)), resultPage.AsSpan(rowDataOffset + 8, sizeof(uint)));
        Assert.Equal(sourcePage.AsSpan(rowDataOffset + 12, sizeof(uint)), resultPage.AsSpan(rowDataOffset + 12, sizeof(uint)));
        Assert.Equal(
            originalStringBytes,
            resultPage.AsSpan(rowDataOffset + fixedDataSize, originalStringBytes.Length));
        Assert.Equal(sourceRowSize + (uint)Encoding.UTF8.GetByteCount("English") + 1, resultRowSize);
        Assert.Equal(
            sourceRowSize - fixedDataSize,
            BinaryPrimitives.ReadUInt32BigEndian(resultPage.AsSpan(rowDataOffset, sizeof(uint))));
        Assert.Equal(sourcePage, Assert.Single(rawFactory.ClonePageData(baseSheet)));
        Assert.Equal(
            (uint)(resultPage.Length - rowOffset),
            BinaryPrimitives.ReadUInt32BigEndian(resultPage.AsSpan(12, sizeof(uint))));
    }

    [Fact]
    public void Schema_MismatchOrSubrows_IsRejected()
    {
        var header = CreateHeader(ExcelVariant.Default);
        var wrongType = CreateOverlay(["uint32", "uint32", "str", "str"]).Definition;

        Assert.False(CsvOverlayRawSheetFactory.IsCompatible(header, wrongType));

        var subrowHeader = CreateHeader(ExcelVariant.Subrows);
        Assert.False(
            CsvOverlayRawSheetFactory.IsCompatible(
                subrowHeader,
                CreateOverlay(["str", "uint32", "str", "str"]).Definition));
    }

    [Fact]
    public void Create_GrowingFirstRowRewritesFollowingIndexOffset()
    {
        var header = CreateHeader(ExcelVariant.Default, rowCount: 2);
        var sourcePage = CreateTwoRowPage();
        var originalSecondOffset = BinaryPrimitives.ReadUInt32BigEndian(sourcePage.AsSpan(44, sizeof(uint)));
        var originalSecondRow = sourcePage.AsSpan(
            checked((int)originalSecondOffset),
            sourcePage.Length - checked((int)originalSecondOffset)).ToArray();
        var module = (ExcelModule)RuntimeHelpers.GetUninitializedObject(typeof(ExcelModule));
        var rawFactory = new LuminaRawSheetFactory();
        var baseSheet = rawFactory.Create(
            module,
            header,
            Language.ChineseSimplified,
            0x12345678,
            [sourcePage]);

        var result = new CsvOverlayRawSheetFactory().Create(
            module,
            header,
            Language.English,
            baseSheet,
            CreateOverlay(["str", "uint32", "str", "str"]));
        var resultPage = Assert.Single(rawFactory.ClonePageData(result))!;
        var resultSecondOffset = BinaryPrimitives.ReadUInt32BigEndian(resultPage.AsSpan(44, sizeof(uint)));

        Assert.Equal(
            originalSecondOffset + (uint)Encoding.UTF8.GetByteCount("English") + 1,
            resultSecondOffset);
        Assert.Equal(originalSecondRow, resultPage.AsSpan(checked((int)resultSecondOffset)));
    }

    [Fact]
    public void Create_EncodesMacroStringAsSeStringPayload()
    {
        var header = CreateHeader(ExcelVariant.Default);
        var module = (ExcelModule)RuntimeHelpers.GetUninitializedObject(typeof(ExcelModule));
        var rawFactory = new LuminaRawSheetFactory();
        var baseSheet = rawFactory.Create(
            module,
            header,
            Language.ChineseSimplified,
            0x12345678,
            [CreatePage()]);
        var overlay = CreateOverlay(["str", "uint32", "str", "str"]);
        overlay.Rows[0].Cells[0].Value = "<icon(1)>";
        overlay.Rows[0].Cells[0].IsMacroString = true;

        var result = new CsvOverlayRawSheetFactory().Create(
            module,
            header,
            Language.English,
            baseSheet,
            overlay);
        var value = CreateRawRow(result).ReadStringColumn(0);

        Assert.Equal("<icon(1)>", value.ToMacroString());
        Assert.NotEqual("<icon(1)>", value.ExtractText());
    }

    private static MemoryStream CreatePack(
        string packJson,
        string sheetJson,
        string sheetEntry = "sheets/TestSheet.json")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "pack.json", packJson);
            WriteEntry(archive, sheetEntry, sheetJson);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
        writer.Write(value);
    }

    private static ExcelHeaderFile CreateHeader(ExcelVariant variant, uint rowCount = 1)
    {
        var header = new ExcelHeaderFile();
        SetProperty(
            header,
            nameof(ExcelHeaderFile.Header),
            new ExcelHeaderHeader
            {
                DataOffset = 16,
                Variant = variant,
                RowCount = rowCount,
            });
        SetProperty(
            header,
            nameof(ExcelHeaderFile.ColumnDefinitions),
            new[]
            {
                new ExcelColumnDefinition { Type = ExcelColumnDataType.String, Offset = 0 },
                new ExcelColumnDefinition { Type = ExcelColumnDataType.UInt32, Offset = 4 },
                new ExcelColumnDefinition { Type = ExcelColumnDataType.String, Offset = 8 },
                new ExcelColumnDefinition { Type = ExcelColumnDataType.String, Offset = 12 },
            });
        return header;
    }

    private static byte[] CreatePage()
    {
        var first = Encoding.UTF8.GetBytes("待替换");
        var second = Encoding.UTF8.GetBytes("保留中文");
        byte[] macro = [0x02, 0x10, 0x01, 0x03];
        var stringDataSize = first.Length + second.Length + macro.Length + 3;
        const int rowOffset = 40;
        const int fixedDataSize = 16;
        var rowSize = fixedDataSize + stringDataSize;
        var data = new byte[rowOffset + 6 + rowSize];
        "EXDF"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), checked((uint)(6 + rowSize)));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), 42);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(36), rowOffset);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(rowOffset), checked((uint)rowSize));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rowOffset + 4), 1);

        var row = data.AsSpan(rowOffset + 6);
        BinaryPrimitives.WriteUInt32BigEndian(row, 0);
        BinaryPrimitives.WriteUInt32BigEndian(row.Slice(4), 0x12345678);
        BinaryPrimitives.WriteUInt32BigEndian(row.Slice(8), checked((uint)(first.Length + 1)));
        BinaryPrimitives.WriteUInt32BigEndian(row.Slice(12), checked((uint)(first.Length + second.Length + 2)));
        var strings = row[fixedDataSize..];
        first.CopyTo(strings);
        strings[first.Length] = 0;
        second.CopyTo(strings[(first.Length + 1)..]);
        strings[first.Length + second.Length + 1] = 0;
        macro.CopyTo(strings[(first.Length + second.Length + 2)..]);
        return data;
    }

    private static byte[] CreateTwoRowPage()
    {
        var singlePage = CreatePage();
        var row = singlePage.AsSpan(40).ToArray();
        const int firstRowOffset = 48;
        var secondRowOffset = firstRowOffset + row.Length;
        var data = new byte[secondRowOffset + row.Length];
        singlePage.AsSpan(0, 32).CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 16);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), checked((uint)(row.Length * 2)));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), 42);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(36), firstRowOffset);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), 43);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), checked((uint)secondRowOffset));
        row.CopyTo(data.AsSpan(firstRowOffset));
        row.CopyTo(data.AsSpan(secondRowOffset));
        return data;
    }

    private static CsvOverlaySheet CreateOverlay(IReadOnlyList<string> columnTypes) =>
        new(
            new CsvOverlaySheetDefinition
            {
                Name = "TestSheet",
                Entry = "sheets/TestSheet.json",
                ColumnTypes = [.. columnTypes],
                Variant = "default",
            },
            [
                new CsvOverlayRow
                {
                    RowId = 42,
                    Cells = [new CsvOverlayCell { Column = 0, Value = "English" }],
                },
            ]);

    private static void SetProperty<T>(ExcelHeaderFile header, string propertyName, T value)
    {
        var property = typeof(ExcelHeaderFile).GetProperty(propertyName);
        Assert.NotNull(property);
        property.SetValue(header, value);
    }

    private static RawRow CreateRawRow(RawExcelSheet sheet)
    {
        var method = typeof(RawExcelSheet).GetMethod(
            "UnsafeCreateRowAt",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<RawRow>(method.MakeGenericMethod(typeof(RawRow)).Invoke(sheet, [0]));
    }
}
