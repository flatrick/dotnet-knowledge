using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.CSharpScriptHost;

internal enum ScriptHostKind
{
    Api,
    Csi
}

internal sealed record ScenarioDescriptor(
    string Id,
    string Entry,
    IReadOnlyList<string> SupportFiles,
    IReadOnlyList<ScriptHostKind> Hosts,
    IReadOnlyList<string> Arguments,
    ScriptGlobalsInput? Globals,
    IReadOnlyList<string> Submissions,
    IReadOnlyDictionary<ScriptHostKind, ScenarioExpectation> Expectations)
{
    public IReadOnlyList<string> Validate(string scenarioDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioDirectory);

        var errors = new List<string>();
        var supportFiles = SupportFiles ?? [];
        var hosts = Hosts ?? [];
        var submissions = Submissions ?? [];
        var expectations = Expectations ?? new Dictionary<ScriptHostKind, ScenarioExpectation>();

        AddMissingCollectionError(errors, SupportFiles, "supportFiles");
        AddMissingCollectionError(errors, Hosts, "hosts");
        AddMissingCollectionError(errors, Arguments, "arguments");
        AddMissingCollectionError(errors, Submissions, "submissions");
        AddMissingCollectionError(errors, Expectations, "expectations");

        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("Scenario ID is required.");
        }

        if (string.IsNullOrWhiteSpace(Entry))
        {
            errors.Add("Scenario entry is required.");
        }

        if (hosts.Count == 0)
        {
            errors.Add("At least one host is required.");
        }

        foreach (var host in hosts
            .GroupBy(host => host)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(host => host))
        {
            errors.Add($"Duplicate host: {HostName(host)}.");
        }

        foreach (var host in hosts.Distinct().OrderBy(host => host))
        {
            if (!expectations.ContainsKey(host))
            {
                errors.Add($"Missing expectation for host: {HostName(host)}.");
            }
        }

        foreach (var host in expectations.Keys.Except(hosts).OrderBy(host => host))
        {
            errors.Add($"Unexpected expectation for host: {HostName(host)}.");
        }

        foreach (var (host, expectation) in expectations.OrderBy(pair => pair.Key))
        {
            if (expectation is null)
            {
                errors.Add($"Expectation must not be null: {HostName(host)}.");
                continue;
            }

            AddNullExpectationCollectionError(
                errors,
                expectation.StandardOutput,
                host,
                "standardOutput");
            AddNullExpectationCollectionError(
                errors,
                expectation.StandardError,
                host,
                "standardError");
        }

        ValidatePath(errors, scenarioDirectory, Entry, "Entry", requiresScriptExtension: true);

        foreach (var supportFile in supportFiles)
        {
            ValidatePath(errors, scenarioDirectory, supportFile, "Support file", requiresScriptExtension: false);
        }

        if (supportFiles.Contains(Entry, StringComparer.Ordinal))
        {
            errors.Add($"Entry must not also be a support file: {Entry}.");
        }

        foreach (var supportFile in supportFiles
            .GroupBy(path => path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            errors.Add($"Duplicate support file: {supportFile}.");
        }

        if (submissions.Count > 0 && !string.Equals(submissions[0], Entry, StringComparison.Ordinal))
        {
            errors.Add($"The first submission must match entry: {Entry}.");
        }

        foreach (var submission in submissions)
        {
            ValidatePath(errors, scenarioDirectory, submission, "Submission", requiresScriptExtension: true);

            if (!string.Equals(submission, Entry, StringComparison.Ordinal) &&
                !supportFiles.Contains(submission, StringComparer.Ordinal))
            {
                errors.Add($"Submission is not listed by entry or support files: {submission}.");
            }
        }

        return errors.OrderBy(error => error, StringComparer.Ordinal).ToArray();
    }

    private static void ValidatePath(
        ICollection<string> errors,
        string scenarioDirectory,
        string? relativePath,
        string pathKind,
        bool requiresScriptExtension)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            errors.Add($"{pathKind} path is required.");
            return;
        }

        string fullPath;
        string relative;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(scenarioDirectory, relativePath));
            relative = Path.GetRelativePath(scenarioDirectory, fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Invalid {pathKind.ToLowerInvariant()} path: {relativePath}.");
            return;
        }

        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            errors.Add($"Path escapes the scenario directory: {relativePath}.");
            return;
        }

        if (requiresScriptExtension && !relativePath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{pathKind} must have a .csx extension: {relativePath}.");
        }

        if (!File.Exists(fullPath))
        {
            errors.Add($"Path does not exist: {relativePath}.");
        }
    }

    private static string HostName(ScriptHostKind host) => host.ToString().ToLowerInvariant();

    private static void AddMissingCollectionError(
        ICollection<string> errors,
        object? collection,
        string memberName)
    {
        if (collection is null)
        {
            errors.Add($"Required collection is missing: {memberName}.");
        }
    }

    private static void AddNullExpectationCollectionError(
        ICollection<string> errors,
        object? collection,
        ScriptHostKind host,
        string memberName)
    {
        if (collection is null)
        {
            errors.Add($"Expectation collection must not be null: {HostName(host)}.{memberName}.");
        }
    }
}

internal sealed record ScriptGlobalsInput(string Prefix = "");

internal sealed record ScenarioExpectation(
    [property: JsonRequired] int ExitCode,
    [property: JsonRequired] string? ReturnType,
    [property: JsonRequired] JsonElement? ReturnValue,
    [property: JsonRequired] IReadOnlyList<string> StandardOutput,
    [property: JsonRequired] IReadOnlyList<string> StandardError,
    [property: JsonRequired] int CompletedSubmissionCount);
