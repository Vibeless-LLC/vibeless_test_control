using System.Globalization;
using System.Text;

namespace Gridline.Core;

/// <summary>An immutable computed cell value (§4.4). <c>default(CellValue)</c> is Blank.</summary>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly string? _text;
    private readonly string? _error;

    private CellValue(ValueKind kind, double number, string? text, bool boolean, string? error)
    {
        Kind = kind;
        Number = number;
        _text = text;
        Boolean = boolean;
        _error = error;
    }

    public ValueKind Kind { get; }
    public double Number { get; }
    public string Text => _text ?? "";
    public bool Boolean { get; }
    public string Error => _error ?? "";

    public static CellValue Blank => default;

    public static CellValue FromNumber(double value)
    {
        if (!double.IsFinite(value)) return FromError(ErrorCodes.Num);
        if (value == 0) value = 0.0; // normalizes -0
        return new CellValue(ValueKind.Number, value, null, false, null);
    }

    public static CellValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CellValue(ValueKind.Text, 0, value, false, null);
    }

    public static CellValue FromBoolean(bool value) => new(ValueKind.Boolean, 0, null, value, null);

    public static CellValue FromError(string code)
    {
        if (!ErrorCodes.IsValid(code))
            throw new ArgumentException($"Unknown error code '{code}'.", nameof(code));
        return new CellValue(ValueKind.Error, 0, null, false, code);
    }

    public string ToCanonicalString()
    {
        switch (Kind)
        {
            case ValueKind.Number:
                return "N:" + Number.ToString("R", CultureInfo.InvariantCulture);
            case ValueKind.Text:
                var sb = new StringBuilder(Text.Length + 2).Append("T:");
                foreach (var c in Text)
                {
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            case ValueKind.Boolean:
                return Boolean ? "B:TRUE" : "B:FALSE";
            case ValueKind.Error:
                return "E:" + Error;
            default:
                return "BLANK";
        }
    }

    public override string ToString() => ToCanonicalString();

    public bool Equals(CellValue other) =>
        Kind == other.Kind && Number.Equals(other.Number) && Boolean == other.Boolean &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        string.Equals(Error, other.Error, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CellValue o && Equals(o);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Number, Boolean, Text, Error);

    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);
    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);
}
