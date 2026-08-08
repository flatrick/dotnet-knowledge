#!/usr/bin/env dotnet
#:property PublishAot=false
#:property Nullable=enable

// A throwaway probe of the *host's* compilers, not an MCP server. It measures which of the nine
// compiler binaries this repository drives accept `/preferreduilang`, and whether the switch
// actually changes the diagnostic language on this machine.
//
// The question it answers: is `/preferreduilang` support a property of the generation (pre-Roslyn
// vs Roslyn), of the language (csc vs vbc), or of the individual binary? Each compiler compiles a
// file that errors and a file that does not, at baseline and under two spellings of the switch, and
// the first diagnostic line is printed verbatim so a rejection, an ignored warning, and a genuine
// language change are all distinguishable. The VSLANG and DOTNET_CLI_UI_LANGUAGE routes are then
// tried for the compilers that reject the switch.
//
// It cannot say what a compiler does on a host with different satellite languages installed: a
// compiler that already prints English here is silent about whether it honored anything. That is a
// fact about the machine, not the binary, and the printed baseline line is what shows which case a
// given row is.
//
// The Microsoft.Net.Compilers 1.3.2 pair comes from `.artifacts/period-compilers/`, which the
// dotnet-code-examples corpus's verify-feature-floors.cs downloads; the modern pair is found with
// the same `vswhere` query that script uses. Missing binaries are reported, not guessed at.
//
// Windows only.
//
//   dotnet run --file scripts/probes/probe-preferreduilang.cs
//   dotnet run --file scripts/probes/probe-preferreduilang.cs -- --root C:\path\to\checkout

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

var repoRoot = FindCheckoutRoot();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help":
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --file scripts/probes/probe-preferreduilang.cs -- [options]");
            Console.WriteLine();
            Console.WriteLine("  --root <path>   Checkout supplying .artifacts/period-compilers (default: this script's checkout).");
            Console.WriteLine("  -h, --help      This text.");
            return 0;
        case "--root" when i + 1 < args.Length:
            repoRoot = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine($"probe-preferreduilang: unrecognized argument '{args[i]}'.");
            return 2;
    }
}

var work = Path.Combine(Path.GetTempPath(), "prefuilang-work");
Directory.CreateDirectory(work);

var csBad = Path.Combine(work, "bad.cs");
File.WriteAllText(csBad, "class C { void M() { Undefined(); } }\n");
var csGood = Path.Combine(work, "good.cs");
File.WriteAllText(csGood, "class C { void M() { } }\n");

var vbBad = Path.Combine(work, "bad.vb");
File.WriteAllText(vbBad, "Class C\n  Sub M()\n    Undefined()\n  End Sub\nEnd Class\n");
var vbGood = Path.Combine(work, "good.vb");
File.WriteAllText(vbGood, "Class C\n  Sub M()\n  End Sub\nEnd Class\n");

var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
var fx = Path.Combine(windows, "Microsoft.NET", "Framework64");
var packaged = Path.Combine(repoRoot, ".artifacts", "period-compilers", "microsoft.net.compilers.1.3.2", "tools");
var msbuild = FindMsBuild();
var vs = msbuild is null ? null : Path.Combine(Path.GetDirectoryName(msbuild)!, "Roslyn");

Console.WriteLine($"checkout: {repoRoot}");
Console.WriteLine($"packaged: {packaged}");
Console.WriteLine($"VS Roslyn: {vs ?? "(no Visual Studio MSBuild found via vswhere)"}");
Console.WriteLine();

