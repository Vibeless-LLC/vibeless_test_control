namespace Gridline.IO;

/// <summary>Options for CSV import into a sheet (CR1.6, §8.5).</summary>
public sealed record CsvOptions(char Delimiter = ',');
