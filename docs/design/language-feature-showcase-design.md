# Design — .NET language-feature showcase corpus

## The gap this fills

`testData/` fixtures are purpose-built per tool scenario (see
[`testdata-fixtures`](/.claude/skills/testdata-fixtures/SKILL.md)) — each one exists because some
specific MCP tool needed a specific code shape. Nothing in the repo is a systematic, checklist-backed
corpus of *every shipped C#/VB.NET language feature*. Without one, "does this tool handle construct
X" is answered ad hoc, per bug report, rather than against a known-complete example set.

This design specifies that corpus: a standalone set of minimal, compilable examples — one per shipped
language feature, per language, per era — plus a manifest that makes "100% coverage" a falsifiable,
checkable claim rather than an assertion.

## Scope

**In scope:**
- Every C# language feature shipped from C# 1.0 through the latest version shipped with .NET 10.
- Every VB.NET language feature shipped from VB.NET 1.0 through the latest version shipped with
  Visual Studio 2022/2026 and .NET 10.
- A coverage manifest proving the above two claims are true, or documenting exactly what's excluded
  and why.

**Explicitly out of scope for this change:**
- Feature *interactions* / combinations (e.g. nullable reference types combined with pattern
  matching combined with generics). Deferred to a later pass, once single-feature coverage exists.
- Wiring this corpus into `testData/`, `DotNetMcpServer.slnx`, or any dotnet-mcp tool/E2E test. This
  corpus is deliberately independent of what the MCP server currently supports — that's a separate,
  future integration step.
- Preview or unshipped language features (proposals not yet released in a stable compiler).
- A legacy-XML (non-SDK) VB.NET/net48 project. C# gets one because C# has a real feature (nullable
  reference types) that's gated behind the SDK-style project system on net48; VB.NET has no
  equivalent SDK-gated feature at that boundary (see the project matrix below), so a second VB/net48
  project variant was considered and explicitly declined here, not overlooked.

## Project matrix

The current tree lives under `examples/language-features/` and materializes each build coordinate
explicitly:

- `CSharp/dotnet/` contains the cumulative SDK/TFM projects, which the executable build matrix
  discovers; `10/latest/` also holds the canonical `library`, `unsafe`, and `exe` sources.
- `CSharp/dotNetFramework/v4.8/CSharp_v*/` contains the hand-authored net48 language-version
  projects, including dedicated unsafe projects where required.
- `VB.NET/dotnet/Net10/` and `VB.NET/dotNetFramework/v4.8/` are the two VB **families**. Each is one
  shared `src/` tree plus a project per pinned `<LangVersion>` under `<pin>/<kind>/`, on the ladder
  `11, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`. `vbc` rejects `17` and `17.0` with `BC2014`, so
  those rungs do not exist. `library` is the mainline kind; the net48 family also has a `my` kind at
  the `11` and `latest` rungs, carrying the one row that needs `MyType=Windows`.

Each cumulative project includes every applicable feature up to its version ceiling unless the
TFM, project format, or a deliberately disabled compiler switch excludes it.

### Why VB shares one source tree and C# does not

A VB compilation prepends `RootNamespace` to every declaration, so a VB sample declares a
version-relative namespace (`Namespace Vb15.Tuples`) and takes its project coordinate at compile
time. Every pinned project in a family can therefore glob the same file, and cross-pin drift is
structurally impossible rather than test-enforced. C# has no equivalent — each C# copy must differ on
its namespace line — which is why the C# tree duplicates its sources physically.

The shared tree introduces the opposite hazard: a row added under `src/` that no project globs is in
the corpus and compiled by nothing. `VbSourceCoverageTests` covers that. `src/` is per-family rather
than shared across both families because four rows genuinely diverge — `MyNamespaceHelpers` (net48
only), `ConsumingCSharpRefReturnValues` (both, different subject), and
`CallerArgumentExpressionConsumption` and `OverloadResolutionPriorityConsumption` (net10 only).

### Why the `My` namespace gets its own projects

`MyType=Windows` exists solely for the `MyNamespaceHelpers` row, which demonstrates the `My`
namespace the compiler synthesizes under that setting. It is a per-compilation switch that cannot be
scoped to a folder — structurally the same constraint as `AllowUnsafeBlocks` and `OutputType` — so
it gets the same treatment: the mainline `library` projects set no `MyType` at all and
`Compile Remove` the row, and the `my` projects set it and glob only that row.

