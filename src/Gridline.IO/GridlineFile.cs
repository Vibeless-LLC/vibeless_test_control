using System.Text;
using System.Text.Json;
using Gridline.Core;

namespace Gridline.IO;

/// <summary>The .gridline JSON file format, version 2, reading 1 and 2 (§8.1, §8.2, §8.4, CR1.5).</summary>
public static class GridlineFile
{
    private const long CurrentVersion = 2;

    /// <summary>Canonical text: LF line endings, trailing newline. Always version 2 (CR1.5).</summary>
    public static string Serialize(Workbook wb)
    {
        ArgumentNullException.ThrowIfNull(wb);
        var sb = new StringBuilder();
        sb.Append("{\n  \"format\": \"gridline\",\n  \"version\": 2,\n  \"activeSheet\": ");
        JsonEscaper.Append(sb, wb.ActiveSheet);
        sb.Append(",\n");

        var names = wb.GetNames().OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) sb.Append("  \"names\": {},\n");
        else
        {
            sb.Append("  \"names\": {\n");
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append("    ");
                JsonEscaper.Append(sb, names[i].Key);
                sb.Append(": ");
                JsonEscaper.Append(sb, names[i].Value);
                sb.Append(i + 1 < names.Count ? ",\n" : "\n");
            }
            sb.Append("  },\n");
        }

        sb.Append("  \"sheets\": [\n");
        var sheets = wb.SheetNames;
        for (int s = 0; s < sheets.Count; s++)
        {
            sb.Append("    {\n      \"name\": ");
            JsonEscaper.Append(sb, sheets[s]);
            sb.Append(",\n");
            var addrs = wb.GetUsedAddresses(sheets[s]);
            if (addrs.Count == 0) sb.Append("      \"cells\": []\n");
            else
            {
                string prefix = Gridline.Core.Addressing.SheetNames.Format(sheets[s]) + "!";
                sb.Append("      \"cells\": [\n");
                for (int i = 0; i < addrs.Count; i++)
                {
                    sb.Append("        [");
                    JsonEscaper.Append(sb, addrs[i]);
                    sb.Append(", ");
                    JsonEscaper.Append(sb, wb.GetInput(prefix + addrs[i]));
                    sb.Append(i + 1 < addrs.Count ? "],\n" : "]\n");
                }
                sb.Append("      ]\n");
            }
            sb.Append(s + 1 < sheets.Count ? "    },\n" : "    }\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    /// <summary>Canonical text as UTF-8 without a BOM.</summary>
    public static byte[] SerializeToUtf8(Workbook wb) => new UTF8Encoding(false).GetBytes(Serialize(wb));

    public static Workbook Load(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            // Lenient reading: skip a leading UTF-8 BOM (never written).
            if (utf8Json.Length >= 3 && utf8Json[0] == 0xEF && utf8Json[1] == 0xBB && utf8Json[2] == 0xBF)
                utf8Json = utf8Json[3..];
            using var doc = JsonDocument.Parse(utf8Json.ToArray());
            return Build(doc.RootElement);
        }
        catch (InvalidOperationException ex)
        {
            // System.Text.Json cannot decode this string (e.g. invalid UTF-8 inside a value).
            throw new GridlineFormatException("undecodable string: " + ex.Message, ex);
        }
        catch (JsonException ex)
        {
            throw new GridlineFormatException("malformed JSON: " + ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            throw new GridlineFormatException("invalid workbook: " + ex.Message, ex);
        }
    }

    public static Workbook LoadFromPath(string path) => Load(File.ReadAllBytes(path));

    /// <summary>Writes to a temp file in the same directory, flushes to disk, then replaces the target (§8.4).</summary>
    public static void SaveToPath(Workbook wb, string path)
    {
        ArgumentNullException.ThrowIfNull(wb);
        ArgumentNullException.ThrowIfNull(path);
        // Resolve first: an empty or malformed path throws here, before any temp file exists, and the temp file
        // always lands in the target's directory (never the working directory for a relative "" target).
        path = Path.GetFullPath(path);
        var bytes = SerializeToUtf8(wb);
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

    /// <summary>
    /// Reads a JSON string value. System.Text.Json refuses strings with an unpaired surrogate escape
    /// (<c>\ud83d</c>), which the writer legitimately emits for lone surrogates, so fall back to manual unescaping.
    /// </summary>
    private static string Str(JsonElement e)
    {
        try { return e.GetString()!; }
        catch (InvalidOperationException) { }
        return Unescape(e.GetRawText());
    }

    /// <summary>A property name, tolerating lone-surrogate escapes (parsed from the raw property text).</summary>
    private static string PropName(JsonProperty p)
    {
        try { return p.Name; }
        catch (InvalidOperationException) { }
        var raw = p.ToString();
        int end = 1;
        while (raw[end] != '"') end += raw[end] == '\\' ? 2 : 1;
        return Unescape(raw[..(end + 1)]);
    }

    /// <summary>Unescapes a raw quoted JSON string token (already validated by the parser).</summary>
    private static string Unescape(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 1; i < raw.Length - 1; i++)
        {
            char c = raw[i];
            if (c != '\\') { sb.Append(c); continue; }
            char n = raw[++i];
            switch (n)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16));
                    i += 4;
                    break;
                default: sb.Append(n); break; // " \ /
            }
        }
        return sb.ToString();
    }

    private static bool IsIntegerSpelling(string raw)
    {
        int i = raw.Length > 0 && raw[0] == '-' ? 1 : 0;
        if (i == raw.Length) return false;
        for (; i < raw.Length; i++)
            if (raw[i] is < '0' or > '9') return false;
        return true;
    }

    private static Workbook Build(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new GridlineFormatException("root must be an object");

        if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || Str(format) != "gridline")
            throw new GridlineFormatException("format must be \"gridline\"");

        // An integer is spelled as digits with an optional leading '-' (no fraction or exponent). One too large
        // for Int64 is still an integer, and a positive one is a future version.
        if (!root.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.Number || !IsIntegerSpelling(ver.GetRawText()))
            throw new GridlineFormatException("version missing or not an integer");
        if (!ver.TryGetInt64(out long version))
        {
            string raw = ver.GetRawText();
            if (raw[0] == '-') throw new GridlineFormatException("invalid version " + raw);
            throw new GridlineFormatException("unsupported version " + raw);
        }
        if (version > CurrentVersion) throw new GridlineFormatException("unsupported version " + version);
        if (version < 1) throw new GridlineFormatException("invalid version " + version);

        var names = new List<KeyValuePair<string, string>>();
        if (root.TryGetProperty("names", out var namesEl))
        {
            if (namesEl.ValueKind != JsonValueKind.Object) throw new GridlineFormatException("names must be an object");
            foreach (var p in namesEl.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) throw new GridlineFormatException("name target must be a string");
                names.Add(new(PropName(p), Str(p.Value)));
            }
        }

        if (version == 1)
        {
            // Migration (CR1.5): one sheet Sheet1. Names pass through unchanged: the single-sheet FromSnapshot
            // qualifies an unqualified refersTo with Sheet1 and accepts one already qualified with it (or #REF!).
            return Workbook.FromSnapshot(ReadCells(root), names);
        }

        if (!root.TryGetProperty("activeSheet", out var activeEl) || activeEl.ValueKind != JsonValueKind.String)
            throw new GridlineFormatException("activeSheet missing or not a string");
        if (!root.TryGetProperty("sheets", out var sheetsEl) || sheetsEl.ValueKind != JsonValueKind.Array || sheetsEl.GetArrayLength() == 0)
            throw new GridlineFormatException("sheets must be a non-empty array");
        var sheets = new List<SheetSnapshot>();
        foreach (var se in sheetsEl.EnumerateArray())
        {
            if (se.ValueKind != JsonValueKind.Object) throw new GridlineFormatException("sheet must be an object");
            if (!se.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                throw new GridlineFormatException("sheet name missing or not a string");
            if (!se.TryGetProperty("cells", out _)) throw new GridlineFormatException("sheet cells missing");
            sheets.Add(new SheetSnapshot(Str(nameEl), ReadCells(se)));
        }
        return Workbook.FromSnapshot(sheets, names, Str(activeEl));
    }

    private static List<KeyValuePair<string, string>> ReadCells(JsonElement root)
    {
        var cells = new List<KeyValuePair<string, string>>();
        if (root.TryGetProperty("cells", out var cellsEl))
        {
            if (cellsEl.ValueKind != JsonValueKind.Array) throw new GridlineFormatException("cells must be an array");
            foreach (var e in cellsEl.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2) throw new GridlineFormatException("cell entry must be a two-string array");
                var a = e[0];
                var b = e[1];
                if (a.ValueKind != JsonValueKind.String || b.ValueKind != JsonValueKind.String)
                    throw new GridlineFormatException("cell entry must be a two-string array");
                cells.Add(new(Str(a), Str(b)));
            }
        }

        return cells;
    }
}
