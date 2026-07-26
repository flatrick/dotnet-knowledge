# verify-feature-floors.cs

Checks that every language-feature example in the corpus actually *requires* the language version it
is filed under, and reports the rows whose requirement `<LangVersion>` cannot enforce.

## The problem it exists for

The corpus is organized by language version: `CSharp_v7.0/CSharp7/GeneralizedAsyncReturnTypes/`
holds a C# 7.0 row, and each project pins a `<LangVersion>` ceiling. The obvious way to police that
organization is to build a project and see whether anything above its ceiling fails.

That check is weaker than it looks. **`/langversion:N` on a current compiler is not the C# N
compiler.** Roslyn holds a construct to a language version only where its binder explicitly calls
`CheckFeatureAvailability`. Syntax-driven features — out variables, tuples, patterns, local
functions, digit separators — all got that call, so a too-low ceiling rejects them. Semantic and
attribute-driven features did not, and they compile straight through a ceiling that should have
stopped them.

Two rows in this corpus are known to do exactly that:

| Row | Filed under | Compiles as low as |
|---|---|---|
| `GeneralizedAsyncReturnTypes` | C# 7.0 | `/langversion:5` |
| `Variance` | C# 2.0 | `/langversion:ISO-1` |

Neither is a defective example. `async ValueTask<T>`, and even a hand-rolled `[AsyncMethodBuilder]`
type, genuinely require C# 7.0 — Roslyn simply never gated the feature. A real C# 6 compiler rejects
the row with CS1983, and a real C# 2.0 compiler rejects `Variance` with CS0410.

So a survivor in a `<LangVersion>` sweep is ambiguous: the example may be misfiled, or the compiler
may have no gate for it. Distinguishing the two needs a compiler that predates the feature.

## Usage

```bash
dotnet scripts/verify-feature-floors.cs                          # every CSharp_v* project
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one project
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
dotnet scripts/verify-feature-floors.cs -- --offline             # skip the NuGet download
```

Windows only, and it needs Visual Studio's `MSBuild.exe`: the corpus's net48 projects are non-SDK XML
and resolve `PackageReference` assets only through the NuGet targets that ship with Visual Studio.
The reference set for each probe comes from MSBuild rather than being hand-assembled, so a probe sees
exactly the assemblies the real build sees.

Exit code is **1** when any group is `MISPLACED` or `NOT-VERSION-SPECIFIC` — the two outcomes that
mean the corpus is wrong. Every other outcome is a finding about the toolchain's reach and exits 0.

## How a group is probed

Each *group folder* — one feature row, e.g. `CSharp7/GeneralizedAsyncReturnTypes/` — is compiled
standalone, not as part of its project.

1. **At its own version.** Must succeed. If it does not, the group needs project context the probe
   cannot reproduce, and it is `INCONCLUSIVE` rather than judged.
2. **One rung down the ladder**, on the current compiler.
   Rejected → `GATED`. `<LangVersion>` enforces this row and nothing more is needed.
   Accepted → suspicious; escalate.
3. **On a compiler whose native ceiling is that lower rung.**
   Rejected → `UNGATED`. The example is genuine; the current compiler simply has no gate for it.
   Accepted → `NOT-VERSION-SPECIFIC`. A compiler predating the feature compiles the code, so the
   code cannot require the feature. This is a defect in the example.
4. **No such compiler?** Fall back to the oldest available compiler *held to* the lower rung with
   `/langversion`. A rejection still proves version-dependence; an acceptance proves nothing and
   leaves the row `UNPROVEN`.

The asymmetry in step 4 is the important part. A `/langversion` flag can only ever prove that
something *is* version-dependent. It can never prove the converse, because a missing gate looks
exactly like a feature that was always legal. Only a compiler at a **native ceiling** can support
`NOT-VERSION-SPECIFIC`, which is why only step 3 is allowed to accuse an example.

A group filed in a project whose ceiling is below the group's own version is `MISPLACED` and
reported without probing.

## Outcomes

| Outcome | Meaning | Fails the run |
|---|---|---|
| `GATED` | `<LangVersion>` rejects the row one rung down. The healthy case. | |
| `UNGATED` | A period compiler rejects it but the current compiler does not. Genuine feature, unenforceable ceiling. | |
| `NOT-VERSION-SPECIFIC` | A compiler predating the feature accepts the code. The example does not demonstrate its own row. | yes |
| `MISPLACED` | Group filed above its project's `<LangVersion>`. | yes |
| `UNPROVEN` | No compiler exists here that can settle the boundary. | |
| `BASELINE` | A C# 1.0 row — there is no lower version to test against. | |
| `INCONCLUSIVE` | The group will not compile standalone, or an old compiler could not read the reference set. | |
| `EXEMPT` | A row a floor probe structurally cannot judge. See below. | |

