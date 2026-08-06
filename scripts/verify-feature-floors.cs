#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Verify that every language-feature example actually requires the language version it is filed
// under, and report the ones whose requirement <LangVersion> cannot enforce.
//
// Why this exists: `/langversion:N` on a modern compiler is not the C# N compiler. Roslyn gates a
// construct on language version only where its binder explicitly calls CheckFeatureAvailability.
// Syntax-driven features (out variables, tuples, patterns, local functions) all got that call, so
// a too-low <LangVersion> rejects them. Semantic and attribute-driven features did not.
// Generalized async return types is the known case: `async ValueTask<T>`, and even a hand-rolled
// [AsyncMethodBuilder] type, compile clean under /langversion:5. Building the corpus at a pinned
// <LangVersion> therefore proves less than it appears to. The same holds for VB.
//
// So each feature group folder is probed twice, and escalated to a period compiler when the two
// probes disagree with expectations:
//
//   1. Compile the group standalone at its own language version. Must succeed, or the group needs
//      project context this script cannot reproduce and is reported INCONCLUSIVE.
//   2. Compile it at the language version one rung down the ladder.
//        rejected  -> GATED. <LangVersion> enforces the row. Nothing further to check.
//        accepted  -> suspicious. Escalate to step 3.
//   3. Compile it with a real compiler whose native ceiling is that lower version.
//        rejected  -> UNGATED. The example is genuine; Roslyn simply never gated the feature.
//        accepted  -> NOT-VERSION-SPECIFIC. The example does not demonstrate anything that
//                     requires its version. This is a defect in the example.
//      No period compiler for that boundary -> UNPROVEN, reported rather than assumed either way.
//
// A group may sit in a project whose pin is below the group's own version, and that is the corpus's
// deliberate model rather than a mistake: a project holds every row that compiles at its pin,
// including rows filed above it whose feature the compiler turns out not to gate, and the project's
// green build at the lower pin is what makes the ungatedness a checked fact instead of a note. Such
// a row is therefore compiled at its project's pin first, before the probe above runs:
//        accepted  -> the placement is evidence that the pin does not gate the row. The row then
//                     goes through the normal probe and reports its usual outcome, with its detail
//                     recording that it sits above the pin and compiles there anyway, and sdk-pin
//                     evidence where the probe itself found none.
//        rejected  -> retry at the row's own version, because this isolated compile is not the
//                     project build and a row the harness simply cannot build fails at every
//                     version alike.
//                       accepted there -> MISPLACED. The pin rejects a row that otherwise stands
//                                         up, so the project claims a row it cannot build.
//                       rejected there -> INCONCLUSIVE. The failure is the harness's, not the pin's.
//
// Period compilers, all used at their native ceiling rather than behind a /langversion flag. Both
// languages ship in the same places, so each row below serves whichever language is being probed:
//   C# 6 / VB 14 -- Microsoft.Net.Compilers 1.3.2, downloaded to .artifacts/ on first run
//   C# 5 / VB 11 -- the in-box compiler at %WINDIR%\Microsoft.NET\Framework64\v4.0.30319
//   C# 3 / VB 9  -- the in-box compiler at ...\v3.5
//   C# 2         -- the in-box compiler at ...\v2.0.50727
//
// There is no C# 4 compiler and no C# 1.x compiler. .NET 4.5 upgraded v4.0.30319's csc in place
// from C# 4 to C# 5, so the C# 4 binary is gone from any machine carrying .NET 4.5 or later, and
// .NET 1.0/1.1 do not install on a current Windows. Those two floors are reported UNPROVEN rather
// than guessed at. Older reference assemblies are not the missing piece -- the C# 2 and C# 3
// compilers read net48 reference assemblies without complaint, so no era-specific projects are
// needed to use them.
//
// VB's gaps are VB 10 and VB 12, reported UNPROVEN for the same reason. The v2.0.50727 vbc tops out
// at VB 8, below the ladder's first rung, so it is never a floor and is not registered. VB 14 is the
// highest native VB ceiling that exists, so every VB floor above it rests on the modern compiler
// under a /langversion pin alone.
//
// Every verdict therefore also records what kind of evidence it rests on, because a pinned modern
// compiler and a period compiler are not the same claim:
//   native-ceiling -- a compiler whose native ceiling is the rung below settled it. This does not
//                     drift as SDKs ship, and it is the only evidence that can settle the question
//                     in both directions.
//   legacy-pin     -- a compiler that does not top out at the rung below, held there instead with
//                     /langversion. One-directional: a rejection settles it -- the construct is
//                     version-dependent. An acceptance settles nothing (reported UNPROVEN), because
//                     the gate can be missing from this compiler for the same reason it is missing
//                     from the modern one. This gate only ever fires for the C# 1.0 and 4.0 floors,
//                     both resolved by an in-box, pre-Roslyn csc; the C# 6 / VB 14 boundary is
//                     settled at its native ceiling in step 3 by Microsoft.Net.Compilers 1.3.2 and
//                     never reaches this gate.
//   sdk-pin        -- the installed SDK's compiler under /langversion, and nothing else. This says
//                     what the current toolchain gates, which is a fact that drifts.
//   none           -- nothing compiled here bears on the floor.
//
// The two corpora are shaped differently and are discovered differently:
//   C# -- each CSharp_v* project directory holds its own version folders, one group folder deep.
//   VB -- each family has one shared src/ tree and every pinned project selects row folders from it
//         with Compile Include globs, minus its Compile Remove globs. Reading those items is the
//         only way to learn which rows a VB project actually compiles.
//
// Windows only, and needs Visual Studio's MSBuild: the corpus's net48 C# projects are non-SDK XML
// and resolve PackageReference assets only through the NuGet targets that ship with Visual Studio.
//
// Run from the repo root:
//   dotnet scripts/verify-feature-floors.cs
//   dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0
//   dotnet scripts/verify-feature-floors.cs -- --language vb
//   dotnet scripts/verify-feature-floors.cs -- --json
//   dotnet scripts/verify-feature-floors.cs -- --offline
//
// Exit code is 1 when any group is MISPLACED or NOT-VERSION-SPECIFIC -- the two outcomes that mean
// the corpus is wrong. UNGATED, UNPROVEN and INCONCLUSIVE are findings about the toolchain's reach,
// not corpus defects, and do not fail the run.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var projectFilter = (string?)null;
var emitJson = false;
var offline = false;
var language = "cs";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            projectFilter = args[++i];
            break;
        case "--language" when i + 1 < args.Length:
            language = args[++i].ToLowerInvariant();
            break;
        case "--json":
            emitJson = true;
            break;
        case "--offline":
            offline = true;
            break;
        case "-h":
        case "--help":
            Console.WriteLine("Usage: dotnet scripts/verify-feature-floors.cs [-- --language <cs|vb>] [--project <name>] [--json] [--offline]");
            return 0;
        default:
            Console.Error.WriteLine($"verify-feature-floors: unknown argument: {args[i]}");
            return 2;
    }
}

