# verify-feature-floors.cs

Checks that every language-feature example in the corpus actually *requires* the language version it
is filed under, and reports the rows whose requirement `<LangVersion>` cannot enforce.

## The problem it exists for

The corpus is organized by language version. On the C# side
`CSharp_v7.0/CSharp7/GeneralizedAsyncReturnTypes/` holds a C# 7.0 row; on the VB side the family's
`src/Vb15_5/PrivateProtectedAccessModifier/` holds a VB 15.5 one, and each pinned project globs the
rows it claims. Either way a project pins a `<LangVersion>` ceiling, and the obvious way to police
the organization is to build a project and see whether anything above its ceiling fails.

That check is weaker than it looks. **`/langversion:N` on a current compiler is not the C# N
compiler.** Roslyn holds a construct to a language version only where its binder explicitly calls
`CheckFeatureAvailability`. Syntax-driven features — out variables, tuples, patterns, local
functions, digit separators — all got that call, so a too-low ceiling rejects them. Semantic and
attribute-driven features did not, and they compile straight through a ceiling that should have
stopped them.

Two C# rows in this corpus are known to do exactly that:

| Row | Filed under | Compiles as low as |
|---|---|---|
| `GeneralizedAsyncReturnTypes` | C# 7.0 | `/langversion:5` |
| `Variance` | C# 2.0 | `/langversion:ISO-1` |

Neither is a defective example. `async ValueTask<T>`, and even a hand-rolled `[AsyncMethodBuilder]`
type, genuinely require C# 7.0 — Roslyn simply never gated the feature. A real C# 6 compiler rejects
the row with CS1983, and a real C# 2.0 compiler rejects `Variance` with CS0410.

**The same holds for VB, and there it is the common case rather than the exception.** VB's releases
after 14 add recognition — the compiler learning to honor metadata a C# assembly emitted — far more
often than they add syntax, and a change that adds no syntax has nothing for `LangVersion` to gate.
`UnmanagedConstraintRecognition` is the clearest instance: filed at VB 17.13, its measured floor is
VB 11, the ladder's lowest rung. Only the compiler binary differs across that range.

So a survivor in a `<LangVersion>` sweep is ambiguous: the example may be misfiled, or the compiler
may have no gate for it. Distinguishing the two needs a compiler that predates the feature.

## Usage

```bash
dotnet scripts/verify-feature-floors.cs                          # every CSharp_v* project
dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0 # one C# project
dotnet scripts/verify-feature-floors.cs -- --language vb         # the VB ladder, both families
dotnet scripts/verify-feature-floors.cs -- --language vb --project dotnet/Net10/15.5/library
dotnet scripts/verify-feature-floors.cs -- --language vb --project dotNetFramework/v4.8/latest/my
dotnet scripts/verify-feature-floors.cs -- --json                # machine-readable
dotnet scripts/verify-feature-floors.cs -- --offline             # skip the NuGet download
```

`--language` takes `cs` (the default) or `vb`. Anything else exits 2.

`--project` is spelled differently per language, because the two corpora are shaped differently:

| Language | Shape | Examples |
|---|---|---|
| `cs` | the project **directory name** under `CSharp/dotNetFramework/v4.8/` | `CSharp_v7.0`, `CSharp_v8.0-Unsafe` |
| `vb` | the project directory's path relative to `VB.NET/`, forward slashes, `<family>/<pin>/<kind>` | `dotnet/Net10/15.5/library`, `dotNetFramework/v4.8/latest/my` |

Windows only, and it needs Visual Studio's `MSBuild.exe`: the corpus's net48 C# projects are non-SDK
XML and resolve `PackageReference` assets only through the NuGet targets that ship with Visual
Studio, and the modern `csc.exe`/`vbc.exe` the probe drives are the ones sitting beside that
MSBuild. The reference set for each probe comes from MSBuild rather than being hand-assembled, so a
probe sees exactly the assemblies the real build sees. For VB the resolution also carries over the
project's `Option Explicit`/`Strict`/`Infer`/`Compare` settings, its project-level `Imports`, and
`FinalDefineConstants`; without them ordinary rows fail with `BC30209` and `BC30451`, which read
exactly like version gating. It carries `RootNamespace` too, as `/rootnamespace:`, because a VB
compilation prepends that to every declaration and a probe omitting it would compile the row in a
different namespace than the project that ships it. **`csc` has no such switch** — in C# the
property only seeds new-file templates — so the value is resolved for both languages and emitted
only for VB.

