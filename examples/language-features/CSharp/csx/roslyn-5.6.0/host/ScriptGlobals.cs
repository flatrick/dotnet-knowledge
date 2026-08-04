namespace DotNetKnowledge.CSharpScriptHost;

public sealed class ScriptGlobals(
    string[] args,
    string prefix,
    CancellationToken cancellationToken)
{
    public string[] Args { get; } = args;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public string Prefix { get; } = prefix;

    public string Format(string value) => $"{Prefix}: {value}";
}