if (language is not ("cs" or "vb"))
{
    Console.Error.WriteLine($"verify-feature-floors: unknown language '{language}'; expected cs or vb.");
    return 2;
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
if (repoRoot is null)
{
    Console.Error.WriteLine("verify-feature-floors: could not locate the repo root.");
    return 2;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("verify-feature-floors: Windows only -- the corpus's net48 projects need Visual Studio's MSBuild.");
    return 2;
}

var msbuild = FindMsBuild();
if (msbuild is null)
{
    Console.Error.WriteLine("verify-feature-floors: could not locate Visual Studio's MSBuild.exe via vswhere.");
    return 2;
}

// Each language's identity within this probe: how to find its files, spell its language versions on
// the compiler command line, and walk its version ladder.
var csharpProfile = new LanguageProfile(
    Name: "C#",
    SourceExtension: ".cs",
    ProjectExtension: ".csproj",
    CompilerFileName: "csc.exe",
    Ladder: Versions.Ladder,
    InBoxCompilers: [("2.0", "v2.0.50727"), ("3.0", "v3.5"), ("5.0", "v4.0.30319")],
    PackagedCompilerCeiling: "6.0",
    FolderVersion: ParseFolderVersion,
    LangVersionArg: LangVersionArg,
    LegacyLangVersionArg: LegacyLangVersionArg,
    CeilingVersion: CSharpCeilingVersion,
    IsEnvironmentError: IsEnvironmentError,
    ControlSource: "internal class ReferenceSetControl { }\n");

var vbProfile = new LanguageProfile(
    Name: "VB",
    SourceExtension: ".vb",
    ProjectExtension: ".vbproj",
    CompilerFileName: "vbc.exe",
    Ladder: Versions.VbLadder,
    // No v2.0.50727 entry: that vbc tops out at VB 8, which is below the ladder, so no row can ever
    // have it as a floor.
    InBoxCompilers: [("9", "v3.5"), ("11", "v4.0.30319")],
    PackagedCompilerCeiling: "14",
    FolderVersion: VbFolderVersion,
    // Every VB ladder value is spelled the same on the command line, unlike C#'s ISO-1 / ISO-2.
    LangVersionArg: version => version,
    // VB has no equivalent of the C# legacy-pin tier. The in-box vbc predates /langversion as a
    // meaningful switch -- v3.5 answers it with BC2007, "option 'langversion' is unknown and
    // ignored" -- so holding one of them to a rung would silently probe its own ceiling instead.
    LegacyLangVersionArg: _ => null,
    CeilingVersion: VbCeilingVersion,
    IsEnvironmentError: IsVbEnvironmentError,
    ControlSource: "Friend Class ReferenceSetControl\nEnd Class\n");

var profile = language == "vb" ? vbProfile : csharpProfile;

var modernCompiler = Path.Combine(Path.GetDirectoryName(msbuild)!, "Roslyn", profile.CompilerFileName);
if (!File.Exists(modernCompiler))
{
    Console.Error.WriteLine($"verify-feature-floors: no {profile.CompilerFileName} beside MSBuild at '{modernCompiler}'.");
    return 2;
}

// Period compilers, keyed by the language version each one natively tops out at.
var periodCompilers = new Dictionary<string, string>();

// The .NET Framework keeps its old compilers side by side, and each one's language ceiling is
// fixed. The C# list deliberately has no v4.0 entry: .NET 4.5 upgraded the v4.0.30319 compiler in
// place from C# 4 to C# 5, so no C# 4 compiler survives on a modern machine. C# 4 is therefore the
// one floor in that range that cannot be settled by a native ceiling.
var frameworkRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64");

foreach (var (version, directory) in profile.InBoxCompilers)
{
    var candidate = Path.Combine(frameworkRoot, directory, profile.CompilerFileName);
    if (File.Exists(candidate))
    {
        periodCompilers[version] = candidate;
    }
}

// Microsoft.Net.Compilers 1.3.2 carries both compilers, so one download serves the C# 6 boundary
// and the VB 14 one.
var packagedCompiler = await AcquirePackagedCompilerAsync(repoRoot, offline, profile);
if (packagedCompiler is not null)
{
    periodCompilers[profile.PackagedCompilerCeiling] = packagedCompiler;
}

var skippedProjects = new List<string>();

// Discovery throws InvalidOperationException on a layout it cannot read -- a Compile glob whose
// shape is not "<directory>/**/*.vb", a source file at an unexpected depth under src/, a version
// folder naming no ladder rung. Failing loudly on those is deliberate, because a silently skipped
// row is far worse; only the shape of the failure would otherwise be wrong. A malformed layout is a
// setup problem, so it takes the documented exit 2 and prints the message the exception already
// carries rather than a stack trace.
Discovery discovery;
try
{
    discovery = profile.Name == "VB"
        ? DiscoverVbProjects(repoRoot, profile, projectFilter, skippedProjects)
        : DiscoverCSharpProjects(repoRoot, profile, projectFilter, skippedProjects);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"verify-feature-floors: {ex.Message}");
    return 2;
}

if (discovery.Fatal is not null)
{
    Console.Error.WriteLine(discovery.Fatal);
    return 2;
}

var workRoot = Path.Combine(repoRoot, ".artifacts", "feature-floors");
Directory.CreateDirectory(workRoot);

// The same group folder is duplicated across the cumulative projects, and its floor is a property
// of the files rather than of the project holding them. Probe each distinct (scope, version,
// content) once and reuse the verdict. The scope is derived from the project's resolved compile
// inputs rather than declared, so a project whose reference set, constants or compiler options
// differ from another's cannot silently reuse its verdict -- a net10 reference set and a net48 one
// can disagree about whether a row compiles at all.
var floorCache = new Dictionary<string, Verdict>(StringComparer.Ordinal);
var results = new List<Result>();

