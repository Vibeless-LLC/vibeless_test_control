# Decisions

Non-trivial implementation choices, with spec section references.

## Task 1: Addressing

- **Leading-zero rows accepted (§4.1).** `A01` parses as `A1` (Excel accepts it). Canonical `ToString()` yields `A1`.
- **Solution file format (§3.1).** `Gridline.sln` is the classic format (`dotnet new sln --format sln`), since the .NET 10 default is `.slnx` and the build contract names `Gridline.sln`.
- **Shared build settings (§3.1).** `Directory.Build.props` sets LangVersion latest, Nullable, ImplicitUsings, TreatWarningsAsErrors and InvariantGlobalization for all projects.
- **`CellAddress.TryParse(ReadOnlySpan<char>)` is `internal`** and used by `RangeAddress`; the public API is the `string` overload only (no allocation when parsing ranges).
