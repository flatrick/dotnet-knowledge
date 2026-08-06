using System.Diagnostics;

namespace DotNetKnowledge.Corpus.Tests.Toolchains;

/// <summary>
/// Locates Visual Studio's <c>MSBuild.exe</c>, the only host that builds the corpus's legacy
/// non-SDK net48 projects. <c>dotnet build</c> restores their <c>PackageReference</c> items and then
/// resolves none of them, because a non-SDK project consumes package assets through NuGet targets
/// that ship with Visual Studio rather than with the SDK.
/// <para>
/// The lookup is the one <c>scripts/verify-feature-floors.cs</c> already performs. That script and a
/// test project cannot share code, so this is a deliberate second copy rather than a defect; keep
/// the vswhere arguments identical between them.
/// </para>
/// </summary>
internal sealed class VisualStudioMsBuild
{
    internal const string VswhereArguments =
        "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe";

    private readonly Func<bool> isWindows;
    private readonly Func<string> getProgramFilesX86;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, string, string> run;

    public VisualStudioMsBuild()
        : this(
            OperatingSystem.IsWindows,
            () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            File.Exists,
            RunCapturingStandardOutput)
    {
    }

    internal VisualStudioMsBuild(
        Func<bool> isWindows,
        Func<string> getProgramFilesX86,
        Func<string, bool> fileExists,
        Func<string, string, string> run)
    {
        this.isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
        this.getProgramFilesX86 = getProgramFilesX86 ?? throw new ArgumentNullException(nameof(getProgramFilesX86));
        this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        this.run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>
    /// Resolves the host, or explains what was inspected and did not answer. The reason names the
    /// exact paths rather than saying "Visual Studio is not installed", for the reason
    /// <see cref="RequiredToolchainsTests"/> names its host: a caller who cannot see what was looked
    /// at reads an environmental gap as a defect in the corpus.
    /// </summary>
    public bool TryResolve(out string path, out string reason)
    {
        path = string.Empty;
        if (!isWindows())
        {
            reason =
                "Visual Studio's MSBuild.exe is unavailable: it requires Windows, and the corpus's " +
                "legacy non-SDK net48 C# projects build under no other host.";
            return false;
        }

        var vswhere = Path.Combine(
            getProgramFilesX86(),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (!fileExists(vswhere))
        {
            reason =
                $"vswhere was not found at '{vswhere}', so Visual Studio's MSBuild.exe could not be " +
                "located. Install Visual Studio with the Microsoft.Component.MSBuild component.";
            return false;
        }

        string output;
        try
        {
            output = run(vswhere, VswhereArguments);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            reason =
                $"vswhere at '{vswhere}' could not be run: " +
                $"{exception.GetType().FullName}: {exception.Message}";
            return false;
        }

        var candidate = output
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && fileExists(line));
        if (candidate is null)
        {
            reason =
                $"'{vswhere} {VswhereArguments}' named no existing MSBuild.exe. It printed: " +
                $"{(string.IsNullOrWhiteSpace(output) ? "(nothing)" : output.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Trim())}";
            return false;
        }

        path = candidate;
        reason = string.Empty;
        return true;
    }

    private static string RunCapturingStandardOutput(string executable, string arguments)
    {
        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return standardOutput;
    }
}