foreach (var project in discovery.Projects)
{
    Console.Error.WriteLine($"verify-feature-floors: {project.Name} (ceiling {project.Ceiling})");

    ProjectInputs? inputs = null;
    var scope = "";

    // Resolving references costs a full MSBuild evaluation, so it still happens at most once per
    // project and not at all for a project with nothing to classify. It can no longer be deferred
    // past a cache read, though: the cache key names the reference set a verdict was measured
    // against, and that is only knowable once the references are resolved. False means the project
    // resolved nothing usable and has been skipped.
    bool EnsureInputs()
    {
        if (inputs is not null)
        {
            return true;
        }

        inputs = ResolveProjectInputs(msbuild, project.ProjectPath, profile);
        if (inputs.References.Length == 0)
        {
            // Clear it again, so a later call re-reads the same failure instead of short-circuiting
            // on a non-null field that was just declared unusable.
            inputs = null;
            skippedProjects.Add($"{project.Name}: MSBuild resolved no references");
            return false;
        }

        scope = ScopeOf(inputs);
        return true;
    }

    foreach (var row in project.Rows)
    {
        if (row.Files.Length == 0)
        {
            results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                "INCONCLUSIVE", $"group folder holds no {profile.SourceExtension} files", Evidence.None));
            continue;
        }

        // A row filed above its project's pin is a claim the project makes: that the row compiles
        // at the lower pin even though it is filed higher. Check exactly that claim, per (row, pin),
        // against this project's own reference set. The answer is not a property of the row alone,
        // so it stays out of the floor cache, which is keyed by row.
        string? abovePinNote = null;
        if (LadderIndex(profile.Ladder, row.Version) > LadderIndex(profile.Ladder, project.Ceiling))
        {
            if (!EnsureInputs())
            {
                break;
            }

            var pinArg = profile.LangVersionArg(project.Ceiling);
            if (pinArg is null)
            {
                results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                    "INCONCLUSIVE",
                    $"{profile.Name} {project.Ceiling} has no /langversion spelling, so the pin this row sits above cannot be compiled at",
                    Evidence.None));
                continue;
            }

            var atPin = Compile(profile, modernCompiler, pinArg, row.Files, inputs!, workRoot);
            if (!atPin.Succeeded)
            {
                // This isolated compile is not the project build, so a failure is not on its own
                // the project's fault. A row that needs sibling sources, needs /link rather than
                // /reference, or trips over anything else the harness cannot reproduce fails at
                // every language version alike. Retry at the row's own version to tell the two
                // apart: only a row that stands up there and falls over at the pin is misplaced.
                var ownArg = profile.LangVersionArg(row.Version);
                var atOwn = ownArg is null
                    ? null
                    : Compile(profile, modernCompiler, ownArg, row.Files, inputs!, workRoot);

                if (ownArg is null)
                {
                    results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                        "INCONCLUSIVE",
                        $"{profile.Name} {row.Version} has no /langversion spelling, so its failure at this project's pin of {profile.Name} {project.Ceiling} ({atPin.FirstError}) cannot be attributed to the pin",
                        Evidence.None));
                    continue;
                }

                if (!atOwn!.Succeeded)
                {
                    results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                        "INCONCLUSIVE",
                        $"does not compile standalone at /langversion:{ownArg} ({atOwn.FirstError}), so its failure at this project's pin of {profile.Name} {project.Ceiling} says nothing about the placement",
                        Evidence.None));
                    continue;
                }

                results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                    "MISPLACED",
                    $"{profile.Name} {row.Version} group in a project pinned to {project.Ceiling}: it compiles at /langversion:{ownArg} but not at the pin ({atPin.FirstError})",
                    Evidence.None));
                continue;
            }

            abovePinNote =
                $"filed above this project's pin and compiles at /langversion:{pinArg}, so {profile.Name} {project.Ceiling} does not gate it";
        }

        // Two shapes of exemption. A group exemption says the probe cannot see the feature at all,
        // so nothing is compiled. A bucket exemption says the bucket has no single previous version
        // to test against; the sources still have to stand on their own, so those keep step 1.
        var exemption = ExemptionReason(row.Group);
        if (exemption is not null)
        {
            results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
                "EXEMPT", WithAbovePinNote(exemption, abovePinNote),
                WithAbovePinEvidence(Evidence.None, abovePinNote)));
            continue;
        }

        var bucketExemption = ExemptionReason(row.VersionFolder);

        // Before the key, not after: the scope half of it is this project's resolved reference set.
        if (!EnsureInputs())
        {
            break;
        }

        var key = scope + "|" + row.Version + "|" + HashFiles(row.Files);
        if (!floorCache.TryGetValue(key, out var verdict))
        {
            verdict = bucketExemption is not null
                ? ProbeOwnVersion(profile, row.Version, row.Files, inputs!, workRoot, modernCompiler,
                    bucketExemption)
                : ProbeFloor(profile, row.Version, row.Files, inputs!, workRoot, modernCompiler,
                    periodCompilers);
            floorCache[key] = verdict;
        }

        results.Add(new Result(project.Name, project.Ceiling, row.Version, row.Group,
            verdict.Outcome, WithAbovePinNote(verdict.Detail, abovePinNote),
            WithAbovePinEvidence(verdict.Evidence, abovePinNote)));
    }
}

Report(profile, results, skippedProjects, periodCompilers, emitJson);

var failures = results.Count(r => r.Outcome is "MISPLACED" or "NOT-VERSION-SPECIFIC");
return failures > 0 ? 1 : 0;

// ---------------------------------------------------------------------------------------------

// A verdict is a property of the row, so it is cached and reported unchanged; whether the row sits
// above the reporting project's pin is a property of the placement. Carry the second alongside the
// first so an above-pin row never reads as an ordinary at-or-below-pin one.
static string WithAbovePinNote(string detail, string? abovePinNote) =>
    abovePinNote is null ? detail : $"{detail} -- {abovePinNote}";

// The at-the-pin compile is the installed SDK's compiler under a /langversion pin, so it is exactly
// sdk-pin evidence. Record it wherever the row's own verdict rests on nothing, or a consumer
// filtering out "none" throws away the only compile some of these rows ever get -- C# 1.2 rows have
// no /langversion spelling of their own, so the pin compile is all they have. A verdict that already
// rests on a stronger tier keeps it; this never downgrades one.
static string WithAbovePinEvidence(string evidence, string? abovePinNote) =>
    abovePinNote is not null && evidence == Evidence.None ? Evidence.SdkPin : evidence;

// Step 1 on its own, for buckets whose exemption is about the ladder rather than about the sources.
// The row still has to compile at the version it claims; what it cannot be asked is what happens
// one rung down, because the bucket spans many rungs.
static Verdict ProbeOwnVersion(
    LanguageProfile profile,
    string featureVersion,
    string[] files,
    ProjectInputs inputs,
    string workRoot,
    string modernCompiler,
    string exemption)
{
    var ownArg = profile.LangVersionArg(featureVersion);
    if (ownArg is null)
    {
        return new Verdict("UNPROVEN", $"{profile.Name} {featureVersion} has no /langversion spelling",
            Evidence.None);
    }

    var own = Compile(profile, modernCompiler, ownArg, files, inputs, workRoot);
    return own.Succeeded
        ? new Verdict("EXEMPT", exemption, Evidence.None)
        : new Verdict("INCONCLUSIVE",
            $"does not compile standalone at /langversion:{ownArg} ({own.FirstError})", Evidence.None);
}

