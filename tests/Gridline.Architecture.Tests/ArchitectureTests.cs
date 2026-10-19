using System.Reflection;
using Gridline.Cli;
using Gridline.Core;
using Gridline.IO;
using NetArchTest.Rules;

namespace Gridline.Architecture.Tests;

// Spec 3.2 / 3.3. Namespace filters use exact sub-namespaces: ResideInNamespace is a prefix match.
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(Workbook).Assembly;
    private static readonly Assembly IO = typeof(GridlineFile).Assembly;
    private static readonly Assembly Cli = typeof(CliApp).Assembly;

    private const string CoreNs = "Gridline.Core";

    private static void AssertNoDependency(Assembly asm, string ns, params string[] forbidden)
    {
        var result = Types.InAssembly(asm)
            .That().ResideInNamespace(ns)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();
        Assert.True(result.IsSuccessful,
            $"{ns} must not depend on [{string.Join(", ", forbidden)}]. Failing types: " +
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_does_not_depend_on_IO_Cli_App_Windows_or_file_console_APIs() =>
        AssertNoDependency(Core, CoreNs,
            "Gridline.IO", "Gridline.Cli", "Gridline.App", "System.Windows",
            "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.Console");

    [Fact]
    public void IO_does_not_depend_on_App_Cli_Windows_or_Console() =>
        AssertNoDependency(IO, "Gridline.IO",
            "Gridline.App", "Gridline.Cli", "System.Windows", "System.Console");

    [Fact]
    public void Cli_does_not_depend_on_App_or_Windows() =>
        AssertNoDependency(Cli, "Gridline.Cli", "Gridline.App", "System.Windows");

    // App_does_not_depend_on_Cli: added in Phase 2 with the App project.

    [Fact]
    public void Addressing_does_not_depend_on_other_Core_namespaces() =>
        AssertNoDependency(Core, "Gridline.Core.Addressing",
            "Gridline.Core.Parsing", "Gridline.Core.Evaluation", "Gridline.Core.Functions",
            "Gridline.Core.Dependencies", "Gridline.Core.Commands");

    [Fact]
    public void Parsing_does_not_depend_on_Evaluation_Functions_Dependencies_Commands() =>
        AssertNoDependency(Core, "Gridline.Core.Parsing",
            "Gridline.Core.Evaluation", "Gridline.Core.Functions",
            "Gridline.Core.Dependencies", "Gridline.Core.Commands");

    [Fact]
    public void Evaluation_does_not_depend_on_Commands_or_Dependencies() =>
        AssertNoDependency(Core, "Gridline.Core.Evaluation",
            "Gridline.Core.Commands", "Gridline.Core.Dependencies");

    [Fact]
    public void Functions_does_not_depend_on_Parsing_Dependencies_Commands() =>
        AssertNoDependency(Core, "Gridline.Core.Functions",
            "Gridline.Core.Parsing", "Gridline.Core.Dependencies", "Gridline.Core.Commands");

    [Fact]
    public void Dependencies_does_not_depend_on_Functions_or_Commands() =>
        AssertNoDependency(Core, "Gridline.Core.Dependencies",
            "Gridline.Core.Functions", "Gridline.Core.Commands");

    [Fact]
    public void No_public_mutable_static_state()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        foreach (var asm in new[] { Core, IO, Cli })
        {
            foreach (var type in asm.GetTypes())
            {
                if (!type.IsVisible) continue; // only publicly reachable members count
                foreach (var f in type.GetFields(flags))
                {
                    if (f.Name.Contains('<') || f.IsLiteral || f.IsInitOnly) continue;
                    offenders.Add($"{type.FullName}.{f.Name} (field)");
                }
                foreach (var p in type.GetProperties(flags))
                {
                    if (p.Name.Contains('<')) continue;
                    if (p.SetMethod is { IsPublic: true }) offenders.Add($"{type.FullName}.{p.Name} (property)");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Public mutable static state: " + string.Join(", ", offenders));
    }
}
