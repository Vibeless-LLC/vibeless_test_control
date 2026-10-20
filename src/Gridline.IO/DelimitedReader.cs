using System.Text;

namespace Gridline.IO;

/// <summary>RFC 4180 record/field reader shared by CSV import and clipboard paste (§8.5 Records, Fields).</summary>
public static class DelimitedReader
{
    public const int MaxRecords = 100_000;
    public const int MaxFields = 702;

    /// <summary>
    /// Splits <paramref name="text"/> into records of fields. Records end at CRLF, LF or CR (mixed allowed); a final
    /// terminator adds no row. A quoted field may hold the delimiter, <c>""</c> and line breaks; a <c>"</c> inside an
    /// unquoted field is literal. Throws <see cref="GridlineFormatException"/> for an unterminated quote, more than
    /// 100,000 records or more than 702 fields in a record.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        int i = 0, n = text.Length;
        while (i < n)
        {
            // Start of a field.
            if (text[i] == '"')
            {
                i++;
                bool closed = false;
                while (i < n)
                {
                    char c = text[i++];
                    if (c != '"') { sb.Append(c); continue; }
                    if (i < n && text[i] == '"') { sb.Append('"'); i++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) throw new GridlineFormatException("Unterminated quoted field.");
                // Lenient: characters between the closing quote and the next delimiter/terminator are kept literally.
                while (i < n && text[i] != delimiter && text[i] != '\r' && text[i] != '\n') sb.Append(text[i++]);
            }
            else
            {
                while (i < n && text[i] != delimiter && text[i] != '\r' && text[i] != '\n') sb.Append(text[i++]);
            }

            AddField(sb.ToString());
            sb.Clear();

            if (i >= n) break;
            char t = text[i++];
            if (t == delimiter)
            {
                if (i >= n) AddField(""); // trailing delimiter: one more empty field
                continue;
            }
            if (t == '\r' && i < n && text[i] == '\n') i++;
            EndRecord();
        }
        if (fields.Count > 0) EndRecord();
        return records;

        void AddField(string f)
        {
            if (fields.Count >= MaxFields) throw new GridlineFormatException($"A record has more than {MaxFields} fields.");
            fields.Add(f);
        }

        void EndRecord()
        {
            if (records.Count >= MaxRecords) throw new GridlineFormatException($"More than {MaxRecords} records.");
            records.Add(fields.ToArray());
            fields.Clear();
        }
    }
}

/// <summary>Maps an imported field to a cell input (§8.5 Mapping).</summary>
public static class DelimitedMapping
{
    public static string ToInput(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Length == 0) return "";
        return field[0] is '=' or '\'' ? "'" + field : field;
    }
}
