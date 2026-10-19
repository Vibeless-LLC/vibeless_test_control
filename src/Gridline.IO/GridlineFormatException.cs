namespace Gridline.IO;

/// <summary>Thrown when a .gridline file is malformed or violates the format schema (§8.2).</summary>
public sealed class GridlineFormatException : Exception
{
    public GridlineFormatException(string message) : base(message) { }
    public GridlineFormatException(string message, Exception inner) : base(message, inner) { }
}