static Verdict ProbeFloor(
    LanguageProfile profile,
    string featureVersion,
    string[] files,
    ProjectInputs inputs,
    string workRoot,
    string modernCompiler,
    Dictionary<string, string> periodCompilers)
{
    // Step 1 -- the group must stand on its own at its own language version, or nothing below
    // this can be interpreted.
    var ownArg = profile.LangVersionArg(featureVersion);
    if (ownArg is null)
    {
        return new Verdict("UNPROVEN", $"{profile.Name} {featureVersion} has no /langversion spelling",
            Evidence.None);
    }

    var own = Compile(profile, modernCompiler, ownArg, files, inputs, workRoot);
    if (!own.Succeeded)
    {
        return new Verdict("INCONCLUSIVE",
            $"does not compile standalone at /langversion:{ownArg} ({own.FirstError})", Evidence.None);
    }

    var floor = Below(featureVersion, profile);
    if (floor is null)
    {
        return new Verdict("BASELINE",
            $"{profile.Name} {featureVersion} is the oldest rung; there is no lower version to test against",
            Evidence.None);
    }

    var floorArg = profile.LangVersionArg(floor);
    if (floorArg is null)
    {
        return new Verdict("UNPROVEN", $"{profile.Name} {floor} has no /langversion spelling", Evidence.None);
    }

    // Step 2 -- the modern compiler, held down to the rung below.
    var gated = Compile(profile, modernCompiler, floorArg, files, inputs, workRoot);
    if (!gated.Succeeded)
    {
        return new Verdict("GATED", $"rejected at /langversion:{floorArg} ({gated.FirstError})",
            Evidence.SdkPin);
    }

    // Step 3 -- a compiler whose native ceiling is the rung below. Only this tier can settle the
    // question in both directions: a compiler that predates the feature and still accepts the code
    // proves the code does not need the feature.
    if (periodCompilers.TryGetValue(floor, out var periodCsc))
    {
        // Environment control. A compiler this old may simply be unable to read the resolved
        // reference set, and that failure says nothing about the language.
        var control = Compile(profile, periodCsc, langVersion: null, [TrivialSource(profile, workRoot)],
            inputs, workRoot);
        if (!control.Succeeded)
        {
            return new Verdict("INCONCLUSIVE",
                $"the {profile.Name} {floor} compiler cannot read this project's reference set ({control.FirstError})",
                Evidence.None);
        }

        var period = Compile(profile, periodCsc, langVersion: null, files, inputs, workRoot);
        if (period.Succeeded)
        {
            return new Verdict("NOT-VERSION-SPECIFIC",
                $"the {profile.Name} {floor} compiler accepts it, so nothing here requires {profile.Name} {featureVersion}",
                Evidence.NativeCeiling);
        }

        return profile.IsEnvironmentError(period.FirstError)
            ? new Verdict("INCONCLUSIVE",
                $"the {profile.Name} {floor} compiler could not process the group for a non-language reason ({period.FirstError})",
                Evidence.None)
            : new Verdict("UNGATED",
                $"accepted at /langversion:{floorArg} but rejected by the {profile.Name} {floor} compiler ({period.FirstError})",
                Evidence.NativeCeiling);
    }

    // Step 4 -- no compiler tops out at this rung, so fall back to the nearest higher-ceiling
    // compiler held to that setting with /langversion. LegacyLangVersionArg only spells 1.0-5.0, so
    // this fallback only ever fires for the C# 1.0 and 4.0 floors, and the gate it picks is always
    // an in-box, pre-Roslyn csc -- the C# 6 / VB 14 boundary is settled at its native ceiling in
    // step 3 by Microsoft.Net.Compilers 1.3.2 and never falls through to here. This is a second
    // implementation's gate rather than a native ceiling, so the evidence is one-directional: a
    // rejection proves the construct is version-dependent, but an acceptance proves nothing, because
    // a gate can be missing in both implementations for the same reason.
    // Acceptance therefore stays UNPROVEN and never accuses the example.
    var legacyArg = profile.LegacyLangVersionArg(floor);
    var gate = periodCompilers
        .Where(kv => LadderIndex(profile.Ladder, kv.Key) > LadderIndex(profile.Ladder, floor))
        .OrderBy(kv => LadderIndex(profile.Ladder, kv.Key))
        .Select(kv => (Ceiling: kv.Key, Path: kv.Value))
        .FirstOrDefault();

    if (legacyArg is not null && gate.Path is not null)
    {
        // Control run first. Unless this compiler can handle the files at its own ceiling, a
        // failure at the floor says nothing about language versions.
        var control = Compile(profile, gate.Path, langVersion: null, files, inputs, workRoot);
        if (!control.Succeeded)
        {
            return new Verdict("INCONCLUSIVE",
                $"the {profile.Name} {gate.Ceiling} compiler cannot process the group even at its own ceiling ({control.FirstError})",
                Evidence.None);
        }

        var legacy = Compile(profile, gate.Path, legacyArg, files, inputs, workRoot);
        if (!legacy.Succeeded)
        {
            return new Verdict("UNGATED",
                $"accepted at /langversion:{floorArg} by the modern compiler but rejected by the {profile.Name} {gate.Ceiling} compiler held to the same setting ({legacy.FirstError})",
                Evidence.LegacyPin);
        }

        return new Verdict("UNPROVEN",
            $"accepted at {profile.Name} {floor} by the modern compiler and by the {profile.Name} {gate.Ceiling} compiler held there, and no compiler topping out at {profile.Name} {floor} exists to settle it",
            Evidence.LegacyPin);
    }

    return new Verdict("UNPROVEN",
        $"accepted at /langversion:{floorArg}, and no compiler for {profile.Name} {floor} is available",
        Evidence.SdkPin);
}

// A compile that exercises only the reference set, used to tell a metadata problem apart from a
// language rejection. It has to be written in the language being probed: handing a .cs file to vbc
// fails the control run every time and turns every VB escalation into a false INCONCLUSIVE.
static string TrivialSource(LanguageProfile profile, string workRoot)
{
    var path = Path.Combine(workRoot, "control" + profile.SourceExtension);
    File.WriteAllText(path, profile.ControlSource);
    return path;
}

static CompileResult Compile(
    LanguageProfile profile,
    string compiler,
    string? langVersion,
    string[] files,
    ProjectInputs inputs,
    string workRoot)
{
    var outDir = Path.Combine(workRoot, "probe");
    Directory.CreateDirectory(outDir);

    // /noconfig is deliberately absent here: the compilers honor it only on the command line. Left
    // in the response file it is ignored, csc.rsp or vbc.rsp gets read anyway, and its
    // auto-references collide with the resolved set as CS1703 on the older compilers.
    var rsp = new StringBuilder();
    rsp.AppendLine("/nologo");

    if (profile.Name == "VB")
    {
        // vbc rejects /nostdlib+ with BC2007; the switch has no trailing sign in VB.
        rsp.AppendLine("/nostdlib");

        // With /nostdlib the compiler no longer supplies the VB runtime itself, and every probe
        // fails with BC2017 unless it is named explicitly.
        var vbRuntime = inputs.References.FirstOrDefault(reference =>
            Path.GetFileName(reference).Equals("Microsoft.VisualBasic.dll", StringComparison.OrdinalIgnoreCase));
        if (vbRuntime is null)
        {
            return new CompileResult(false, "BC2017: no Microsoft.VisualBasic.dll in the resolved reference set");
        }

        rsp.AppendLine($"/vbruntime:\"{vbRuntime}\"");

        // A VB compilation prepends RootNamespace to every declaration, so a probe that omits it
        // compiles the row in a different namespace than the project that ships it. csc has no
        // /rootnamespace switch, which is why this is inside the VB branch.
        if (!string.IsNullOrWhiteSpace(inputs.RootNamespace))
        {
            rsp.AppendLine($"/rootnamespace:{inputs.RootNamespace}");
        }
    }
    else
    {
        rsp.AppendLine("/nostdlib+");
    }

    rsp.AppendLine("/target:library");
    if (langVersion is not null)
    {
        rsp.AppendLine($"/langversion:{langVersion}");
    }
    if (!string.IsNullOrWhiteSpace(inputs.Defines))
    {
        rsp.AppendLine($"/define:{inputs.Defines}");
    }
    foreach (var option in inputs.Options)
    {
        rsp.AppendLine(option);
    }
    rsp.AppendLine($"/out:\"{Path.Combine(outDir, "probe.dll")}\"");
    foreach (var reference in inputs.References)
    {
        rsp.AppendLine($"/reference:\"{reference}\"");
    }
    foreach (var file in files)
    {
        rsp.AppendLine($"\"{file}\"");
    }

    var rspPath = Path.Combine(outDir, "probe.rsp");
    File.WriteAllText(rspPath, rsp.ToString());

    var psi = new ProcessStartInfo(compiler, $"/noconfig @\"{rspPath}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    // Prefer selecting by severity, not just by code: a warning line ahead of the actual error
    // must not be mistaken for it, or a GATED/UNGATED verdict ends up quoting the wrong
    // diagnostic. ": error " is tried first -- on an English toolchain this reproduces the
    // original literal match exactly, warnings excluded. Only when no line matches it at all (a
    // localized toolchain, where the severity word itself is translated) does this fall back to
    // matching any CSnnnn/BCnnnnn code, which can pick up a warning. That fallback trades severity
    // discrimination for not going silent -- the best available signal once the severity word
    // can't be matched by spelling, and strictly better than the old behavior of finding nothing.
    // CS codes are always 4 digits; BC codes run 4 or 5 (BC2001 vs. BC30002, BC36716, BC42024 --
    // all real codes this corpus has hit), so both need the wider count.
    var diagnosticCode = new Regex(@"\b(?:CS|BC)\d{4,5}\b");
    var output = stdout + stderr;
    var lines = output.Split('\n').Select(line => line.Trim()).ToArray();
    var firstError = lines.FirstOrDefault(line => line.Contains(": error ", StringComparison.Ordinal))
        ?? lines.FirstOrDefault(line => diagnosticCode.IsMatch(line))
        ?? (process.ExitCode == 0 ? "" : "no diagnostic text");

    // Keep only the severity word, code, and message; the absolute path in front is noise in a
    // report. The compiler's format is "<location>: <severity> <code>: <message>" -- the colon
    // and space right before the severity word are locale-independent even though the word itself
    // is not, so anchoring on the nearest ": " before the code keeps the severity word without
    // hardcoding its spelling.
    var match = diagnosticCode.Match(firstError);
    if (match.Success)
    {
        var marker = firstError.LastIndexOf(": ", match.Index, StringComparison.Ordinal);
        firstError = marker >= 0 ? firstError[(marker + 2)..] : firstError[match.Index..];
    }

    return new CompileResult(process.ExitCode == 0, firstError);
}

static ProjectInputs ResolveProjectInputs(string msbuild, string projectPath, LanguageProfile profile)
{
    Run(msbuild, $"\"{projectPath}\" -t:Restore -v:q -nologo");

    // VB's conditional-compilation state is not the raw DefineConstants property: the VB targets
    // fold in CONFIG, DEBUG, _MyType and the framework constants, and spell the result the way
    // /define: expects. VB also carries four compilation options and a set of project-level imports
    // that the sources rely on; leaving them out turns ordinary rows into BC30209 and BC30451
    // failures that read exactly like version gating.
    var query = profile.Name == "VB"
        ? "-getItem:ReferencePath -getItem:Import -getProperty:FinalDefineConstants"
          + " -getProperty:OptionExplicit -getProperty:OptionStrict -getProperty:OptionInfer"
          + " -getProperty:OptionCompare -getProperty:RootNamespace"
        : "-getItem:ReferencePath -getProperty:DefineConstants -getProperty:RootNamespace";

    var json = Run(msbuild, $"\"{projectPath}\" -t:ResolveReferences {query} -nologo");

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var references = Array.Empty<string>();
        root.TryGetProperty("Items", out var items);
        if (items.ValueKind == JsonValueKind.Object
            && items.TryGetProperty("ReferencePath", out var paths))
        {
            // Deduplicate by simple name, not by path. MSBuild can resolve the same assembly
            // identity from two places (a reference assembly and a package), which the modern
            // compiler tolerates but older ones reject outright with CS1703.
            references = paths.EnumerateArray()
                .Select(p => p.TryGetProperty("FullPath", out var full)
                    ? full.GetString()
                    : p.GetProperty("Identity").GetString())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();
        }

        root.TryGetProperty("Properties", out var properties);

        string? defines = null;
        var definesProperty = profile.Name == "VB" ? "FinalDefineConstants" : "DefineConstants";
        if (properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(definesProperty, out var define))
        {
            defines = define.GetString();
        }

        string? rootNamespace = null;
        if (properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("RootNamespace", out var declaredRoot))
        {
            rootNamespace = declaredRoot.GetString();
        }

        var options = new List<string>();
        if (profile.Name == "VB")
        {
            options.AddRange(VbOptions(properties, items));
        }

        return new ProjectInputs(references, defines, rootNamespace, options);
    }
    catch (JsonException)
    {
        return new ProjectInputs(Array.Empty<string>(), null, null, Array.Empty<string>());
    }
}

