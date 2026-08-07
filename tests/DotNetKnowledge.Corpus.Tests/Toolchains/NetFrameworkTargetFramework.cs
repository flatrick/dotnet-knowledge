namespace DotNetKnowledge.Corpus.Tests.Toolchains;

/// <summary>
/// Tells a .NET Framework target framework moniker (<c>net48</c>) apart from a CoreCLR one
/// (<c>net7.0</c>, <c>net10.0</c>). The two need different build reference assemblies and,
/// critically, different execution: CoreCLR runs through <c>dotnet &lt;assembly.dll&gt;</c>, while
/// a Framework assembly is its own launchable <c>.exe</c> and has no <c>dotnet</c> host at all.
/// A dotted suffix is what the TFM grammar uses to mean CoreCLR; an undotted numeric suffix means
/// Framework.
/// </summary>
internal static class NetFrameworkTargetFramework
{
    private const string Prefix = "net";

    public static bool IsFramework(string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);

        if (!targetFramework.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = targetFramework[Prefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsAsciiDigit);
    }
}
