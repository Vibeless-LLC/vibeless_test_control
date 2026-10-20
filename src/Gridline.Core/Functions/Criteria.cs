using Gridline.Core.Evaluation;

namespace Gridline.Core.Functions;

/// <summary>A parsed SUMIF/COUNTIF/AVERAGEIF criterion (§7.5).</summary>
internal sealed class Criteria
{
    private enum Op { Eq, Ne, Lt, Gt, Le, Ge }
    private enum Shape { Empty, NonBlank, Number, Boolean, Text }

    private readonly Shape _shape;
    private readonly Op _op;
    private readonly double _number;
    private readonly bool _bool;
    private readonly string _text;
    private readonly string _textUpper;

    private Criteria(Shape shape, Op op, double number = 0, bool b = false, string text = "")
    {
        _shape = shape;
        _op = op;
        _number = number;
        _bool = b;
        _text = text;
        _textUpper = text.ToUpperInvariant();
    }

    /// <summary>
    /// Builds the criterion for a criteria value. A number or boolean means equality, a Blank value is the empty
    /// criterion, text is parsed as an optional operator and an operand. The caller handles an Error value.
    /// </summary>
    public static Criteria FromValue(CellValue c)
    {
        switch (c.Kind)
        {
            case ValueKind.Number: return new Criteria(Shape.Number, Op.Eq, number: c.Number);
            case ValueKind.Boolean: return new Criteria(Shape.Boolean, Op.Eq, b: c.Boolean);
            case ValueKind.Text: return Parse(c.Text);
            default: return new Criteria(Shape.Empty, Op.Eq);
        }
    }

    private static Criteria Parse(string s)
    {
        Op op = Op.Eq;
        int len = 0;
        if (s.StartsWith("<=", StringComparison.Ordinal)) { op = Op.Le; len = 2; }
        else if (s.StartsWith(">=", StringComparison.Ordinal)) { op = Op.Ge; len = 2; }
        else if (s.StartsWith("<>", StringComparison.Ordinal)) { op = Op.Ne; len = 2; }
        else if (s.StartsWith('=')) { op = Op.Eq; len = 1; }
        else if (s.StartsWith('<')) { op = Op.Lt; len = 1; }
        else if (s.StartsWith('>')) { op = Op.Gt; len = 1; }
        string operand = s.Substring(len);

        if (operand.Length == 0)
        {
            if (op == Op.Eq) return new Criteria(Shape.Empty, Op.Eq);
            if (op == Op.Ne) return new Criteria(Shape.NonBlank, Op.Ne);
            return new Criteria(Shape.Text, op, text: ""); // ordering against the empty text
        }
        if (NumberGrammar.TryParse(operand, out double n)) return new Criteria(Shape.Number, op, number: n);
        if (operand.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new Criteria(Shape.Boolean, op, b: true);
        if (operand.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new Criteria(Shape.Boolean, op, b: false);
        return new Criteria(Shape.Text, op, text: operand);
    }

    private bool Test(int cmp) => _op switch
    {
        Op.Eq => cmp == 0,
        Op.Ne => cmp != 0,
        Op.Lt => cmp < 0,
        Op.Gt => cmp > 0,
        Op.Le => cmp <= 0,
        _ => cmp >= 0,
    };

    /// <summary>Whether a Blank cell matches (so callers can count blank positions without visiting them).</summary>
    public bool MatchesBlank => Matches(CellValue.Blank);

    public bool Matches(CellValue cell)
    {
        if (cell.Kind == ValueKind.Error) return false; // error cells never match
        switch (_shape)
        {
            case Shape.Empty:
                return cell.Kind == ValueKind.Blank || (cell.Kind == ValueKind.Text && cell.Text.Length == 0);
            case Shape.NonBlank:
                return cell.Kind != ValueKind.Blank;
            case Shape.Number:
                return cell.Kind == ValueKind.Number && Test(cell.Number < _number ? -1 : cell.Number > _number ? 1 : 0);
            case Shape.Boolean:
                return cell.Kind == ValueKind.Boolean && Test(cell.Boolean == _bool ? 0 : cell.Boolean ? 1 : -1);
            default:
                if (_op == Op.Eq) return cell.Kind == ValueKind.Text && Wildcard.IsMatch(_text, cell.Text);
                if (_op == Op.Ne) return !(cell.Kind == ValueKind.Text && Wildcard.IsMatch(_text, cell.Text));
                return cell.Kind == ValueKind.Text &&
                       Test(Math.Sign(string.CompareOrdinal(cell.Text.ToUpperInvariant(), _textUpper)));
        }
    }
}