// The compilation options a VB project carries besides its references and constants. Option Infer
// is the one that matters most: the SDK turns it on, and without it every `Dim x = ...` in the
// corpus fails with BC30209 under Option Strict On.
static IEnumerable<string> VbOptions(JsonElement properties, JsonElement items)
{
    var options = new List<string>();

    if (properties.ValueKind == JsonValueKind.Object)
    {
        AddSwitch("OptionExplicit", "/optionexplicit");
        AddSwitch("OptionInfer", "/optioninfer");

        if (Property("OptionStrict") is { Length: > 0 } strict)
        {
            options.Add(strict.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                ? "/optionstrict:custom"
                : "/optionstrict" + (strict.Equals("On", StringComparison.OrdinalIgnoreCase) ? "+" : "-"));
        }

        if (Property("OptionCompare") is { Length: > 0 } compare)
        {
            options.Add("/optioncompare:" + compare.ToLowerInvariant());
        }
    }

    if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("Import", out var imports))
    {
        var namespaces = imports.EnumerateArray()
            .Select(i => i.GetProperty("Identity").GetString())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToArray();
        if (namespaces.Length > 0)
        {
            options.Add("/imports:" + string.Join(",", namespaces));
        }
    }

    return options;

    string? Property(string name) =>
        properties.TryGetProperty(name, out var value) ? value.GetString() : null;

    void AddSwitch(string name, string option)
    {
        if (Property(name) is { Length: > 0 } value)
        {
            options.Add(option + (value.Equals("On", StringComparison.OrdinalIgnoreCase) ? "+" : "-"));
        }
    }
}

// Each CSharp_v* project directory holds its own version folders, so the tree alone says which rows
// a project compiles.
static Discovery DiscoverCSharpProjects(
    string repoRoot,
    LanguageProfile profile,
    string? projectFilter,
    List<string> skippedProjects)
{
    var corpusRoot = Path.Combine(
        repoRoot, "examples", "language-features", "CSharp", "dotNetFramework", "v4.8");
    if (!Directory.Exists(corpusRoot))
    {
        return new Discovery([], $"verify-feature-floors: corpus not found at '{corpusRoot}'.");
    }

    var projectDirs = Directory
        .EnumerateDirectories(corpusRoot, "CSharp_v*")
        .Where(d => projectFilter is null || Path.GetFileName(d) == projectFilter)
        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (projectDirs.Length == 0)
    {
        return new Discovery([], projectFilter is null
            ? $"verify-feature-floors: no CSharp_v* projects under '{corpusRoot}'."
            : $"verify-feature-floors: no project named '{projectFilter}' under '{corpusRoot}'.");
    }

    var projects = new List<ProbeProject>();

    foreach (var projectDir in projectDirs)
    {
        var projectName = Path.GetFileName(projectDir);
        var csproj = Directory.EnumerateFiles(projectDir, "*" + profile.ProjectExtension).FirstOrDefault();
        if (csproj is null)
        {
            skippedProjects.Add($"{projectName}: no {profile.ProjectExtension}");
            continue;
        }

        var ceiling = ReadLangVersion(csproj, profile);
        if (ceiling is null)
        {
            skippedProjects.Add($"{projectName}: no <LangVersion> in {Path.GetFileName(csproj)}");
            continue;
        }

        var versionFolders = Directory
            .EnumerateDirectories(projectDir, "CSharp*")
            .Select(d => (Dir: d, Version: profile.FolderVersion(Path.GetFileName(d))))
            .Where(x => x.Version is not null)
            .OrderBy(x => LadderIndex(profile.Ladder, x.Version!))
            .ToArray();

        if (versionFolders.Length == 0)
        {
            skippedProjects.Add($"{projectName}: no CSharpN feature folders");
            continue;
        }

        var rows = new List<RowGroup>();
        foreach (var (versionDir, featureVersion) in versionFolders)
        {
            foreach (var groupDir in Directory.EnumerateDirectories(versionDir)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var files = Directory
                    .EnumerateFiles(groupDir, "*" + profile.SourceExtension, SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                rows.Add(new RowGroup(
                    Path.GetFileName(versionDir), featureVersion!, Path.GetFileName(groupDir), files));
            }
        }

        projects.Add(new ProbeProject(projectName, csproj, ceiling, rows));
    }

    return new Discovery(projects, null);
}

// Each VB family has one shared src/ tree, and a pinned project selects rows from it with Compile
// Include globs minus Compile Remove globs. The project file is therefore the only statement of
// which rows it owns -- the directory it sits in holds no sources at all.
static Discovery DiscoverVbProjects(
    string repoRoot,
    LanguageProfile profile,
    string? projectFilter,
    List<string> skippedProjects)
{
    var vbRoot = Path.Combine(repoRoot, "examples", "language-features", "VB.NET");
    var families = new[]
    {
        Path.Combine(vbRoot, "dotnet", "Net10"),
        Path.Combine(vbRoot, "dotNetFramework", "v4.8"),
    };

    foreach (var family in families)
    {
        if (!Directory.Exists(family))
        {
            return new Discovery([], $"verify-feature-floors: corpus not found at '{family}'.");
        }
    }

    var projects = new List<ProbeProject>();

    foreach (var family in families)
    {
        var sourceRoot = Path.Combine(family, "src");
        var familyProjects = new List<ProbeProject>();

        foreach (var projectPath in Directory
                     .EnumerateFiles(family, "*" + profile.ProjectExtension, SearchOption.AllDirectories)
                     .Where(p => !IsBuildOutput(p)))
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetRelativePath(vbRoot, projectDirectory).Replace('\\', '/');
            if (projectFilter is not null && projectName != projectFilter)
            {
                continue;
            }

            var ceiling = ReadLangVersion(projectPath, profile);
            if (ceiling is null)
            {
                skippedProjects.Add(
                    $"{projectName}: no usable <LangVersion> in {Path.GetFileName(projectPath)}");
                continue;
            }

            var rows = VbRows(projectPath, sourceRoot, profile);
            if (rows.Count == 0)
            {
                skippedProjects.Add($"{projectName}: compiles no row folders under src/");
                continue;
            }

            familyProjects.Add(new ProbeProject(projectName, projectPath, ceiling, rows));
        }

        // Ascending pin order, so the lowest project probes each row first and the pins above it
        // read the verdict out of the cache instead of paying to compile the row again.
        projects.AddRange(familyProjects
            .OrderBy(p => LadderIndex(profile.Ladder, p.Ceiling))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase));
    }

    if (projects.Count == 0)
    {
        return new Discovery([], projectFilter is null
            ? $"verify-feature-floors: no {profile.ProjectExtension} projects under '{vbRoot}'."
            : $"verify-feature-floors: no project named '{projectFilter}' under '{vbRoot}'.");
    }

    return new Discovery(projects, null);
}