(string Label, string Path, string Lang)[] compilers =
[
    ("csc v2.0.50727 (in-box)", Path.Combine(fx, "v2.0.50727", "csc.exe"), "cs"),
    ("csc v3.5 (in-box)",       Path.Combine(fx, "v3.5", "csc.exe"), "cs"),
    ("csc v4.0.30319 (in-box)", Path.Combine(fx, "v4.0.30319", "csc.exe"), "cs"),
    ("csc 1.3.2 (packaged)",    Path.Combine(packaged, "csc.exe"), "cs"),
    ("csc VS Roslyn (modern)",  vs is null ? "" : Path.Combine(vs, "csc.exe"), "cs"),
    ("vbc v2.0.50727 (in-box)", Path.Combine(fx, "v2.0.50727", "vbc.exe"), "vb"),
    ("vbc v3.5 (in-box)",       Path.Combine(fx, "v3.5", "vbc.exe"), "vb"),
    ("vbc v4.0.30319 (in-box)", Path.Combine(fx, "v4.0.30319", "vbc.exe"), "vb"),
    ("vbc 1.3.2 (packaged)",    Path.Combine(packaged, "vbc.exe"), "vb"),
    ("vbc VS Roslyn (modern)",  vs is null ? "" : Path.Combine(vs, "vbc.exe"), "vb"),
];

foreach (var (label, path, lang) in compilers)
{
    Console.WriteLine("==== " + label + " ====");
    Console.WriteLine("path: " + (path.Length == 0 ? "(unresolved)" : path) + (File.Exists(path) ? "" : "  <<< MISSING"));
    if (!File.Exists(path)) { Console.WriteLine(); continue; }

    var bad = lang == "cs" ? csBad : vbBad;
    var good = lang == "cs" ? csGood : vbGood;
    var outFlag = "/out:\"" + Path.Combine(work, "o_" + Guid.NewGuid().ToString("N") + ".dll") + "\"";

    foreach (var (variant, extra) in new[]
             {
                 ("baseline", ""),
                 ("/preferreduilang:en-US", "/preferreduilang:en-US "),
                 ("/preferreduilang:en", "/preferreduilang:en "),
             })
    {
        var r1 = RunIt(path, extra + "/nologo /target:library " + outFlag + " \"" + bad + "\"");
        var r2 = RunIt(path, extra + "/nologo /target:library " + outFlag + " \"" + good + "\"");
        Console.WriteLine($"  [{variant}] bad: exit={r1.Exit} | {First(r1.Output)}");
        Console.WriteLine($"  [{variant}] good: exit={r2.Exit} | {First(r2.Output)}");
    }

    // Environment-variable route, for compilers that reject the switch.
    foreach (var (varName, value) in new[] { ("VSLANG", "1033"), ("DOTNET_CLI_UI_LANGUAGE", "en-US") })
    {
        var r = RunIt(path, "/nologo /target:library " + outFlag + " \"" + bad + "\"", (varName, value));
        Console.WriteLine($"  [{varName}={value}] bad: exit={r.Exit} | {First(r.Output)}");
    }

    Console.WriteLine();
}

return 0;

static string First(string output)
{
    var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    return lines.Length == 0 ? "(no output)" : string.Join("  ||  ", lines.Take(2));
}

static (int Exit, string Output) RunIt(string exe, string args, (string Name, string Value)? env = null)
{
    var psi = new ProcessStartInfo(exe, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };
    if (env is { } e) { psi.Environment[e.Name] = e.Value; }
    using var p = Process.Start(psi)!;
    var so = p.StandardOutput.ReadToEnd();
    var se = p.StandardError.ReadToEnd();
    p.WaitForExit();
    return (p.ExitCode, so + se);
}

static string? FindMsBuild()
{
    var vswhere = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswhere))
    {
        return null;
    }

    var psi = new ProcessStartInfo(vswhere, "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
    };
    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();

    return stdout.Split('\n').Select(l => l.Trim()).FirstOrDefault(File.Exists);
}

static string FindCheckoutRoot([CallerFilePath] string scriptPath = "")
{
    var probesDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath))
        ?? throw new InvalidOperationException($"Could not determine directory of script path: {scriptPath}");
    var scriptsDir = Path.GetDirectoryName(probesDir)
        ?? throw new InvalidOperationException($"Could not determine scripts directory above: {probesDir}");
    return Path.GetDirectoryName(scriptsDir)
        ?? throw new InvalidOperationException($"Could not determine checkout root above: {scriptsDir}");
}