## Compilers

All period compilers are used at their **native ceiling**, never behind a `/langversion` flag.

| C# | Source |
|---|---|
| 2.0 | `%WINDIR%\Microsoft.NET\Framework64\v2.0.50727\csc.exe` |
| 3.0 | `%WINDIR%\Microsoft.NET\Framework64\v3.5\csc.exe` |
| 5.0 | `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| 6.0 | `Microsoft.Net.Compilers` 1.3.2, downloaded to `.artifacts/` on first run |

These are the in-box .NET Framework compilers, present on a normal Windows install. They read net48
reference assemblies without complaint, so **no era-specific projects or older target frameworks are
needed** to drive them — the existing net48 corpus is enough.

`--offline` skips the NuGet download; C# 6 boundaries then report `UNPROVEN`.

## Limitations

**C# 4 and C# 1.x cannot be probed at a native ceiling.** .NET 4.5 is an in-place update that
replaced `v4.0.30319`'s C# 4 compiler with a C# 5 one, so no C# 4 binary survives on a machine
carrying .NET 4.5 or later. NuGet cannot fill the hole either — Roslyn's first release *was* C# 6.
Below that, .NET 1.0/1.1 do not install on current Windows. Rows whose floor is C# 4.0 or C# 1.x
therefore report `UNPROVEN`, and a `NOT-VERSION-SPECIFIC` verdict is impossible for them. Recovering
a C# 4 compiler would mean extracting `csc.exe` from the .NET Framework 4.0 standalone installer
payload, which is unsupported on current Windows.

**Features that live outside the source are invisible.** The probe compiles source files with
`/reference:`. Anything expressed in the *build* rather than in the code cannot be seen: NoPIA
embedding (`/link` with `EmbedInteropTypes`), `AllowUnsafeBlocks`, `OutputType` for top-level
statements. `EmbeddedInteropTypes` is exempted for this reason.

**Compilation is not execution.** A row can compile identically on two compilers and still behave
differently at runtime. This tool says nothing about behavior, and does not replace verifying by
execution any comment that claims runtime semantics.

**Groups are judged whole.** The unit is the group folder, so a folder mixing a genuine C# N
construct with unrelated older code reports `GATED` on the strength of the one construct. The
verdict is "something in here requires C# N", not "everything in here does".

**Standalone compilation is not the project build.** A group that depends on a type from another
group, or on a compilation-wide switch, will not compile alone and lands in `INCONCLUSIVE` rather
than being silently skipped.

**Verdicts are cached by content.** The same group folder is duplicated across the cumulative
projects, and its floor is a property of the files rather than of the project holding them. Each
distinct `(version, file content)` pair is probed once and the verdict reused, so per-project rows
in the report can be identical by construction.

**Scope is the C# net48 tree.** Only `examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v*`
is walked. The net10 projects, the `*Unsafe` projects, and the whole VB.NET corpus are untouched.

## Exemptions

Some rows cannot be judged by a floor probe for reasons inherent to the row. They are listed in
`ExemptionReason` in the script, each with its reason, and report `EXEMPT` instead of an accusation.

| Row | Why |
|---|---|
| `LockStatement` | `lock` shipped in C# 1.0. The corpus files it under C# 3.0 to mirror its source document's section placement, which `MANIFEST.md`'s Note column records. An older compiler accepting it is the expected result. |
| `EmbeddedInteropTypes` | NoPIA is a property of the reference, not the source. The feature is absent from the compilation the probe performs, so no compiler version can reveal it. |

Add an entry only when a row is genuinely unjudgeable, never to silence a verdict that is merely
inconvenient. An exemption with a weak reason turns a real defect into a clean run.

## Implementation notes

Two details are load-bearing and easy to reintroduce as bugs:

- **`/noconfig` is honored only on the command line.** Placed inside a response file it is silently
  ignored, `csc.rsp` is read anyway, and its auto-references collide with the resolved reference set
  as CS1703 on the older compilers.
- **An old compiler's failure is not automatically a language rejection.** Before a rejection at the
  floor is trusted, the same compiler must succeed on a control — the reference set alone for a
  period compiler, the group at the compiler's own ceiling for the fallback gate. Known
  environment-level diagnostics are demoted to `INCONCLUSIVE`. Without this, a broken reference set
  manufactures confident `UNGATED` verdicts.
