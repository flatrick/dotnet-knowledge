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

        var canonicalScenarioDirectory = Path.GetFullPath(scenarioDirectory);
        var submissionPaths = (descriptor.Submissions.Count == 0
                ? [descriptor.Entry]
                : descriptor.Submissions)
            .Select(path => Path.GetFullPath(Path.Combine(canonicalScenarioDirectory, path)))
            .ToArray();
        var supportFilePaths = descriptor.SupportFiles.ToDictionary(
            path => path,
            path => Path.GetFullPath(Path.Combine(canonicalScenarioDirectory, path)),
            StringComparer.Ordinal);
        var arguments = descriptor.Arguments
            .Select(argument => supportFilePaths.TryGetValue(argument, out var supportFilePath)
                ? supportFilePath
                : argument)
            .ToArray();
        var entryPath = submissionPaths[0];
        var code = await File.ReadAllTextAsync(entryPath, cancellationToken);
        var options = ScriptOptions.Default
            .AddReferences(
                MetadataReference.CreateFromFile(
                    System.Reflection.Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(
                    System.Reflection.Assembly.Load("System.Memory").Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(JsonDocument).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Xml.Linq.XDocument).Assembly.Location))
            .WithFilePath(entryPath)
            .WithSourceResolver(new RestrictedSourceResolver(canonicalScenarioDirectory))
            .WithMetadataResolver(new BclMetadataResolver());

        var standardOutput = new StringWriter(CultureInfo.InvariantCulture);
        var standardError = new StringWriter(CultureInfo.InvariantCulture);
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        Console.SetOut(standardOutput);
        Console.SetError(standardError);
        try
        {
            var script = CSharpScript.Create<object?>(code, options, typeof(ScriptGlobals));
            ThrowIfCompilationHasWarningsOrErrors(script);

            ScriptState<object?> state;
            var completedSubmissionCount = 0;
            try
            {
                state = await script.RunAsync(
                    new ScriptGlobals(
                        arguments,
                        descriptor.Globals?.Prefix ?? "",
                        cancellationToken),
                    cancellationToken);
                completedSubmissionCount++;

                foreach (var submissionPath in submissionPaths.Skip(1))
                {
                    code = await File.ReadAllTextAsync(submissionPath, cancellationToken);
                    var continuation = state.Script.ContinueWith<object?>(
                        code,
                        options.WithFilePath(submissionPath));
                    ThrowIfCompilationHasWarningsOrErrors(continuation);
                    state = await continuation.RunFromAsync(state, cancellationToken);
                    completedSubmissionCount++;
                }
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new ScriptSuccess(
                descriptor.Id,
                "api",
                state.ReturnValue?.GetType().FullName,
                JsonSerializer.SerializeToElement(state.ReturnValue),
                SplitLines(standardOutput.ToString()),
                SplitLines(standardError.ToString()),
                completedSubmissionCount);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private static void ThrowIfCompilationHasWarningsOrErrors(Script<object?> script)
    {
        var diagnostics = script.Compile()
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();
        if (diagnostics.Length != 0)
        {
            throw new CompilationErrorException(
                "Script compilation produced diagnostics.",
                diagnostics);
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
