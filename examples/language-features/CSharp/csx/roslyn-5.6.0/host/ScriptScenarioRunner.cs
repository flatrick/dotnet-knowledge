using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DotNetKnowledge.CSharpScriptHost;

internal sealed class ScriptScenarioRunner
{
    public async Task<ScriptSuccess> RunAsync(
        ScenarioDescriptor descriptor,
        string scenarioDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioDirectory);

        var entryPath = Path.GetFullPath(Path.Combine(scenarioDirectory, descriptor.Entry));
        var code = await File.ReadAllTextAsync(entryPath, cancellationToken);
        var options = ScriptOptions.Default
            .AddReferences(
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(JsonDocument).Assembly,
                typeof(System.Xml.Linq.XDocument).Assembly)
            .WithFilePath(entryPath);

        var standardOutput = new StringWriter(CultureInfo.InvariantCulture);
        var standardError = new StringWriter(CultureInfo.InvariantCulture);
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        Console.SetOut(standardOutput);
        Console.SetError(standardError);
        try
        {
            var script = CSharpScript.Create<object?>(code, options, typeof(ScriptGlobals));
            var diagnostics = script.Compile()
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
                .ToArray();
            if (diagnostics.Length != 0)
            {
                throw new CompilationErrorException(
                    "Script compilation produced diagnostics.",
                    diagnostics.ToImmutableArray());
            }

            var state = await script.RunAsync(
                new ScriptGlobals(
                    descriptor.Arguments.ToArray(),
                    descriptor.Globals?.Prefix ?? "",
                    cancellationToken),
                cancellationToken);

            return new ScriptSuccess(
                descriptor.Id,
                "api",
                state.ReturnValue?.GetType().FullName,
                JsonSerializer.SerializeToElement(state.ReturnValue),
                SplitLines(standardOutput.ToString()),
                SplitLines(standardError.ToString()),
                1);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private static IReadOnlyList<string> SplitLines(string output)
    {
        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return [];
        }

        return (normalized.EndsWith('\n') ? normalized[..^1] : normalized).Split('\n');
    }
}