Exit code is **1** when any group is `MISPLACED`, `UNDER-PLACED` or `NOT-VERSION-SPECIFIC` — the
three outcomes that mean the corpus is wrong. **2** for a setup failure that means nothing was probed: an unknown
argument, an unrecognized `--language` value, no repo root, a non-Windows host, no Visual Studio
MSBuild, no compiler beside it, or one of discovery's three fatal cases — the corpus root itself
missing, no projects found under it with no `--project` filter given (C#'s message names the
`CSharp_v*` directory pattern; VB's names the `.vbproj` extension), or a `--project` filter matching
no project. A layout discovery cannot read is the same kind of failure and takes the same exit code:
a `Compile` glob whose shape is not `<directory>/**/*.vb`, a source file at an unexpected depth
under `src/`, or a version folder naming no ladder rung each print their own message and exit 2.
Every other outcome is a finding about the toolchain's reach and exits 0.

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

Step 4 never runs for VB. The in-box `vbc` compilers predate `/langversion` as a meaningful switch —
`v3.5` answers it with `BC2007`, "option 'langversion' is unknown and ignored" — so holding one of
them to a rung would silently probe its own ceiling instead. VB therefore has step 3 or nothing.

### A row filed above its project's pin

A project holds **every** row that compiles at its pin, including rows filed above it whose feature
`LangVersion` turns out not to gate. That is the corpus's deliberate model, not a mistake: the
project's green build at the lower pin is what makes the ungatedness a checked fact instead of a
note. Such a row is therefore compiled at its project's pin *before* the probe above runs, against
that project's own reference set:

- **Accepted at the pin** → the placement is evidence that the pin does not gate the row. The row
  then goes through the normal probe and reports its usual outcome, with its detail recording that
  it sits above the pin and compiles there anyway, and `sdk-pin` evidence where the probe itself
  found none.
- **Rejected at the pin** → retry at the row's own version, because this isolated compile is not the
  project build and a row the harness simply cannot build fails at every version alike.
  - Accepted there → `MISPLACED`. The pin rejects a row that otherwise stands up, so the project
    file claims a row it cannot build.
  - Rejected there too → `INCONCLUSIVE`. The failure is the harness's, not the pin's.

**`MISPLACED` therefore means a stale project file, never "filed above the pin".** An agent that
treats every above-pin placement as a defect would delete exactly the evidence the ladder exists to
record.

### A row a project should claim and does not

`MISPLACED` is only half of the placement invariant. `MANIFEST.md` states it in both directions: a
project holds **every** row that compiles at its pin, and a row lives in its family's projects from
its measured floor upward. Delete a `<Compile Include>` for a row placed below its own version and
nothing noticed — the project still builds because it now compiles strictly less,
`VbSourceCoverageTests` still passes because it asks only that *some* project compiles each file,
and the floor probe still passed because it only ever classified rows a project already claims —
while that row's **Measured floor** cell became false.

So after a project's own rows are classified, a second pass compiles every row under its family's
`src/` that the project neither claims nor `Compile Remove`s, at the project's pin. One that
succeeds is `UNDER-PLACED`. A `Compile Remove` is excluded because it is a policy statement rather
than an omission — it is what houses `MyNamespaceHelpers` in the `my/` projects.

**VB only, and only the `library` kind.** A C# project owns its version folders on disk, so it has
no shared tree to be under-claiming from. VB's `my/` kind exists to house the one row that needs
`MyType=Windows`, a per-compilation switch that cannot be scoped to a folder; it claims that row and
nothing else by construction, so the rest of `src/` being unclaimed there is the housing decision
and not a defect.

Cost is one compile per (unclaimed row, pin) pair: **77 compiles across the whole VB sweep**,
measured at 0.136 s each because a row that fails at the pin fails fast — about 10 s on an 85 s run,
inside the sweep's own run-to-run spread. It shrinks as pins rise, because a row claimed at one pin
is claimed at every pin above it: 21 and 20 unclaimed rows at the two families' `11` pins, none at
all from `16.9` upward.

## Outcomes

| Outcome | Meaning | Fails the run |
|---|---|---|
| `GATED` | `<LangVersion>` rejects the row one rung down. The healthy case. | |
| `UNGATED` | A period compiler rejects it but the current compiler does not. Genuine feature, unenforceable ceiling. | |
| `NOT-VERSION-SPECIFIC` | A compiler predating the feature accepts the code. The example does not demonstrate its own row. | yes |
| `MISPLACED` | A row above its project's pin that compiles at its own version but not at the pin. The project file is stale. | yes |
| `UNDER-PLACED` | A row the project does not claim that nonetheless compiles at its pin. The `Compile Include` is missing, or the row's measured floor is wrong. VB `library` projects only. | yes |
| `UNPROVEN` | No compiler exists here that can settle the boundary. | |
| `BASELINE` | A row at the ladder's lowest rung — there is no lower version to test against. | |
| `INCONCLUSIVE` | The probe hit a failure it cannot attribute to a language version — for example the group will not compile standalone, a period or gate compiler cannot process the group for a non-language reason (its reference-set or own-ceiling control failed), a row above its pin failed at both the pin and its own version, the group folder holds no source files of the probed language, or the pin or the row's own version has no `/langversion` spelling to compile at. This list is representative of the script's `INCONCLUSIVE` sites, not exhaustive. | |
| `EXEMPT` | A row a floor probe structurally cannot judge. See below. | |

## Evidence

Every verdict also records **what kind of evidence it rests on**, in a separate `evidence` field that
`--json` emits alongside the outcome. A pinned modern compiler and a period compiler are not the same
claim, and reporting a floor without saying which produced it overstates the weaker case.

| Evidence | Meaning |
|---|---|
| `native-ceiling` | A compiler whose native ceiling is the rung below settled it. Does not drift as SDKs ship, and the only tier that can settle the question in *both* directions. |
| `legacy-pin` | A compiler that does not top out at the rung below, held there with `/langversion` instead — always an in-box, pre-Roslyn `csc`, since this tier only ever fires for the C# 1.0 and 4.0 floors. (The C# 6 / VB 14 boundary is settled at its native ceiling by `Microsoft.Net.Compilers` 1.3.2 and never reaches this tier.) One-directional: a rejection settles it — version dependence proven; an acceptance settles nothing and reports `UNPROVEN`. C# only. |
| `sdk-pin` | The installed SDK's compiler under `/langversion`, and nothing else. Says what today's toolchain gates — a fact that drifts. |
| `exempt` | The row is exempt from the floor probe, so no floor was measured and there is no evidence to tier. Every group exemption reports this. Distinct from `none`: the at-the-pin compile such a row may still have received speaks to its placement, not to its floor. |
| `none` | Nothing compiled here bears on the floor. A bucket exemption stays here, because its own-version check did compile. |

`MANIFEST.md`'s VB **Measured floor (evidence)** column is derived from this field, so a reader is
never left to assume the stronger claim.

## Lowest accepted `/langversion` — C# only

A second quantity, measured alongside the outcome and reported apart from it. After the probe above
has run, the ladder is walked **down** one spellable rung at a time until a rung rejects the row or
the ladder bottoms out, and the lowest rung still accepted is reported. `--json` carries it as
`LowestAcceptedLangVersion` and `LowestAcceptedLangVersionEvidence`; the console report lists the
rows that go below their own version in a section of the same name.

**It is not the outcome restated, and the two answer different questions.** The outcome asks whether
anything in the row requires the version it is filed under, and a period compiler can settle that.
This asks how far a `/langversion` pin can be lowered before *the installed compiler* complains —
`sdk-pin` evidence by construction, because a period compiler has one fixed ceiling and cannot be
walked down a ladder at all. The tier is carried anyway, to say out loud that the number drifts as
SDKs ship. The two disagree on exactly the rows this script exists to find:
`GeneralizedAsyncReturnTypes` is `UNGATED` at a native ceiling — a real C# 6 compiler rejects it —
while today's compiler accepts it as low as `/langversion:5`.

| Reported | Meaning |
|---|---|
| a rung, `sdk-pin` | The installed compiler accepts the row there and rejects it one rung lower, or the ladder ran out. |
| nothing, `exempt` | The row is exempt from the floor probe, so no descent was attempted. |
| nothing at all | No rung could be measured: the row does not compile standalone, or it is a row above its project's pin that never reached the probe. |

Cost is one compile per rung below the first, and only rows that *have* a rung below the first pay
it — a row `GATED` one rung down ends the descent on the compile the probe already made. A row whose
own version has no `/langversion` spelling still gets a descent from the rung below, which is what
gives C# 1.2's `ForeachEnhancements` a rung despite its `UNPROVEN` outcome.

**VB does not descend.** `MANIFEST.md`'s VB tables already carry a **Measured floor** column, and
that column is *placement-derived* — the lowest pin whose project compiles the row. A second,
differently-defined number printed beside it would be merged by the first reader who assumed one
column had been duplicated, and there is nothing in the VB corpus consuming it. `--json` omits both
fields entirely for VB rather than emitting nulls, so a VB run is byte-identical to one from before
this measurement existed.

## `--json`

The payload is `periodCompilers`, `skippedProjects`, and `results`. Each result carries `Project`,
`Ceiling`, `FeatureVersion`, `Group`, `Outcome`, `DiagnosticCode`, `Detail`, `Evidence`, and for C#
the two `LowestAcceptedLangVersion*` fields.

**`Detail` in `--json` is not the `Detail` the console prints.** Where the console quotes the
compiler's own message, `--json` substitutes the bare diagnostic code:

```
console:  accepted at /langversion:ISO-1 by the modern compiler but rejected by the C# 2.0
          compiler held to the same setting (error CS0410: Ingen överlagring för CreateCircle
          har den korrekta parameter- och returtypen)

--json:   "DiagnosticCode": "CS0410",
          "Detail": "accepted at /langversion:ISO-1 by the modern compiler but rejected by the
                     C# 2.0 compiler held to the same setting (CS0410)"
```

That is what makes the payload machine-independent. Four of the nine compilers keep the host's
language, so prose in `--json` would make two machines disagree byte-for-byte on identical code —
which silently voids the reproducibility [Limitations](#limitations) describes the serialized VB
binding as protecting. **Nothing in classification changes**: `IsEnvironmentError` and
`IsVbEnvironmentError` already match on codes and never on prose, and the code is what `MANIFEST.md`
and this document quote.

The substitution replaces the exact diagnostic substring the `Detail` embedded, so nothing parses
the composed sentence — a message can contain parentheses of its own (`CS0246`'s "(are you missing a
using directive…)") and any bracket-matching rule would eventually cut one in half. `Result` carries
that substring in a required `Diagnostic` parameter rather than an optional one, so a new
construction site that forgets it fails to compile instead of quietly leaking translated text.

**A diagnostic the code pattern cannot name reports `DiagnosticCode: ""` and keeps its prose.** That
covers two cases and is deliberate in both: a `Detail` that quotes no compiler at all — most of
them, including every `BASELINE` and every exemption — and compiler output carrying no recognizable
code, where dropping the text would leave the row with no description of its failure. The field is
always present; `""` means "no code", never "not measured".

The console report is unchanged and keeps whatever language the compiler emitted.

## Compilers

All period compilers are used at their **native ceiling**, never behind a `/langversion` flag. Both
languages ship in the same places, so each source below serves whichever language is being probed —
one `Microsoft.Net.Compilers` download covers the C# 6 boundary and the VB 14 one.

| C# | VB | Source |
|---|---|---|
| 2.0 | — | `%WINDIR%\Microsoft.NET\Framework64\v2.0.50727\` — that `vbc.exe` tops out at VB 8, below the ladder's first rung, so it can never be a floor and is not registered |
| 3.0 | 9 | `%WINDIR%\Microsoft.NET\Framework64\v3.5\` |
| 5.0 | 11 | `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\` |
| 6.0 | 14 | `Microsoft.Net.Compilers` 1.3.2, downloaded to `.artifacts/` on first run |

These are the in-box .NET Framework compilers, present on a normal Windows install. They read net48
reference assemblies without complaint, so **no era-specific projects or older target frameworks are
needed** to drive them — the existing net48 corpus is enough.

`--offline` skips the NuGet download; C# 6 and VB 14 boundaries then report `UNPROVEN`.

**VB 14 is the highest native VB ceiling that exists**, so every VB floor above it rests on the
modern compiler under a `/langversion` pin alone — `sdk-pin` evidence. In this corpus the escalation
therefore settles exactly one distinct VB row, `ConsumingCSharpRefReturnValues`: a floor is probed
against the rung *below* the row's own version, the corpus has no VB 10 or VB 12 rows, and VB 14 is
the only native ceiling landing one rung below a version the corpus files rows at.

## Limitations

**C# 4 and C# 1.x cannot be probed at a native ceiling.** .NET 4.5 is an in-place update that
replaced `v4.0.30319`'s C# 4 compiler with a C# 5 one, so no C# 4 binary survives on a machine
carrying .NET 4.5 or later. NuGet cannot fill the hole either — Roslyn's first release *was* C# 6.
Below that, .NET 1.0/1.1 do not install on current Windows. Rows whose floor is C# 4.0 or C# 1.x
therefore report `UNPROVEN`, and a `NOT-VERSION-SPECIFIC` verdict is impossible for them. Recovering
a C# 4 compiler would mean extracting `csc.exe` from the .NET Framework 4.0 standalone installer
payload, which is unsupported on current Windows.

**VB's gaps are VB 10, VB 12, and everything above VB 14.** The first two have no compiler for the
same reason as C# 4. Above 14 there is no native VB ceiling at all, because 14 is the highest one
that ever shipped as a standalone binary. Floors at any of those rungs report `UNPROVEN`.

**Features that live outside the source are invisible.** The probe compiles source files with
`/reference:`, always `/target:library` and never `/unsafe`. Anything expressed in the *build* rather
than in the code cannot be seen: NoPIA embedding (`/link` with `EmbedInteropTypes`),
`AllowUnsafeBlocks`, `OutputType` for top-level statements. `EmbeddedInteropTypes` is exempted for
this reason; the unsafe rows report `INCONCLUSIVE` with `CS0227` and get no rung.

The descent inherits that limit, and one of its numbers is a harness artifact because of it.
`AsyncMain` reports a lowest accepted `/langversion` of 5.0: the C# 7.1 feature is an *entry point*
signature, so under `/target:library` the gate never fires and what the descent actually measures is
the async method body. The number is real about the compilation performed and says nothing about the
row's feature — which is exactly why it is reported as a rung the compiler accepted rather than as a
floor the row requires.

**Compilation is not execution.** A row can compile identically on two compilers and still behave
differently at runtime. This tool says nothing about behavior, and does not replace verifying by
execution any comment that claims runtime semantics.

**Groups are judged whole.** The unit is the group folder, so a folder mixing a genuine version-N
construct with unrelated older code reports `GATED` on the strength of the one construct. The
verdict is "something in here requires version N", not "everything in here does".

**Standalone compilation is not the project build.** A group that depends on a type from another
group, or on a compilation-wide switch, will not compile alone and lands in `INCONCLUSIVE` rather
than being silently skipped.

**Verdicts are cached by content**, and the lowest accepted `/langversion` is cached with them. The
same row is held by several projects, and its floor is a property of the files rather than of the
project holding them. Each distinct
`(scope, version, file content)` triple is probed once and the verdict reused, so per-project rows in
the report can be identical by construction. `scope` keeps rows that share a reference set together,
because a net10 reference set and a net48 one can disagree about whether a row compiles at all, and
the `my/` projects compile with `_MyType` defined. It is **derived, not declared**: a SHA-256 of the
project's sorted resolved reference paths, its conditional-compilation constants, and its compiler
options — the inputs that decide whether a row compiles. `RootNamespace` is excluded on purpose even
though the probe passes it, because it renames a row's declarations without bearing on whether they
compile and differs at every pin by design; hashing it would give each pin its own scope and empty
the cache. The above-pin check is deliberately *outside* the cache — whether a row compiles at a
given pin is a property of the placement, not of the row.

The price is that a project's reference resolution can no longer be deferred past its first cache
read: the key names the reference set the verdict was measured against, so it has to be resolved
before the lookup. Resolution still happens at most once per project, and not at all for a project
with no row to classify, but a project whose every row is already cached now pays one MSBuild
evaluation it used to skip. Discovering VB's projects in ascending pin order still pays — it saves
the compiles, which dominate — it just no longer saves the resolution too.

**Scope is the C# net48 tree and the whole VB corpus.** With `--language cs` only
`examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v*` is walked; the C# net10 projects
are untouched. With `--language vb` both families under `examples/language-features/VB.NET/` are
walked, every pin and every kind. The two are discovered differently, because the corpora are shaped
differently: a C# project directory holds its own version folders one group deep, whereas a VB family
has one shared `src/` tree and each pinned project selects rows from it with `Compile Include` globs
minus its `Compile Remove` globs. Reading those items is the only way to learn which rows a VB
project actually compiles — honoring `Remove` is what keeps `MyNamespaceHelpers` attributed to the
`my/` projects alone. A `Compile` glob that does not end in `**/*.vb` fails the run with exit 2
rather than being silently skipped.

**VB probes serialize the compiler's binding, C# probes do not.** Roslyn's `vbc` binds method bodies
concurrently, so which of several genuine errors a rejection reported first varied per process and
the `Detail` string in `--json` was irreproducible — `BC30643`, `BC30657` and `BC36954` were all
observed for the same row, all real. The VB branch of the response file therefore carries
`/parallel-`, which makes the reported diagnostic stable without changing any `Outcome` or
`Evidence`. Measured cost: about 5% on a whole VB run.

Two gates on that, both load-bearing. **Roslyn only** — `/parallel` predates nothing older, so the
in-box `csc` rejects it with `CS2007` and exits 1, and the in-box `vbc` answers with command line
warning `BC2007`, which would then be the first diagnostic line the report picks up on a toolchain
whose severity words the probe cannot match by spelling. **VB only** — the rotation has only ever
been observed in VB, C#'s `--json` is reproducible as it stands, and serializing every C# probe
would cost runtime for no measured gain. Two `--json` runs of unchanged code are now byte-identical
in both languages, so diffing them *is* a valid regression technique.

**Four of the nine compilers cannot be forced to English, which is why `--json` carries codes rather
than prose.** `/preferreduilang:en-US` reaches the two modern compilers and the two in
`Microsoft.Net.Compilers` 1.3.2, plus the in-box `csc` at `v4.0.30319` — five of the nine. The
in-box `csc` at `v2.0.50727` and `v3.5` reject the switch outright, the in-box `vbc` at `v3.5` and
`v4.0.30319` ignore it, and no environment variable reaches a compiler binary at all. The console report
therefore prints whatever language those five emit — on a sv-SE host, the `CS0410` behind the
`Variance` row's C# 2.0 legacy pin is Swedish, and stays Swedish. See
[`docs/gotchas.md`](../docs/gotchas.md) for the per-compiler table.

## Exemptions

Some rows cannot be judged by a floor probe for reasons inherent to the row. They are listed in
`ExemptionReason` in the script, each with its reason, and report `EXEMPT` instead of an accusation.

| Row | Why | Shape |
|---|---|---|
| `LockStatement` | `lock` shipped in C# 1.0. The corpus files it under C# 3.0 to mirror its source document's section placement, which `MANIFEST.md`'s Note column records. An older compiler accepting it is the expected result. | group |
| `EmbeddedInteropTypes` | NoPIA is a property of the reference, not the source. The feature is absent from the compilation the probe performs, so no compiler version can reveal it. | group |
| `CallerArgumentExpressionConsumption`, `OverloadResolutionPriorityConsumption`, `UnmanagedConstraintRecognition` (VB 17.13) | One category. Each demonstrates the compiler honoring metadata a C# assembly emitted, and VB can express none of the three in source, so the feature is absent from the compilation the probe performs and no compiler version can reject it. Verified: all three compile on the 2016 native VB 14 compiler. A gated construct elsewhere in such a row — `CallerArgumentExpressionConsumption` uses null-conditional access, so VB 11 rejects it with `BC36716` — fixes a floor for that row's own sources and still says nothing about the 17.13 feature. | group |
| `Baseline` (VB) | The bucket spans VS.NET 2002 to VS2012 and the upstream sources give no per-version attribution below VB 14, so no single previous-version pin is meaningful for it. | bucket |

The two shapes differ. A **group** exemption says the probe cannot see the feature at all, so nothing
is compiled and the evidence is `exempt`. A **bucket** exemption says the bucket has no single previous
version to test against; the sources still have to stand on their own, so step 1 still runs — VB's
`Baseline` rows are compiled at VB 11, the highest rung the bucket can contain, and a failure there
reports `INCONCLUSIVE` rather than `EXEMPT`.

Add an entry only when a row is genuinely unjudgeable, never to silence a verdict that is merely
inconvenient. An exemption with a weak reason turns a real defect into a clean run.

## Implementation notes

These details are load-bearing and easy to reintroduce as bugs:

- **`/noconfig` is honored only on the command line.** Placed inside a response file it is silently
  ignored, `csc.rsp` or `vbc.rsp` is read anyway, and its auto-references collide with the resolved
  reference set as CS1703 on the older compilers.
- **An old compiler's failure is not automatically a language rejection.** Before a rejection at the
  floor is trusted, the same compiler must succeed on a control — the reference set alone for a
  period compiler, the group at the compiler's own ceiling for the fallback gate. Known
  environment-level diagnostics are demoted to `INCONCLUSIVE`. Without this, a broken reference set
  manufactures confident `UNGATED` verdicts. `BC30002`, `BC30451` and `BC30209` are on that list
  deliberately: they are ordinary "missing type", "missing name" and "Option Strict On requires an
  As clause" errors, none of which is ever version-gated, so reading one as a broken environment is
  safer than reading it as evidence of gating.
- **The control source has to be written in the language being probed.** Handing a `.cs` file to
  `vbc` fails the control run every time and turns every VB escalation into a false `INCONCLUSIVE`.
- **Diagnostic selection is locale-independent.** The probe prefers `": error "` so an English
  toolchain reproduces an exact severity match, and falls back to matching any `CS`/`BC` code only
  when no line matches at all — which is what a localized in-box compiler produces. `CS` codes are
  four digits; `BC` codes run four or five, so the pattern must allow both.
- **`/preferreduilang` is gated per compiler binary, never on `IsRoslyn` and never on the language.**
  Compilers that honor it get `/preferreduilang:en-US` in their response file; the switch is honored
  from inside one, unlike `/noconfig`. The in-box `csc` gains it at `v4.0.30319` and answers
  `fatal error CS2007` with exit 1 below that; no in-box `vbc` implements it at any version —
  including `v4.0.30319`'s, sitting beside the `csc` that does — and each answers command line
  warning `BC2007` instead. Both exclusions manufacture verdicts: the first fails every compile, the
  second puts a warning ahead of the real error for the first-diagnostic scan to find.
  `HonorsPreferredUiLanguage` on `ProbeCompiler` carries the measurement per binary, and
  `InBoxCompilers` declares it per entry. The Roslyn compilers are sent it even though they already
  print English on a machine with no localized satellites installed for them — that is a fact about
  the machine, not about the compiler.
- **VB's compiler switches differ in spelling.** `vbc` rejects `/nostdlib+` with `BC2007`; the switch
  carries no trailing sign in VB. And under `/nostdlib` the compiler does not supply the VB runtime
  itself, so `Microsoft.VisualBasic.dll` has to be named explicitly with `/vbruntime:` or every probe
  fails with `BC2017`.
- **References are deduplicated by simple name, not by path.** MSBuild can resolve one assembly
  identity from two places — a reference assembly and a package — which the modern compiler tolerates
  and the older ones reject outright with CS1703.
