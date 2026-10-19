namespace Gridline.Core.Parsing;

/// <summary>Immutable formula syntax tree (§5.3).</summary>
internal abstract record Node;

internal sealed record NumberNode(double Value, string Lexeme) : Node;
internal sealed record StringNode(string Value) : Node;
internal sealed record BoolNode(bool Value) : Node;
internal sealed record ErrorNode(string Code) : Node;
internal sealed record RefNode(int Row, int Col, bool RowAbs, bool ColAbs) : Node;
/// <summary>Corners are kept as written; normalize at evaluation time.</summary>
internal sealed record RangeNode(RefNode First, RefNode Second) : Node;
internal sealed record NameNode(string Name) : Node;
internal sealed record UnaryNode(char Op, Node Operand) : Node;
internal sealed record PercentNode(Node Operand) : Node;
internal sealed record BinaryNode(string Op, Node Left, Node Right) : Node;
/// <summary><c>Name</c> is as written; a null argument means omitted.</summary>
internal sealed record CallNode(string Name, IReadOnlyList<Node?> Args) : Node;
internal sealed record ParenNode(Node Inner) : Node;

internal sealed record ParsedFormula(Node? Root, bool IsParseError);
