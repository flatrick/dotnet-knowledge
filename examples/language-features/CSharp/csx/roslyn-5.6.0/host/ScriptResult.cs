using System.Text.Json;

namespace DotNetKnowledge.CSharpScriptHost;

internal sealed record ScriptSuccess(
    string ScenarioId,
    string Host,
    string? ReturnType,
    JsonElement ReturnValue,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    int CompletedSubmissionCount);

internal sealed record ScriptFailure(
    string Kind,
    string Type,
    string Message,
    IReadOnlyList<string> Diagnostics);