`Microsoft.NETFramework.ReferenceAssemblies` is carried by the net48 VB family's
`Directory.Build.props` and by `CSharp_v8.0`, which that family references. Without it no `net48`
SDK-style project in this repository builds on a machine with no .NET Framework targeting pack; it
is the known prerequisite for building that half of the corpus off Windows. `MyType` is not
obviously a second obstacle, since the `My` accessors resolve against `Microsoft.VisualBasic.dll`,
which the same package supplies.

A pinned project is not an era emulation. The family props are unconditional, so the pin-11 net48
project carries package and project references its `Baseline` rows never use. The pin constrains the
language, not the framework surface — which is already true of the C# tree.

### Why an executable gets its own project

`OutputType` is a per-project setting that cannot be scoped to a folder — structurally the same
constraint as `/unsafe`, and resolved the same way. Top-level statements require the compilation to
be an executable; a library is rejected with `CS8805` ("Program using top-level statements must be
an executable"). Hosting that row in a mainline project would give every other example in it an
entry point, and would mean the corpus contained no project demonstrating C# as an ordinary library.

The `10/latest/exe` project carries **only** rows that need an entry point. It is also inherently capped at one such
row, because a compilation may contain at most one file with top-level statements — so unlike the
`*Unsafe` projects it will not accumulate.

It is the one project in the corpus whose output can be executed, which makes its row the only one
verifiable beyond compilation: running it confirms the synthesized entry point receives `args` and
propagates the returned exit code.

### Why unsafe code gets its own projects

`AllowUnsafeBlocks` maps to the compiler's `/unsafe` switch, which is **per-compilation**: it cannot
be scoped to a folder or a file. Enabling it on a mainline project would mean every example in that
project compiles under a switch most real-world projects do not set, and the corpus would contain no
project demonstrating C# under default compilation.

The dedicated `unsafe` projects carry **only** the features that cannot compile without `/unsafe`;
every other feature stays in the mainline projects. One project per relevant TFM is enough, because
the unsafe surface does not vary interestingly between net48 and net10.

The unsafe-requiring set is, at the time of writing, six rows: *Unsafe code and pointers* (C# 1.0),
*Indexing movable fixed buffers* and *Custom `fixed` statement* (C# 7.3), *`[SkipLocalsInit]`* and
*Function pointers* (C# 9.0), and *`ref`/`unsafe` in iterators/async* (C# 13.0). `[SkipLocalsInit]`
belongs to that set for a non-obvious reason — it is an attribute, not a pointer construct, but the
compiler rejects it without `/unsafe` (`CS0227`, confirmed by probe). Each authoring plan confirms
its own version's membership by probe rather than inheriting this list on faith.

### VB and ref structs — a suppression, not a capability

VB has no `ref struct`, so `Span(Of T)` and its relatives are normally unusable: the compiler reports
`BC30668`, "types with embedded references are not supported in this version of your compiler".

That diagnostic comes from an `Obsolete` attribute the BCL places on those types precisely so a
non-supporting compiler rejects them. Because a member that is itself marked `Obsolete` suppresses
obsolete diagnostics inside it, decorating a VB method with `<Obsolete("...", False)>` makes the
error go away and the type usable.

**This adds no capability. It removes the compiler's only way of reporting that it has none.** What
works, each verified by execution: indexing a `Span`, writing through it as a genuine view, and
consuming a ref-returning API over one. What does not: VB performs no ref-safety analysis, so boxing
a `Span` or storing one in a field compiles and then fails at JIT time with
`InvalidProgramException` — invalid IL, not a catchable domain error. C# rejects both at compile
time.

Use it only to CONSUME a ref struct in a narrow local scope, always with the hazard stated in the
sample, and never as a way to claim VB supports ref structs. The corpus applies it in exactly one
place, the *Consuming C# reference return values* row, alongside a `CollectionsMarshal`-based form
that needs no suppression at all.

### The COM type-library support project

`CSharpComTypeLib` is a support assembly, not a corpus project: it holds no feature examples and
never appears in a manifest row's target column. It exists because *Embedded interop types (NoPIA)*
is not a source-level construct — it is what the compiler does to a **reference**. Demonstrating it
requires an assembly marked `[assembly: ImportedFromTypeLib]` holding `[ComImport]` types, which a
consumer then references with `EmbedInteropTypes="true"`; the compiler copies those types' shape into
the consumer and drops the assembly reference, so the referenced DLL is absent from the output.

It multi-targets `net48;net10.0` because `ImportedFromTypeLibAttribute` does not exist in
`netstandard2.0`, and both halves of the corpus must be able to embed from it. Unlike
`AllowUnsafeBlocks`, `EmbedInteropTypes` is a per-*reference* switch, so it does not change how the
rest of the consuming project compiles — which is why this feature needs no separate project the way
unsafe code does.

The net48 C# 8.0 project exists specifically because C# 8.0 shipped nullable reference types — a purely
compile-time feature with no runtime dependency, so it behaves identically on net48 and net10. That
makes it valuable to prove out on the older TFM (this repo already treats nullable-across-TFM
behavior as a real area of interest — see e.g. the `CSharpNullabilityMultiTfm*` fixtures in
`testData/`). Default interface members are excluded from that project and appear in the net10
corpus: they require `RuntimeFeature.DefaultImplementationsOfInterfaces`, which net48 doesn't
advertise — a hard compiler error, CS8701/CS8703, not a runtime risk.

> **Correction (verified empirically during plan authoring, Plan 0):** the original text here also
> excluded async streams and Span-dependent C#7.2/C#8 features (stackalloc initializers, Span/ref-like
> types) from the net48 C# 8.0 project, assuming they needed BCL surface net48 lacks. Actual `dotnet build` probes
> against net48 SDK-style/LangVersion 8 showed both compile and build cleanly given the official
> Microsoft packages `Microsoft.Bcl.AsyncInterfaces` (async streams) and `System.Memory` (Span-based
> features) — these are first-party BCL-extension packages, not community polyfills, so they're
> within the design's "no extra polyfill" intent. Only default interface members (hard compiler
> block) and ranges/indexes genuinely fail: `System.Index`/`System.Range` have no official net48
> backport package (`System.Memory` doesn't provide them — confirmed via probe: CS0518/CS0656).
> That project therefore references `Microsoft.Bcl.AsyncInterfaces` and `System.Memory`, and only
> default interface members and ranges/indexes are deferred to the net10 corpus.

VB.NET uses one net48 corpus because it has never added a feature analogous to
nullable reference types that's gated behind the SDK-style project system — per the VB team's own
"consumption-only" evolution strategy (confirmed via Microsoft Learn during design), VB.NET rarely
adds new *syntax* at all; most of its recent version deltas are about *consuming* APIs built on newer
runtime features, not new language constructs. Whether VB.NET's language surface differs at all
between net48 and net10 is therefore an open question resolved empirically during research (see the
applicability rule), not assumed — but the asymmetry in project count vs. C# is intentional, not an
oversight.

## Source-of-truth strategy

**C#:** `external/csharplang/Language-Version-History.md` is complete and version-complete back to
C# 1.0. It is the sole primary source, cross-referenced with individual proposal docs under
`external/csharplang/proposals/` when a one-line summary isn't enough to write a correct example.

**VB.NET — verified gap, verified fallback.** `external/vblang/Language-Version-History.md` is
*not* a complete version history: it only documents deltas starting at VB 15.0 (Visual Studio 2017),
plus a few later point-release entries, with nothing for VB 1.0 through VB 11 (generics, LINQ, XML
literals, auto-properties, statement lambdas, async/await, string interpolation, `NameOf`, etc. are
all real shipped features with no entry here). `external/vblang/spec/` is topic-organized, not
version-gated, so it can't fill the gap either.

This was verified during design, not assumed: `https://learn.microsoft.com/dotnet/visual-basic/whats-new/`
is a real, current Microsoft Learn page (confirmed by fetching it) that partially closes the gap —

- **VB 14 (2015) onward** (14, 15, 15.3, 15.5, 16.0, 16.9, 17.0, 17.13): itemized, per-feature, with
  links — same quality as the C# source.
- **Pre-VB14** (Visual Studio .NET 2002 through Visual Studio 2013): only coarse one-line-per-version
  bucket summaries (e.g. Visual Studio 2008: "LINQ, XML literals, local type inference, object
  initializers, anonymous types, extension methods, lambda expressions, `if` operator, partial
  methods, nullable value types" — no per-feature breakdown, no links).

The sourcing strategy follows that split exactly:

- **VB.NET baseline (pre-VB14):** one non-versioned bucket covering everything VB.NET had
  accumulated through VB 11/2013 — sourced from the Learn page's bucket summaries, with
  implementation detail filled in from the local `external/vblang/spec/` topic list. Not attributed
  to individual point versions, because the source material doesn't support that attribution.
- **VB 14/15 onward:** normal per-version deltas, same discipline as C#, sourced from the Learn page
  and cross-checked against `Language-Version-History.md`'s 15.0+ entries where they overlap.

## Folder & file structure

Convention: `<project-root>/<version-or-baseline>/<feature-group>/` for C#, and
`<family-root>/src/<version-or-baseline>/<feature-group>/` for VB. A version folder is created only
if that version actually added a language feature/behavior — a point release with no language-level
change gets no folder. For VB.NET, the pre-14 range collapses to a single `Baseline/` folder (no
per-version attribution, per the sourcing gap above); normal per-version folders resume at `Vb14/`.

VB's version folders keep the `Vb15_3/` spelling because they are namespace segments; VB's *project*
directories use the `15.3` spelling `vbc` accepts on the command line. The two are different
namespaces of names and are deliberately not unified.

Example (canonical C# net10 sources):

```
examples/language-features/CSharp/dotnet/10/latest/library/
  CSharp1/ClassesStructsEnums/, Delegates/, Preprocessor/, ...
  CSharp2/Generics/, Iterators/, PartialTypes/, ...
  ...
  CSharp8/NullableReferenceTypes/, DefaultInterfaceMembers/, AsyncStreams/, PatternMatching/, ...
  ...
  CSharp14/ExtensionMembers/, FieldKeyword/, ...
```

Example (canonical VB net10 sources):

```
examples/language-features/VB.NET/dotnet/Net10/
  Directory.Build.props
  src/Baseline/TypesAndDeclarations/, Linq/, XmlLiterals/, LambdaExpressions/, AsyncAwaitAndIterators/, ...
  src/Vb14/NameOfOperator/, StringInterpolation/, ...
  src/Vb15/Tuples/, BinaryLiteralsAndDigitSeparators/, ConsumingCSharpRefReturnValues/
  src/Vb15_3/NamedTupleInference/
  src/Vb15_5/NonTrailingNamedArguments/, PrivateProtectedAccessModifier/, LeadingDigitSeparator/
  src/Vb16_0/CommentsInMorePlaces/, OptimizedFloatToIntConversion/
  src/Vb16_9/ConsumingInitOnlyProperties/
  src/Vb17_13/UnmanagedConstraintRecognition/, ...
  11/library/ 14/library/ 15/library/ 15.3/library/ 15.5/library/
  16/library/ 16.9/library/ 17.13/library/ latest/library/
```

Each `library.vbproj` names the rows it holds with explicit `Compile` globs — whole-folder globs
where a rung takes an entire version folder, per-row globs where it takes part of one:

```xml
<!-- Net10/14/library/library.vbproj -->
<Compile Include="../../src/Baseline/**/*.vb" LinkBase="Baseline" />
<Compile Include="../../src/Vb14/**/*.vb" LinkBase="Vb14" />
<!-- Rows above this pin that LangVersion does not gate; the green build is the evidence. -->
<Compile Include="../../src/Vb15/ConsumingCSharpRefReturnValues/**/*.vb" LinkBase="Vb15/ConsumingCSharpRefReturnValues" />
```

Rungs that admit nothing their predecessor did not are kept deliberately. A green build at 16 whose
row set equals 15.5's documents that VB 16 gates nothing, and a future gated row at that version has
a home already.

### The per-version projects are hand-authored

The current per-`<LangVersion>` trees are hand-authored probes and the tracked tree is current
truth. They replace the older derived-project layout described by the legacy
`scripts/generate-net48-examples.cs`; that script targets deleted project roots and must not be run
against the current corpus.

Some authored C# samples intentionally appear in several cumulative SDK/TFM project pins. Scope an
edit to the projects named by the task. When those copies are required to remain identical,
propagate the canonical edit explicitly and verify byte equality. Project-specific forms remain
valid where the target framework genuinely changes the construct that can be demonstrated.

VB has no such duplication to keep in step: each family holds one copy under `src/`, and its pinned
projects glob it. Edit the file under `src/` and every pin that compiles the row picks the edit up.

## Coverage manifest

One `MANIFEST.md` at `examples/language-features/` root — the falsifiable artifact that makes "100%
coverage" checkable rather than asserted. One table per language, columns:

| Version | Feature | Group folder | Included in project(s) | Excluded from project(s) (reason) | Source doc |
|---|---|---|---|---|---|

That sketch is the minimum. The three tables as authored extend it differently:

| Table | Columns beyond the sketch |
|---|---|
| C# | `Note` |
| VB baseline bucket | `Measured floor (evidence)` |
| VB itemized per-version | `Measured floor (evidence)`, `Note` |

`Note` carries whatever a row's placement or subject needs explaining. **Measured floor (evidence)**
is the lowest pin at which the row compiles, together with the `verify-feature-floors.cs` evidence
tier that floor rests on (`native-ceiling`, `sdk-pin`, `none`). Keeping the two strengths of evidence
distinguishable is that column's point — collapsing them into a bare number would present a fact
about the installed SDK as a fact about the language. The C# table has no floor column: its
`Target project(s)` column names ceiling projects and has not been extended to the
per-`<LangVersion>` probe projects.

Every row is sourced from `Language-Version-History.md` (C#) or the Learn page + local spec (VB.NET).
Every feature that ships must end up with a folder in at least one project, or an explicit
excluded-with-reason entry — nothing is silently missing. This table is the completion oracle: the
corpus is "done" when every row is accounted for, and it's re-checkable at any time by re-walking the
source docs.

## Applicability & exclusion rule

A feature is included in a given project if and only if:

1. That project's max language version has shipped the feature, **and**
2. The feature compiles cleanly under that project's TFM + project format, with no extra NuGet
   polyfill packages, **and**
3. The feature does not require a compiler switch the project deliberately leaves off.

If (2) fails, the feature is excluded from that project, recorded in `MANIFEST.md` with a one-line
reason (e.g. "CS8703: default interface members require runtime support absent on net48"), and
picked up in the next project up the chain where it does compile. This rule is what produced the
default-interface-members/ranges-and-indexes exclusion in the net48 C# 8.0 project, and it applies
uniformly to every project — including the VB.NET projects, where (unlike C#, whose
default `LangVersion` is TFM-gated by a known mapping table) the actual net48-vs-net10 applicability
gap, if any, is discovered empirically during research rather than assumed up front.

Clause (3) is a **capability** exclusion rather than a **policy** one, and the two are recorded
differently because they mean different things. A (2) exclusion says *this feature cannot exist
here*. A (3) exclusion says *this feature could compile here, but the corpus deliberately houses it
elsewhere so this project stays representative* — today its only instance is `/unsafe`, whose
`AllowUnsafeBlocks` switch is per-compilation and therefore cannot be confined to the folders that
need it. A (3) exclusion must name the project that does carry the feature, so no row is ever
excluded everywhere. A reader who conflates the two would wrongly conclude that net48 cannot run
pointer code.

## Scope of version fidelity

The MCP this corpus serves targets .NET Framework 4.8 and .NET Core / .NET 5+. The corpus therefore
invests in *what each construct looks like*, not in exhaustively reconstructing how a construct's
behavior changed between older C# versions. Specifically, for C# versions **before 7.3**:

- A feature earns a row when the source document lists it as a shipped feature. That is the whole
  inclusion rule.
- A change to an *already-shipped* feature's semantics or codegen — as opposed to its syntax — does
  **not** earn its own row. The worked example is the `lock` statement: its syntax is identical from
  C# 1.0 onward, and only the emitted `try`/`finally` shape changed (in C# 4.0, to close a window
  where an exception between `Monitor.Enter` and the `try` leaked the lock). The corpus does not
  attempt to capture that, and no row exists for it.

This is a deliberate budget decision rather than an oversight. From 7.3 onward the distinctions start
to matter to the tooling being built; before it they mostly do not, and the archaeology needed to get
them right is disproportionate to their value here.

### Stub files — the rare escape hatch

A version folder normally holds a working example. When a row is assigned to a project whose
`LangVersion` ceiling genuinely cannot express it, do not labor over an older-syntax variant: write a
single stub file in that project naming the reason and pointing at the project that does carry a
working example.

A stub is **only** for that case. A feature simply newer than a project's ceiling is not stubbed — it
is absent, and `MANIFEST.md`'s target and exclusion columns already record why. Stubs are expected to
be rare, possibly nonexistent, because the source document gives each increment of an evolving
feature its own row — `PartialMethods` alongside `PartialMethodsWithReturnedValues`,
`ExpressionBodiedMembers` alongside `ExpressionBodiedMembersExtended`, `StackallocInitializers`
alongside `StackallocNestedContexts` — and each such row is expressible at its own version.

A stub file contains comments only, in this shape:

```csharp
// <Feature> cannot be expressed under LangVersion <X>: <one-line reason>.
// A working example lives in <project>/<version folder>/<group folder>/.
```

A comments-only file compiles cleanly and declares no types, so it neither breaks the zero-warning
gate nor adds a phantom API. It contains no placeholder code and never says "TODO" — the explanation
*is* the content.

## Corpus verification contract

Corpus evidence has three layers:

1. Project builds prove validity at a declared SDK, TFM, and language-version coordinate.
2. Isolated compilation cases prove positive and negative feature boundaries.
3. Runtime cases prove comments that assert observable behavior.

These layers are complementary. A successful project build cannot establish a historical feature
boundary or prove a comment about runtime behavior. Every project build still requires **0 errors
and 0 warnings**. The build matrix discovers every SDK-style C# library project under
`CSharp/dotnet/` and every VB project under `VB.NET/`; `CorpusProjectDiscoveryTests` holds the exact
expected list, which is the count of record. The legacy `CSharp_v7.0` project needs Windows and
Visual Studio's `MSBuild.exe`:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" \
  examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj -t:Restore;Build
```

Two distinct toolchain gaps sit behind that, and only the first is about the host OS:

- **No net48 reference assemblies off Windows.** `dotnet build` against a net48 project on a Linux
  host fails with `MSB3644: reference assemblies for .NETFramework,Version=v4.8 were not found` —
  already precedented by `CSharpFwLegacy` in `testData/`, and not a defect in this corpus. Two
  places in the tree close it for themselves: the VB net48 family's `Directory.Build.props` and
  `CSharp_v8.0` carry `Microsoft.NETFramework.ReferenceAssemblies`, which supplies the reference
  assemblies from a package. That is the whole set — `CSharp_v1.0-Unsafe` and `CSharp_v8.0-Unsafe`
  are SDK-style net48 as well and carry nothing, and a legacy non-SDK project cannot consume the
  package at all, because it consumes no package assets under `dotnet build` — which is the second
  gap.
- **`dotnet build` cannot resolve `PackageReference` for a non-SDK project, even on Windows.** The
  SDK's MSBuild restores the project's packages and writes `project.assets.json`, then resolves none
  of them into references, because a non-SDK project consumes package assets through NuGet targets
  that ship with Visual Studio rather than with the SDK. The failure names the missing *types* —
  `CS0246` on `Span` and `ValueTask` — and says nothing about the toolchain, so it reads as a broken
  sample. It is not: the same project builds at 0 errors and 0 warnings under VS `MSBuild.exe`.

The legacy project also needs framework references the SDK-style projects get implicitly. The one
that costs the most time to diagnose is `Microsoft.CSharp`: without it the C# 4.0 `dynamic` row fails
with `CS0656` (missing `RuntimeBinder` members) at the *emit* stage. Emit-stage errors appear only
once binding succeeds, so any earlier unresolved-type error in the project hides them completely —
a probe that is missing some other reference will report a clean-looking absence of `CS0656` and
then produce it as soon as the unrelated error is fixed. Applicable SDK-style net48 projects need
the same explicit reference; the SDK does not add it implicitly on net48.

Manifest completeness is satisfied when:

- `MANIFEST.md` has no unaccounted-for row (every feature is either included in a named project, or
  excluded with a reason), and
- every project in the current on-disk matrix builds with 0 errors and 0 warnings.

That establishes authored coverage, not the stronger compilation-boundary or runtime layers.
Checked-in cases under `tests/DotNetKnowledge.Corpus.Tests/TestCases/` supply those layers for the
claims they name. Use [`../../scripts/install-corpus-test-sdks.md`](../../scripts/install-corpus-test-sdks.md)
to install or verify the exact SDKs in the repository-private test host; do not reproduce that setup
manually.

Selecting an older TFM does not select its historical compiler. For example, an SDK 10 build
targeting `net5.0` uses SDK 10's compiler against the `net5.0` reference pack. SDK selection,
`TargetFramework`, `LangVersion`, and runtime execution remain independent coordinates in every
case.

Any new source comment that asserts observable runtime behavior must carry the
language-appropriate marker in its canonical authored source:

```csharp
// Runtime verification: <case-id>
```

```vb
' Runtime verification: <case-id>
```

The case with that exact ID must contain at least one runtime expectation, and the marker must occur
in the canonical source path named by the case. Every runtime case whose source is in the corpus
must have exactly one canonical marker. Toolchain-only cases whose source lives under `tests/` are
marker-exempt. This contract establishes a mechanical link for declared runtime claims; it does not
classify every corpus row as runtime-verifiable.

### A project build cannot tell whether a sample demonstrates its feature

A clean build proves a sample is *valid* C#. It says nothing about whether the sample exercises the
feature its folder is named for. Every corpus project compiles at `LangVersion=latest`, so a file
that reaches forward to a newer construct, and a file that only shows something which already
worked in an earlier version, both build with 0 errors and 0 warnings.

Two cheap probes close that hole, and every authoring plan should run both:

- **Own-version pin.** Copy a version folder into a scratch project outside the repo pinned to that
  folder's own `LangVersion` and build. It must succeed. A failure names a file that reached past
  its era.
- **Previous-version pin — the stronger of the two.** Pin the same folder one language version
  lower. For most rows it must **fail**, with an error naming the feature. A sample that compiles at
  *N−1* is a strong signal that it demonstrates nothing — but it is a signal to investigate, not an
  automatic verdict. See the exemption below before changing any sample on this basis.

The second probe is the one that finds real defects, because the failures it catches are invisible
to every other gate. Three shipped samples passed compilation, per-task review, and the own-version
pin while demonstrating nothing: an "improved overload candidates" sample whose method group had no
mixed static/instance candidate set, a "movable fixed buffer" sample that indexed through a method
parameter (a *fixed* variable, always legal), and a non-trailing-named-argument comment asserting a
rule that a fully-named call has never been subject to. Each compiled clean at *N−1*.

**Pinning to `ISO-1` or `ISO-2` needs `GenerateTargetFrameworkAttribute=false`.** The SDK generates
an `AssemblyAttributes.cs` containing `[assembly: global::System.Runtime.Versioning.TargetFramework]`,
and `global::` is a C# 2.0 construct, so every C# 1.x pin fails with `CS8022: Feature 'namespace
alias qualifier' is not available in C# 1` no matter what the samples contain. The error names a
generated file rather than a sample, which is the tell. Suppress the attribute and the pin reports
only what the samples are responsible for.

Expect the failure code to vary by feature kind: a syntax addition yields `CS8320`/`CS8302`/`CS8370`/
`CS8400`/`CS8773` ("feature is not available in C# N"), while a resolution-rule change such as
improved overload candidates yields an ordinary `CS0121` ambiguity at *N−1* and resolves at *N*.
Both are valid evidence; what matters is that *N−1* fails for a reason tied to the feature.

#### What `LangVersion` does not gate — the probe's blind spot

`LangVersion` gates syntax and the semantics the specification ties to a version. It does **not**
gate two other kinds of compiler change, and rows of those kinds compile happily at *N−1* while
being entirely correct:

- **Analysis relaxations** — a version that stops reporting an error. Removing a diagnostic cannot
  break existing code, so Roslyn applies the relaxation unconditionally. C# 10.0's improved definite
  assignment is one: its canonical pre-10.0 `CS0165` case compiles clean even when pinned to 7.3,
  five versions below the feature, while a switch expression in the same project at the same pin
  still fails with `CS8370`. The pin is working; there is simply nothing for it to gate.
- **Compiler-injection behavior** — a value the compiler supplies at a call site.
  `CallerArgumentExpression` is one: pinned to 9.0 it still injects the argument's source text, and
  a run produces output identical to 10.0.

For a row of either kind the previous-version pin proves nothing, and forcing it to "fail" would
mean rewriting a correct sample into a wrong one. Verify these by **execution** instead — run the
sample and check the behavior the feature promises — and record the row as probe-exempt so the next
author does not re-litigate it.

Both probes apply to VB as well, and VB honors `LangVersion` the same way where it gates at all — a
VB 15 binary literal pinned to 14 fails with `BC36716`. Four VB-specific cautions:

- **A version label that the compiler rejects is a signal to re-check the row, not to work around
  it.** VB accepts `14`, `14.0`, `15`, `15.0`, `15.3`, `15.5`, `16`, `16.0`, `16.9` and `17.13` —
  both the bare and `.0` spellings where a whole version exists — but rejects `17` *and* `17.0` with
  `BC2014`, because there is no VB 17.0 language version at all. The manifest originally filed a row
  at *VB 17.0*; the compiler's rejection, the row's own cited source section, and
  `Language-Version-History.md` all pointed to 17.13, and the row was refiled there. Treat `BC2014`
  on a plausible-looking version as evidence about the row rather than an obstacle to route around.
- **The `Baseline/` folder spans VS.NET 2002 to VS2012**, so no single previous-version pin is
  meaningful for it. It is `EXEMPT` from the floor probe and gets the own-version check only, at
  VB 11, the highest rung the bucket can contain.
- **Probe one row at a time. A whole-project VB build stops early and under-reports.** This is not a
  theoretical caution: probed as part of a whole project, `PrivateProtectedAccessModifier` appeared
  to compile at `/langversion:14`. Probed in isolation it fails with `BC36716: Visual Basic 14.0 does
  not support Private Protected` and passes at 15.5, which is its real floor. A floor derived from a
  whole-project build is not evidence.
- **Most of VB's post-14 delta is ungated, and the ladder is what records it.** VB's later releases
  add recognition rather than syntax, so the previous-version pin has far less to bite on than
  anywhere in C#. Measured across both families, the rows whose floor sits below the version they are
  filed under are `SmarterNameResolution`, `XmlDocCommentImprovements`,
  `OverridesImplicitlyOverloads` and `AmbiguousInterfaceMethodResolution` (VB 14, floor 11),
  `ConsumingCSharpRefReturnValues` (VB 15, floor 11), `CommentsInMorePlaces` (VB 16.0, floor 14),
  `OptimizedFloatToIntConversion` (VB 16.0, floor 11), and all three VB 17.13 consumption rows
  (floor 14 or 11). Placing each row at every pin that compiles it turns that into a checked fact
  rather than a note, and `MANIFEST.md`'s **Measured floor** column reports it per row.

**The escalation to a native compiler ceiling barely reaches VB, and the manifest says so.** In
principle VB's story is better than C#'s: native ceilings exist at VB 14 (`Microsoft.Net.Compilers`
1.3.2, already cached for the C# 6 boundary), VB 11 (`v4.0.30319`) and VB 9 (`v3.5`). Below VB 14 the
only gaps are VB 10 and VB 12; above it there is no native ceiling at all, because VB 14 is the
highest one that exists. In practice the escalation settles exactly **one** distinct row of this
corpus,
`ConsumingCSharpRefReturnValues` — reported `UNGATED` at `native-ceiling`, because the VB 14 compiler
rejects it with `BC30657`/`BC30643` while the modern compiler accepts it at `/langversion:14`. The
reason the reach is that small is arithmetic, not a defect: a floor is probed against the rung
*below* the row's own version, the corpus has no VB 10 or VB 12 rows, and VB 14 is the only native
ceiling that lands one rung below a version this corpus files rows at. Every other VB floor rests on
`sdk-pin` — the installed SDK's compiler under `/langversion`, which is a fact about today's
toolchain and drifts as SDKs ship. That is why the manifest records the evidence tier beside the
number instead of publishing the number alone.

Known probe-exempt rows, each verified to compile below its own version. The VB entries' floors are
the measured ones — `dotnet scripts/verify-feature-floors.cs -- --language vb`, one row at a time,
identical in both families:

| Row | Version | Why the pin cannot gate it |
|---|---|---|
| `ImprovedDefiniteAssignment` | C# 10.0 | Analysis relaxation — compiles at 7.3 |
| `CallerArgumentExpression` | C# 10.0 | Injection behavior — injects identically at 9.0 |
| `LineSpanDirective` | C# 10.0 | Affects only sequence points and diagnostic positions |
| `ExtendedNameofScopeInAttributes` | C# 11.0 | Scope relaxation — compiles at 9.0 |
| `NumericIntPtr` | C# 11.0 | The operators are on `System.IntPtr` in the BCL, not language-gated |
| `NameofAccessingInstanceMembers` | C# 12.0 | Scope relaxation — compiles at 9.0 |
| `SmarterNameResolution` | VB 14 | Resolution-rule change — compiles at 11 |
| `XmlDocCommentImprovements` | VB 14 | Doc-comment handling, not syntax — compiles at 11 |
| `OverridesImplicitlyOverloads` | VB 14 | Binding-rule relaxation — compiles at 11 |
| `AmbiguousInterfaceMethodResolution` | VB 14 | Resolution-rule change — compiles at 11 |
| `ConsumingCSharpRefReturnValues` | VB 15 | Compiler behavior — compiles at 11 in both families, but the native VB 14 compiler rejects it, so the row is genuinely version-dependent and merely not `LangVersion`-gated |
| `CommentsInMorePlaces` | VB 16.0 | Parser relaxation — compiles at 14 |
| `OptimizedFloatToIntConversion` | VB 16.0 | An emit change; the results are identical, so nothing is observable — compiles at 11 |
| `CallerArgumentExpressionConsumption` | VB 17.13 | Compiler behavior, not a language gate — compiles at 14 |
| `OverloadResolutionPriorityConsumption` | VB 17.13 | Compiler behavior — compiles at 11 |
| `UnmanagedConstraintRecognition` | VB 17.13 | Compiler behavior — the constraint is metadata VB 17.13 learned to honor, and no `LangVersion` value can gate a change with no syntax; compiles at 11 |

`PrivateProtectedAccessModifier` is deliberately **not** in that table. It is gated, at 15.5.
