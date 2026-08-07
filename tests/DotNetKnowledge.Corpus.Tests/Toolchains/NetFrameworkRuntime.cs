namespace DotNetKnowledge.Corpus.Tests.Toolchains;

/// <summary>
/// Checks whether this machine can execute a built .NET Framework assembly. Unlike a CoreCLR
/// target, running one needs no host of its own -- the built <c>.exe</c> launches directly -- so the
/// only preflight is that the in-box CLR it depends on is actually present.
/// <para>
/// The lookup mirrors <see cref="VisualStudioMsBuild"/>'s testable <c>TryResolve</c> shape and
/// inspects the same in-box root <c>scripts/verify-feature-floors.cs</c> already uses to find its
/// native-ceiling compilers: <c>%WINDIR%\Microsoft.NET\Framework64\v4.0.30319</c>. That directory is
/// shared and upgraded in place across every .NET Framework 4.x release, so its presence is evidence
/// of *some* 4.x runtime rather than a specific one -- which is all a probe launching a net48
/// assembly needs to know.
/// </para>
/// </summary>
internal sealed class NetFrameworkRuntime
{
    private readonly Func<bool> isWindows;
    private readonly Func<string> getWindowsFolder;
    private readonly Func<string, bool> fileExists;

    public NetFrameworkRuntime()
        : this(
            OperatingSystem.IsWindows,
            () => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            File.Exists)
    {
    }

    internal NetFrameworkRuntime(
        Func<bool> isWindows,
        Func<string> getWindowsFolder,
        Func<string, bool> fileExists)
    {
        this.isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
        this.getWindowsFolder = getWindowsFolder ?? throw new ArgumentNullException(nameof(getWindowsFolder));
        this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    public bool TryResolve(out string reason)
    {
        if (!isWindows())
        {
            reason =
                ".NET Framework execution requires Windows; the corpus's net48 runtime cases run " +
                "under no other host.";
            return false;
        }

        var mscorlib = Path.Combine(
            getWindowsFolder(),
            "Microsoft.NET",
            "Framework64",
            "v4.0.30319",
            "mscorlib.dll");
        if (!fileExists(mscorlib))
        {
            reason =
                $"The in-box .NET Framework 4.x runtime was not found at '{mscorlib}'. " +
                "Install .NET Framework 4.8.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