// The row folders a single VB project compiles: its Compile Include globs resolved to files on
// disk, minus its Compile Remove globs resolved the same way, then bucketed by the
// src/<version folder>/<group>/ path each surviving file sits under. Honoring Remove is what keeps
// MyNamespaceHelpers attributed to the my/ projects alone; a directory-prefix reading would file it
// under every library project too, none of which compiles it.
static List<RowGroup> VbRows(string projectPath, string sourceRoot, LanguageProfile profile)
{
    var projectDirectory = Path.GetDirectoryName(projectPath)!;
    var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var compile in XDocument.Load(projectPath).Descendants()
                 .Where(element => element.Name.LocalName == "Compile"))
    {
        var includeGlob = compile.Attribute("Include")?.Value;
        if (!string.IsNullOrWhiteSpace(includeGlob))
        {
            included.UnionWith(ResolveGlob(projectPath, projectDirectory, includeGlob, profile));
        }

        var removeGlob = compile.Attribute("Remove")?.Value;
        if (!string.IsNullOrWhiteSpace(removeGlob))
        {
            removed.UnionWith(ResolveGlob(projectPath, projectDirectory, removeGlob, profile));
        }
    }

    included.ExceptWith(removed);

    var buckets = new Dictionary<(string Folder, string Group), List<string>>();
    foreach (var file in included)
    {
        var relative = Path.GetRelativePath(sourceRoot, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length < 3 || segments[0] == "..")
        {
            throw new InvalidOperationException(
                $"{projectPath} compiles '{file}', which is not a src/<version folder>/<group>/ row.");
        }

        var key = (segments[0], segments[1]);
        if (!buckets.TryGetValue(key, out var files))
        {
            files = [];
            buckets[key] = files;
        }

        files.Add(file);
    }

    return buckets
        .Select(bucket => new RowGroup(
            bucket.Key.Folder,
            VbRowVersion(bucket.Key.Folder)
                ?? throw new InvalidOperationException(
                    $"{projectPath} compiles rows from '{bucket.Key.Folder}', which names no VB version."),
            bucket.Key.Group,
            bucket.Value.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()))
        .OrderBy(row => LadderIndex(profile.Ladder, row.Version))
        .ThenBy(row => row.Group, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

// Every Compile Include/Remove glob in this corpus has the shape "<directory>/**/*.vb". Resolve that
// to the source files actually present under the directory rather than implementing a general glob
// matcher. A glob that does not fit this shape throws, so a future pattern change fails loudly
// instead of silently dropping rows from the probe.
static IEnumerable<string> ResolveGlob(
    string projectPath, string projectDirectory, string glob, LanguageProfile profile)
{
    var tailPattern = "**/*" + profile.SourceExtension;
    if (!glob.EndsWith(tailPattern, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Unrecognized Compile glob \"{glob}\" in {projectPath}: expected it to end in \"{tailPattern}\".");
    }

    var directoryPart = glob[..^tailPattern.Length];
    var directory = Path.GetFullPath(Path.Combine(projectDirectory, directoryPart))
        .TrimEnd(Path.DirectorySeparatorChar);

    return Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*" + profile.SourceExtension, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
        : [];
}

static bool IsBuildOutput(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

static string Run(string exe, string arguments)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return stdout;
}

static async Task<string?> AcquirePackagedCompilerAsync(string repoRoot, bool offline, LanguageProfile profile)
{
    var boundary = $"{profile.Name} {profile.PackagedCompilerCeiling}";
    var dir = Path.Combine(repoRoot, ".artifacts", "period-compilers",
        $"microsoft.net.compilers.{Versions.CompilerPackage}");
    var compiler = Path.Combine(dir, "tools", profile.CompilerFileName);
    if (File.Exists(compiler))
    {
        return compiler;
    }

    if (offline)
    {
        Console.Error.WriteLine(
            $"verify-feature-floors: --offline and no cached {boundary} compiler; boundaries at {boundary} will report UNPROVEN.");
        return null;
    }

    var url = $"https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers/{Versions.CompilerPackage}";
    Console.Error.WriteLine($"verify-feature-floors: downloading Microsoft.Net.Compilers {Versions.CompilerPackage}...");

    try
    {
        Directory.CreateDirectory(dir);
        var nupkg = Path.Combine(dir, "package.nupkg");

        using (var http = new HttpClient())
        await using (var stream = await http.GetStreamAsync(url))
        await using (var file = File.Create(nupkg))
        {
            await stream.CopyToAsync(file);
        }

        ZipFile.ExtractToDirectory(nupkg, dir, overwriteFiles: true);
        File.Delete(nupkg);
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
    {
        Console.Error.WriteLine(
            $"verify-feature-floors: could not fetch the {boundary} compiler ({ex.Message}); boundaries at {boundary} will report UNPROVEN.");
        return null;
    }

    return File.Exists(compiler) ? compiler : null;
}

static void Report(
    LanguageProfile profile,
    List<Result> results,
    List<string> skippedProjects,
    Dictionary<string, string> periodCompilers,
    bool emitJson)
{
    // Ladder order, not string order: VB's rungs are bare numbers, and sorting them as text puts 11
    // and 14 ahead of 9.
    var ceilings = periodCompilers.Keys
        .OrderBy(k => LadderIndex(profile.Ladder, k))
        .ToArray();

    if (emitJson)
    {
        var payload = new
        {
            periodCompilers = ceilings,
            skippedProjects,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    // Every group is listed under its outcome. Nothing is capped -- a quietly shortened report
    // reads as a clean corpus.
    var order = new[]
    {
        "MISPLACED", "NOT-VERSION-SPECIFIC", "UNGATED", "UNPROVEN", "INCONCLUSIVE",
        "EXEMPT", "BASELINE", "GATED",
    };

    foreach (var outcome in order)
    {
        var rows = results.Where(r => r.Outcome == outcome).ToArray();
        if (rows.Length == 0)
        {
            continue;
        }

        Console.WriteLine();
        Console.WriteLine($"{outcome} ({rows.Length})");

        if (outcome == "EXEMPT")
        {
            // One line per distinct (group, reason); the reason is the point, not the project
            // copies. Count copies on the same pair the lines are grouped by: a group whose detail
            // varies across projects -- an above-pin placement annotates it -- otherwise prints one
            // line per detail with every line claiming the whole group's copy count.
            foreach (var exemption in rows
                         .Select(r => (r.Group, r.Detail))
                         .Distinct()
                         .OrderBy(x => x.Group, StringComparer.Ordinal)
                         .ThenBy(x => x.Detail, StringComparer.Ordinal))
            {
                var copies = rows.Count(r => r.Group == exemption.Group && r.Detail == exemption.Detail);
                Console.WriteLine($"  {exemption.Group} ({copies} project copies)");
                Console.WriteLine($"      {exemption.Detail}");
            }

            continue;
        }

        if (outcome is "GATED" or "BASELINE")
        {
            // The healthy majority. Naming every project copy of every row would bury the rest.
            var groups = rows
                .Select(r => $"{profile.Name} {r.FeatureVersion} {r.Group}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal)
                .ToArray();
            var note = outcome == "GATED"
                ? "<LangVersion> enforces these"
                : $"{profile.Name} {profile.Ladder[0]} rows, with no lower version to test against";
            Console.WriteLine($"  {groups.Length} distinct groups across {rows.Length} project copies -- {note}.");
            continue;
        }

        foreach (var row in rows.OrderBy(r => r.Project, StringComparer.Ordinal)
                                .ThenBy(r => r.Group, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {row.Project} (ceiling {row.Ceiling})  {profile.Name} {row.FeatureVersion}/{row.Group}");
            Console.WriteLine($"      {row.Detail}");
        }
    }

    if (skippedProjects.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"SKIPPED ({skippedProjects.Count})");
        foreach (var skipped in skippedProjects)
        {
            Console.WriteLine($"  {skipped}");
        }
    }

    var available = ceilings.Length == 0
        ? "none"
        : string.Join(", ", ceilings.Select(k => $"{profile.Name} {k}"));

    Console.WriteLine();
    Console.WriteLine($"Period compilers available: {available}");
    Console.WriteLine(
        $"Totals: {results.Count} group placements, " +
        string.Join(", ", order
            .Select(o => (Outcome: o, Count: results.Count(r => r.Outcome == o)))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Count} {x.Outcome}")));
}

// Walk down to the nearest rung the compiler can actually be held to. C# 1.2 is a corpus row
// (foreach disposal) with no /langversion spelling of its own, so it is skipped as a floor.
static string? Below(string version, LanguageProfile profile)
{
    var index = LadderIndex(profile.Ladder, version);
    if (index <= 0)
    {
        return null;
    }

    for (var i = index - 1; i >= 0; i--)
    {
        if (profile.LangVersionArg(profile.Ladder[i]) is not null)
        {
            return profile.Ladder[i];
        }
    }

    return null;
}

// IReadOnlyList<T> has no IndexOf; the ladder is short enough that a linear scan costs nothing.
static int LadderIndex(IReadOnlyList<string> ladder, string version)
{
    for (var i = 0; i < ladder.Count; i++)
    {
        if (ladder[i] == version)
        {
            return i;
        }
    }

    return -1;
}

static string? LangVersionArg(string version) => version switch
{
    "1.0" => "ISO-1",
    "1.2" => null,
    "2.0" => "ISO-2",
    _ => version,
};

// Groups a floor probe cannot speak to, each for a reason inherent to the row rather than to the
// example's quality. Without these the probe reports NOT-VERSION-SPECIFIC and accuses code that is
// exactly what it should be. "Baseline" names a VB version folder rather than a group: a bucket
// exemption, which still gets the own-version check.
static string? ExemptionReason(string group) => group switch
{
    "LockStatement" =>
        "the lock statement shipped in C# 1.0; the corpus files it under C# 3.0 to mirror its "
        + "source document's section placement, which MANIFEST.md's Note column records. An older "
        + "compiler accepting it is the expected result, not a defect",

    "EmbeddedInteropTypes" =>
        "NoPIA is a property of the reference (/link with EmbedInteropTypes), not of the source. "
        + "This harness compiles with /reference: only, so the feature is absent from the "
        + "compilation it performs and no compiler version can reveal it",

    "Baseline" =>
        "the Baseline bucket spans VS.NET 2002 to VS2012, and the upstream sources give no "
        + "per-version attribution below VB 14. No single previous-version pin is meaningful for "
        + "it, so it gets the own-version check only",

    _ => null,
};

// Failures that say something about the toolchain rather than the language. Treating one of these
// as a rejection would manufacture an UNGATED verdict out of a broken reference set.
static bool IsEnvironmentError(string diagnostic)
{
    string[] codes =
    [
        "CS0006",  // metadata file could not be found
        "CS1703",  // an assembly with the same identity was already imported
        "CS1704",  // an assembly with the same simple name was already imported
        "CS1705",  // assembly requires a newer version of a referenced assembly
        "CS2001",  // source file could not be found
        "CS2008",  // no source files specified
    ];

    return codes.Any(code => diagnostic.Contains(code, StringComparison.Ordinal));
}

// The VB equivalent. BC30002, BC30451 and BC30209 are here deliberately: they are ordinary "missing
// type", "missing name" and "Option Strict On requires an As clause" errors, none of which is ever
// version-gated. Against a correctly resolved reference set with the project's Option settings
// carried over they cannot appear at all, so reading one as a broken environment is safer than
// reading it as evidence of version gating.
static bool IsVbEnvironmentError(string diagnostic)
{
    string[] codes =
    [
        "BC2017",  // could not find library
        "BC2001",  // file could not be found
        "BC2008",  // no input sources specified
        "BC30002", // type is not defined
        "BC30209", // Option Strict On requires all variable declarations to have an As clause
        "BC30451", // name is not declared
        "BC31091", // import of type from assembly failed
    ];

    return codes.Any(code => diagnostic.Contains(code, StringComparison.Ordinal));
}

// The pre-Roslyn compiler accepts only ISO-1, ISO-2, 3, 4, 5 -- not the "N.0" spellings Roslyn
// added. Anything above its ceiling has no answer here.
static string? LegacyLangVersionArg(string version) => version switch
{
    "1.0" => "ISO-1",
    "2.0" => "ISO-2",
    "3.0" => "3",
    "4.0" => "4",
    "5.0" => "5",
    _ => null,
};

static string? ParseFolderVersion(string folderName)
{
    if (!folderName.StartsWith("CSharp", StringComparison.Ordinal))
    {
        return null;
    }

    var suffix = folderName["CSharp".Length..];
    if (suffix.Length == 0)
    {
        return null;
    }

    var candidate = suffix.Replace('_', '.');
    if (!candidate.Contains('.'))
    {
        candidate += ".0";
    }

    return Versions.Ladder.Contains(candidate) ? candidate : null;
}

// Corpus folders are namespace segments, so they carry the version with an underscore and a Vb
// prefix: Vb15_3 -> 15.3, Vb16_0 -> 16. Baseline carries no single version and is handled apart.
static string? VbFolderVersion(string folderName)
{
    if (!folderName.StartsWith("Vb", StringComparison.Ordinal))
    {
        return null;
    }

    var candidate = folderName["Vb".Length..].Replace('_', '.');
    if (candidate.EndsWith(".0", StringComparison.Ordinal))
    {
        candidate = candidate[..^2];
    }

    return Versions.VbLadder.Contains(candidate) ? candidate : null;
}

// The version a VB row folder is probed at. The Baseline bucket is everything VB shipped through
// VS2012, so its own-version check runs at VB 11, the highest rung in it.
static string? VbRowVersion(string folderName) =>
    folderName == "Baseline" ? Versions.VbBaseline : VbFolderVersion(folderName);

// C# spells a whole version with a trailing ".0" on the ladder but not always in a project file.
static string? CSharpCeilingVersion(string value) => value.Contains('.') ? value : value + ".0";

// A VB project pins a bare ladder value, or "latest", which is the top of the ladder this script
// knows about.
static string? VbCeilingVersion(string value) =>
    value.Equals("latest", StringComparison.OrdinalIgnoreCase)
        ? Versions.VbLadder[^1]
        : Versions.VbLadder.Contains(value) ? value : null;

static string? ReadLangVersion(string projectPath, LanguageProfile profile)
{
    foreach (var line in File.ReadLines(projectPath))
    {
        var open = line.IndexOf("<LangVersion>", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            continue;
        }

        var start = open + "<LangVersion>".Length;
        var close = line.IndexOf("</LangVersion>", start, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            continue;
        }

        return profile.CeilingVersion(line[start..close].Trim());
    }

    return null;
}

// The floor cache's scope, derived from what a probe compile actually depends on rather than
// declared alongside the project. Two projects share a cached verdict only when they resolve the
// same reference set, the same conditional-compilation constants and the same compiler options --
// the inputs that decide whether a row compiles at all. References are sorted first, so a set that
// MSBuild happens to emit in a different order is still recognized as the same set.
//
// RootNamespace is deliberately excluded even though the probe passes it. It renames the
// declarations a row emits without bearing on whether they compile, and it differs at every pin by
// design, so hashing it would give each pin its own scope and empty the cache without making a
// single verdict more accurate.
static string ScopeOf(ProjectInputs inputs)
{
    var buffer = new StringBuilder();
    foreach (var reference in inputs.References.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
    {
        buffer.Append(reference).Append('\0');
    }

    buffer.Append('\n').Append(inputs.Defines).Append('\0');
    foreach (var option in inputs.Options)
    {
        buffer.Append(option).Append('\0');
    }

    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString())));
}

static string HashFiles(string[] files)
{
    using var sha = SHA256.Create();
    var buffer = new StringBuilder();
    foreach (var file in files)
    {
        buffer.Append(Path.GetFileName(file));
        buffer.Append('\0');
        buffer.Append(File.ReadAllText(file));
        buffer.Append('\0');
    }

    return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(buffer.ToString())));
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

    var output = Run(vswhere,
        "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe");

    return output
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(File.Exists);
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
            || File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

static class Versions
{
    // The last Roslyn that tops out at C# 6, used as the period compiler for that boundary.
    public const string CompilerPackage = "1.3.2";

    // The bucket every VB row below VB 14 sits in, probed at the highest version it can contain.
    public const string VbBaseline = "11";

    public static readonly List<string> Ladder = new()
    {
        "1.0", "1.2", "2.0", "3.0", "4.0", "5.0", "6.0",
        "7.0", "7.1", "7.2", "7.3",
        "8.0", "9.0", "10.0", "11.0", "12.0", "13.0", "14.0",
    };

    // vbc accepts these and rejects 17 and 17.0 with BC2014 -- there is no VB 17.0 language version.
    // Note the bare "16": VB spells that rung without a minor part.
    public static readonly List<string> VbLadder = new()
    {
        "9", "10", "11", "12", "14", "15", "15.3", "15.5", "16", "16.9", "17.13",
    };
}

// What a verdict rests on. A floor settled by a compiler that natively tops out at the rung below is
// a fact about the language; one settled by the installed SDK under a /langversion pin is a fact
// about today's toolchain and drifts as SDKs ship. Reporting a number without saying which of the
// two produced it overstates the weaker case.
static class Evidence
{
    // A compiler whose native ceiling is the rung below settled it, in either direction.
    public const string NativeCeiling = "native-ceiling";

    // A compiler that does not top out at the rung below, held there with /langversion instead --
    // always an in-box, pre-Roslyn csc, since this tier only ever fires for the C# 1.0 and 4.0
    // floors. The C# 6 / VB 14 boundary is settled at its native ceiling by Microsoft.Net.Compilers
    // 1.3.2 and never reaches this tier. A rejection settles it: version dependence proven. An
    // acceptance settles nothing, because both implementations can lack the same gate; that case
    // reports UNPROVEN.
    public const string LegacyPin = "legacy-pin";

    // Only the installed SDK's compiler under /langversion bears on this floor.
    public const string SdkPin = "sdk-pin";

    // Nothing compiled here bears on the floor.
    public const string None = "none";
}

// A language's identity within this probe: how to find its files, spell its language versions on
// the compiler command line, and walk its version ladder.
record LanguageProfile(
    string Name,
    string SourceExtension,
    string ProjectExtension,
    string CompilerFileName,
    IReadOnlyList<string> Ladder,
    IReadOnlyList<(string Version, string Directory)> InBoxCompilers,
    string PackagedCompilerCeiling,
    Func<string, string?> FolderVersion,
    Func<string, string?> LangVersionArg,
    Func<string, string?> LegacyLangVersionArg,
    Func<string, string?> CeilingVersion,
    Func<string, bool> IsEnvironmentError,
    string ControlSource);

// One feature group folder as a project compiles it. VersionFolder is kept alongside the version it
// resolves to because a bucket exemption is keyed on the folder name, not on the group.
record RowGroup(string VersionFolder, string Version, string Group, string[] Files);

// One project to probe, with the rows it compiles already resolved. The floor cache's scope is not
// here: it is derived from the project's resolved compile inputs by ScopeOf, which needs an MSBuild
// evaluation and so cannot be settled at discovery time.
record ProbeProject(
    string Name,
    string ProjectPath,
    string Ceiling,
    IReadOnlyList<RowGroup> Rows);

record Discovery(List<ProbeProject> Projects, string? Fatal);

// Everything a probe compile needs from a project besides its sources. RootNamespace is VB-only in
// effect: a VB compilation prepends it to every declaration, so omitting it compiles the row in a
// different namespace than the project that ships it. csc has no such switch -- in C# the property
// only seeds new-file templates -- so it is resolved for both languages and emitted for neither but
// VB.
record ProjectInputs(
    string[] References,
    string? Defines,
    string? RootNamespace,
    IReadOnlyList<string> Options);

record Result(
    string Project,
    string Ceiling,
    string FeatureVersion,
    string Group,
    string Outcome,
    string Detail,
    string Evidence);

record Verdict(string Outcome, string Detail, string Evidence);

record CompileResult(bool Succeeded, string FirstError);
