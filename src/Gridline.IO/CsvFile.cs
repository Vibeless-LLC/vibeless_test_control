using System.Globalization;
using System.Text;
using Gridline.Core;
using Gridline.Core.Addressing;

namespace Gridline.IO;

/// <summary>CSV / delimited import and export (§8.5, §10.2).</summary>
public static class CsvFile
{
    private static void CheckDelimiter(char d)
    {
        if (d is '"' or '\r' or '\n') throw new ArgumentException("Invalid delimiter.", nameof(d));
    }

    /// <summary>Decodes bytes: UTF-8 BOM, UTF-16 LE/BE BOM, strict UTF-8, else Windows-1252 (§8.5 Encoding).</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes[3..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { }
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
    }

    public static Workbook Import(ReadOnlySpan<byte> bytes, char delimiter = ',')
    {
        CheckDelimiter(delimiter);
        var records = DelimitedReader.Parse(Decode(bytes), delimiter);
        var cells = new List<KeyValuePair<string, string>>();
        for (int r = 0; r < records.Count; r++)
        {
            var rec = records[r];
            for (int c = 0; c < rec.Count; c++)
            {
                if (rec[c].Length == 0) continue;
                cells.Add(new(new CellAddress(r + 1, c + 1).ToString(), DelimitedMapping.ToInput(rec[c])));
            }
        }
        try { return Workbook.FromSnapshot(cells, Array.Empty<KeyValuePair<string, string>>()); }
        catch (ArgumentException ex) { throw new GridlineFormatException("invalid CSV content: " + ex.Message, ex); }
    }

    public static Workbook ImportFromPath(string path, char delimiter = ',')
    {
        CheckDelimiter(delimiter);
        return Import(File.ReadAllBytes(path), delimiter);
    }

    public static byte[] Export(Workbook wb, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(wb);
        CheckDelimiter(delimiter);
        var values = new Dictionary<int, CellValue>();
        int maxRow = 0, maxCol = 0;
        foreach (var addr in wb.GetUsedAddresses())
        {
            var v = wb.GetValue(addr);
            if (v.Kind == ValueKind.Blank) continue;
            var a = CellAddress.Parse(addr);
            values[a.Key] = v;
            if (a.Row > maxRow) maxRow = a.Row;
            if (a.Col > maxCol) maxCol = a.Col;
        }
        var sb = new StringBuilder();
        for (int r = 1; r <= maxRow; r++)
        {
            for (int c = 1; c <= maxCol; c++)
            {
                if (c > 1) sb.Append(delimiter);
                if (values.TryGetValue(new CellAddress(r, c).Key, out var v)) AppendField(sb, Format(v), delimiter);
            }
            sb.Append("\r\n");
        }
        var body = new UTF8Encoding(false).GetBytes(sb.ToString());
        var result = new byte[3 + body.Length];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    private static string Format(CellValue v) => v.Kind switch
    {
        ValueKind.Number => v.Number.ToString("R", CultureInfo.InvariantCulture),
        ValueKind.Boolean => v.Boolean ? "TRUE" : "FALSE",
        ValueKind.Text => v.Text,
        ValueKind.Error => v.Error,
        _ => "",
    };

    private static void AppendField(StringBuilder sb, string f, char delimiter)
    {
        bool quote = f.Length > 0 && (f.Contains(delimiter) || f.Contains('"') || f.Contains('\r') || f.Contains('\n') || f[0] == ' ' || f[^1] == ' ');
        if (!quote) { sb.Append(f); return; }
        sb.Append('"').Append(f.Replace("\"", "\"\"")).Append('"');
    }

    /// <summary>Writes to a temp file in the same directory, flushes to disk, then replaces the target.</summary>
    public static void ExportToPath(Workbook wb, string path, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(wb);
        ArgumentNullException.ThrowIfNull(path);
        CheckDelimiter(delimiter);
        path = Path.GetFullPath(path);
        var bytes = Export(wb, delimiter);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
