using System.Globalization;
using System.Runtime.CompilerServices;
using Gridline.Core.Addressing;

namespace Gridline.Core.Parsing;

/// <summary>Recursive-descent / precedence-climbing parser for formula bodies (§5.2-5.4).</summary>
internal static class Parser
{
    private sealed class SyntaxError : Exception { }

    /// <summary>Maximum recursive (non-left-spine) nesting of any AST the parser returns (§5.4); deeper is #PARSE!.</summary>
    internal const int MaxDepth = 256;

    /// <summary>Parses a formula body (no leading '='). Never throws.</summary>
    public static ParsedFormula Parse(string formulaBody, IFunctionArity arity)
    {
        try
        {
            if (!Tokenizer.TryTokenize(formulaBody, out var tokens)) return Fail();
            var p = new State(tokens, arity);
            Node root = p.Compare();
            if (p.Cur.Kind != TokenKind.End) return Fail();
            return new ParsedFormula(root, false);
        }
        catch (SyntaxError) { return Fail(); }
        catch (InsufficientExecutionStackException) { return Fail(); }

        static ParsedFormula Fail() => new(null, true);
    }

    private sealed class State
    {
        private readonly List<Token> _t;
        private readonly IFunctionArity _arity;
        private int _i;
        // Recursive depth (walker recursion, left spines excluded) of the node most recently returned by a Parse* method.
        private int _h;

        private void Raise(int h)
        {
            if (h > MaxDepth) throw new SyntaxError();
            _h = h;
        }

        public State(List<Token> tokens, IFunctionArity arity) { _t = tokens; _arity = arity; }

        public Token Cur => _t[_i];

        private bool IsOp(params string[] ops) =>
            Cur.Kind == TokenKind.Op && Array.IndexOf(ops, Cur.Text) >= 0;

        public Node Compare() => Expr(1);

        private static int Prec(string op) => op switch
        {
            "=" or "<>" or "<" or ">" or "<=" or ">=" => 1,
            "&" => 2,
            "+" or "-" => 3,
            "*" or "/" => 4,
            "^" => 5,
            _ => 0,
        };

        // Precedence climbing (all binary operators are left-associative). One method instead of one per level keeps
        // the stack cost of a nesting level small, so MaxDepth levels fit comfortably in a 1 MB thread.
        private Node Expr(int minPrec)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            Node left = Postfix();
            int h = _h;
            bool first = true;
            while (Cur.Kind == TokenKind.Op)
            {
                string op = Cur.Text;
                int prec = Prec(op);
                if (prec < minPrec || prec == 0) break;
                _i++;
                // Walkers recurse into the leftmost operand of a chain (only the spine itself is iterated), so it costs a level.
                if (first) { h++; first = false; }
                Node right = prec == 5 ? Postfix() : Expr(prec + 1);
                left = new BinaryNode(op, left, right);
                h = Math.Max(h, _h + 1); Raise(h);
            }
            return left;
        }

        private Node Postfix()
        {
            Node n = Unary();
            while (IsOp("%")) { _i++; n = new PercentNode(n); Raise(_h + 1); }
            return n;
        }

        private Node Unary()
        {
            if (IsOp("-", "+"))
            {
                char op = Cur.Text[0]; _i++;
                RuntimeHelpers.EnsureSufficientExecutionStack();
                var operand = Unary();
                Raise(_h + 1);
                return new UnaryNode(op, operand);
            }
            return Primary();
        }

        private static RefNode ToRef(Token t)
        {
            string s = t.Text;
            if (!CellAddress.TryParse(s, out var a)) throw new SyntaxError();
            bool colAbs = s[0] == '$';
            int letters = colAbs ? 1 : 0;
            while (letters < s.Length && char.IsAsciiLetter(s[letters])) letters++;
            bool rowAbs = letters < s.Length && s[letters] == '$';
            return new RefNode(a.Row, a.Col, rowAbs, colAbs);
        }

        private Node Primary()
        {
            Token t = Cur;
            _h = 0;
            switch (t.Kind)
            {
                case TokenKind.Number:
                {
                    _i++;
                    double v = double.Parse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(v)) throw new SyntaxError();
                    return new NumberNode(v, t.Text);
                }
                case TokenKind.String: _i++; return new StringNode(t.Text);
                case TokenKind.Bool: _i++; return new BoolNode(t.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
                case TokenKind.Error: _i++; return new ErrorNode(t.Text);
                case TokenKind.Name: _i++; return new NameNode(t.Text);
                case TokenKind.CellRef:
                {
                    _i++;
                    RefNode first = ToRef(t);
                    if (IsOp(":"))
                    {
                        _i++;
                        if (Cur.Kind != TokenKind.CellRef) throw new SyntaxError();
                        Token t2 = Cur; _i++;
                        return new RangeNode(first, ToRef(t2));
                    }
                    return first;
                }
                case TokenKind.LParen:
                {
                    _i++;
                    Node inner = Compare();
                    if (Cur.Kind != TokenKind.RParen) throw new SyntaxError();
                    _i++;
                    Raise(_h + 1);
                    return new ParenNode(inner);
                }
                case TokenKind.Function:
                    return Call(t);
                default:
                    throw new SyntaxError();
            }
        }

        private Node Call(Token fn)
        {
            _i++; // function token
            if (Cur.Kind != TokenKind.LParen) throw new SyntaxError();
            _i++;
            var args = new List<Node?>();
            int h = 0;
            if (Cur.Kind == TokenKind.RParen)
            {
                _i++; // empty list: zero args
            }
            else
            {
                while (true)
                {
                    if (Cur.Kind == TokenKind.Comma || Cur.Kind == TokenKind.RParen) args.Add(null);
                    else { args.Add(Compare()); h = Math.Max(h, _h); }
                    if (Cur.Kind == TokenKind.Comma) { _i++; continue; }
                    if (Cur.Kind == TokenKind.RParen) { _i++; break; }
                    throw new SyntaxError();
                }
            }
            if (_arity.TryGetArity(fn.Text.ToUpperInvariant(), out int min, out int max)
                && (args.Count < min || args.Count > max))
                throw new SyntaxError();
            Raise(h + 1);
            return new CallNode(fn.Text, args);
        }
    }
}
