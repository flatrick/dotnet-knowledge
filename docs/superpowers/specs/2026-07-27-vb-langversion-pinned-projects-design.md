# VB.NET per-`LangVersion` pinned projects

## Problem

The C# corpus proves its examples against a declared language version: every C# project pins one
`<LangVersion>`, and a project holds exactly the rows that compile at that pin. A row's version
claim is therefore checked by a build rather than asserted by a folder name.

VB.NET has no such proof. Both VB projects compile at `LangVersion=latest`, so a VB sample filed
under `Vb16_9/` is only claimed to be a VB 16.9 feature — nothing rejects it if it is really a VB 14
construct, and nothing rejects a `Baseline/` sample that reaches forward into a later version.

That gap is not hypothetical. Two `Baseline/` samples use readonly auto-implemented properties, a
VB 14 feature, inside rows filed in the VS.NET 2002–VS2012 bucket. Both build clean today.

## Goal

Give VB the same per-`LangVersion` proof C# has, and make the corpus's VB half buildable without a
Windows targeting pack.

## Corpus layout

Each VB family gains a ladder of pinned projects under `<family-root>/<pin>/<kind>/`:

```
examples/language-features/VB.NET/
  dotnet/Net10/
    Directory.Build.props
    src/Baseline/… src/Vb14/… src/Vb15/… src/Vb17_13/
    11/library/  14/library/  15/library/  15.3/library/  15.5/library/
    16/library/  16.9/library/  17.13/library/  latest/library/
  dotNetFramework/v4.8/
    Directory.Build.props
    src/…
    11/library/ … latest/library/
    11/my/  latest/my/
```

The rungs are `11, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`. `vbc` accepts
`9, 10, 11, 12, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest` and rejects `17` and `17.0` with
`BC2014`; the ladder carries the rungs the corpus has rows for, plus `latest` as a ceiling check.

Rungs that admit nothing their predecessor did not are kept deliberately. A green build at 16 whose
row set equals 15.5's documents that VB 16 gates nothing, and a future gated row at that version has
a home already.

`Baseline/` is pinned at 11, its era ceiling. It spans VS.NET 2002 to VS2012, so no single floor is
meaningful for it, and the upstream sources give no per-version attribution below VB 14. The lower
rungs `9`, `10` and `12` get no project rather than inventing attribution the sources do not
support.

The `<kind>` segment is present even though `library` is currently the only kind for VB, so that
later project kinds slot in beside it without a move.

### Naming

`RootNamespace` follows the existing `Net<runtime>_<Language><LangVersion>_<Kind>` convention:
`Net10_Vb15_3_Library`, `Net48_Vb17_13_Library`, `Net48_Vb11_My`. The version's own underscore runs
together with the separator, exactly as the existing `Net48_CSharp7_1_Exe` already does.

Project files are named for their kind — `library.vbproj`, `my.vbproj` — matching the C# SDK-side
tree, with a sibling `.slnx`.

Version folders inside `src/` keep their current spelling (`Vb15_3/`, not `15.3/`) because they are
namespace segments.

## One source tree per family

Row sources live once per family under `src/`, and each project selects rows with explicit `Compile`
items:

```xml
<!-- Net10/14/library/library.vbproj -->
<Compile Include="../../src/Baseline/**/*.vb" LinkBase="Baseline" />
<Compile Include="../../src/Vb14/**/*.vb" LinkBase="Vb14" />
<!-- Rows above this pin that LangVersion does not gate; the green build is the evidence. -->
<Compile Include="../../src/Vb15/ConsumingCSharpRefReturnValues/**/*.vb" LinkBase="Vb15/ConsumingCSharpRefReturnValues" />
<Compile Include="../../src/Vb16_0/**/*.vb" LinkBase="Vb16_0" />
```

Whole-folder globs are used where a rung takes an entire version folder, per-row globs where it
takes part of one.

VB is the only half of the corpus where this works. A VB compilation prepends `RootNamespace` to
every declaration, so a sample declares a version-relative namespace (`Namespace Vb15.Tuples`) and
takes its project prefix at compile time. C# has no equivalent — each C# copy must differ on its
namespace line, which is why the C# tree duplicates its sources physically and this one does not.

A shared tree makes cross-project drift structurally impossible rather than test-enforced. It
introduces the opposite hazard: a row added to `src/` that no project globs is in the corpus and
compiled by nothing. A test covers that.

`src/` is per-family rather than shared across both families, because the two families genuinely
diverge on four rows: `MyNamespaceHelpers` (net48 only), `ConsumingCSharpRefReturnValues` (present in
both, different subject), and the two `Vb17_13` consumption rows `CallerArgumentExpressionConsumption`
and `OverloadResolutionPriorityConsumption` (net10 only, for the capability reasons the manifest
already records). A single cross-family tree would need an override convention that costs more than
the duplication it removes.

## Row placement is probe-derived

A project holds every row that compiles at its pin — including rows filed above it whose feature
`LangVersion` does not gate. This is the rule the C# tree already follows, and the green build is
what makes the ungatedness a checked fact rather than a note.

Probing the net10 family establishes the shape:

| Pin | What the rung adds |
|---|---|
| 11 | `Baseline/`, after the two repairs below |
| 14 | `Vb14/`, plus the higher rows `LangVersion` does not gate — `ConsumingCSharpRefReturnValues`, both `Vb16_0` rows, and all three `Vb17_13` rows |
| 15 | `Tuples`, `BinaryLiteralsAndDigitSeparators` |
| 15.3 | `NamedTupleInference` |
| 15.5 | `LeadingDigitSeparator`, `NonTrailingNamedArguments`, `PrivateProtectedAccessModifier` |
| 16 | nothing — VB 16 gates none of its rows |
| 16.9 | `ConsumingInitOnlyProperties` |
| 17.13 | nothing — all three rows are consumption rows |
| latest | nothing |

Many VB rows above VB 14 are not gated by `LangVersion` at all, consistent with what the showcase
design doc already says about VB's later releases adding recognition rather than syntax. The ladder
records that instead of implying otherwise.

### The table above is provisional, and the method matters

The rows above were derived from whole-project builds. `docs/HANDOFF.md` records that a VB
whole-project build stops early and under-reports — "2 errors where per-folder builds reported 5" —
and that is exactly what happened: `PrivateProtectedAccessModifier` first appeared to compile at
`/langversion:14` and does not. Probed in isolation it fails with
`BC36716: Visual Basic 14.0 does not support Private Protected` and passes at 15.5.

The implementation therefore probes **one row at a time**, never a whole project, and treats the
probe's output as authoritative over this table. net48 is probed independently of net10, since it
carries an extra row and a different `ConsumingCSharpRefReturnValues` subject.

### `LangVersion` is an overlay on the compiler, not a substitute for it

A pin restricts a modern compiler where Roslyn's binder calls the feature-availability check. Where
it does not, the pin admits whatever the installed SDK's compiler can already do. So two different
claims hide behind one green build:

- **the feature existed at that version**, and
- **the installed compiler does not gate it**.

Only the second is observable from `/langversion:` alone. Distinguishing them requires a compiler
whose *native* ceiling is the version in question.

`UnmanagedConstraintRecognition` shows why this is not academic. It compiles on
`Microsoft.Net.Compilers` 1.3.2 — a native VB 14 compiler from 2016, predating the `unmanaged`
constraint entirely. That compiler does not reject the row; it ignores the constraint. VB 17.13 added
no syntax, only the honoring of metadata that was always readable. No `LangVersion` value can gate
that, because there is nothing to gate — only the compiler binary differs.

This is why the escalation in the tooling section is load-bearing rather than a refinement. A floor
recorded from `/langversion:` alone means "the current SDK does not gate this here", which is a fact
about the installed toolchain and will drift as SDKs change. A floor confirmed against a native
ceiling means "this feature genuinely was not available then". The manifest's floor column records
which of the two a row's floor rests on, so a reader is never left to assume the stronger claim.

## Project files and shared props

Each family gets a `Directory.Build.props`. It must import its parent explicitly with
`GetPathOfFileAbove`, the way `examples/language-features/Directory.Build.props` already imports the
repository root's: MSBuild's automatic discovery stops at the first props file walking up from
`…/16.9/library/library.vbproj`, which will be the family file. Losing that chain would silently
drop `TreatWarningsAsErrors`, on which the corpus's zero-warning gate depends.

```xml
<!-- VB.NET/dotNetFramework/v4.8/Directory.Build.props -->
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <PackageReference Include="System.Memory" Version="4.5.5" />
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
    <ProjectReference Include="$(MSBuildThisFileDirectory)../../CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj" />
  </ItemGroup>
</Project>
```

The `$(MSBuildThisFileDirectory)` anchor is load-bearing. Items in a props file are evaluated in the
consuming project's context, so a bare relative path would resolve against each `library/` directory
and silently miss.

The net10 family's props has the same shape but carries only the parent import and
`EnableDefaultCompileItems=false`. That family's consumption rows draw on the net10 shared framework
directly, so it needs no packages and no project reference.

Each project file then carries only its coordinate:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Net48_Vb16_9_Library</RootNamespace>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>16.9</LangVersion>
  </PropertyGroup>
</Project>
```

`TargetFramework` stays in the project rather than moving to props. `CorpusProjectDiscovery` reads
raw project XML rather than evaluating MSBuild, and a project with no `TargetFramework` element makes
it throw. Re-implementing props inheritance in the discovery code would cost more than one repeated
line. The resulting split is coordinate in the project, family baggage in props.

The references in props are unconditional, so the pin-11 net48 project also carries package and
project references none of its `Baseline` rows use. Conditioning them is awkward — `$(LangVersion)`
is not yet set when props are evaluated. Leaving them unconditional is consistent with the corpus
rule that reference set and `LangVersion` are independent coordinates, with one consequence worth
stating plainly: **a pinned project is not an era emulation.** VB 11 code in the pin-11 project can
see types that shipped a decade later. The pin constrains the language, not the framework surface.
This is already true of the C# tree.

## `MyType` and building without Windows

`MyType=Windows` exists solely for the `MyNamespaceHelpers` row, which demonstrates the `My`
namespace the VB compiler synthesizes under that setting.

`MyType` is a per-compilation switch that cannot be scoped to a folder, which is the same reason C#
houses its `AllowUnsafeBlocks` and `OutputType` rows in separate `unsafe` and `exe` projects on a
sparse ladder rather than at every pin. `MyNamespaceHelpers` is treated the same way: the mainline
`library/` projects drop `MyType` entirely, and the row moves to a `my/` kind at the `11` and
`latest` rungs of the net48 family. Each `my.vbproj` sets `MyType=Windows` itself and globs only
`src/Baseline/MyNamespaceHelpers/`, so the setting never reaches a mainline project.

Separately, `Microsoft.NETFramework.ReferenceAssemblies` is added to the net48 VB family props and to
`CSharp_v8.0`, which the family references. Without it no `net48` project in this repository builds
on a machine with no .NET Framework targeting pack. This is the actual obstacle to building the
corpus on Linux; `MyType` is not obviously one, since the `My` accessors resolve against
`Microsoft.VisualBasic.dll`, which the reference-assemblies package supplies.

Both halves are verified on Windows: net48 `Baseline` at pin 11 builds clean with no `MyType` and
with the reference-assemblies package, and the isolated `my/` project builds clean with
`MyType=Windows` and the same package.

Linux buildability is a verification step with a known fallback. If `MyType=Windows` does not survive
there, only the `my/` projects are Windows-gated rather than the whole net48 VB family. The legacy
non-SDK C# net48 projects remain Windows-only regardless, because they need Visual Studio's
`MSBuild.exe`.

## Repairing the two forward-reaching Baseline samples

`Baseline/Attributes/Attributes.vb` and
`Baseline/AutoImplementedPropertiesAndCollectionInitializers/AutoImplementedPropertiesAndCollectionInitializers.vb`
both use `Public ReadOnly Property X As T` — a readonly auto-implemented property, which is VB 14.

Neither is what its row is about. The `Attributes` row demonstrates attributes; its `Reason` property
becomes a classic `ReadOnly` property over an explicit backing field, which is legal well below VB 11
and keeps the read-only intent. The auto-property row sits in the VS2010 bucket, and its comment
*"a ReadOnly one may still be assigned from a constructor"* describes a capability that arrived four
years later; that clause and its property are removed.

No new row is created. The manifest's cited source for VB 14 enumerates its rows and readonly
auto-implemented properties is not among them — they are all already present — so adding one would
assert coverage the source does not support. Nothing the manifest claims is lost, because readonly
auto-properties were never a row.

With both repairs applied, `Baseline` builds clean at pin 11 in both families. This is verified
rather than assumed: `docs/HANDOFF.md` records that a whole-project VB build can stop early and
under-report, so the repaired tree was re-probed rather than reasoned about.

## Tooling

**`CorpusProjectDiscovery`** gains a VB scan over `examples/language-features/VB.NET` for `*.vbproj`,
with the same SDK-style and library filters it applies to C#. `CorpusProjectBuildTests` then builds
every VB project at 0 errors and 0 warnings. This is what makes "proven at this language version"
true rather than asserted, and it is the load-bearing part of this spec.
`CorpusProjectDiscoveryTests` holds a hardcoded expected project list, which grows accordingly.

**`scripts/verify-feature-floors.cs`** gains a VB ladder alongside its C# one: the `vbc` version
list above, with `Vb15_3` → `15.3` folder-name mapping beside the existing `CSharp7_1` → `7.1`.

VB's escalation story is better than C#'s. `Microsoft.Net.Compilers` 1.3.2 — already downloaded and
cached for the C# 6 floor — ships a `vbc` whose native ceiling is VB 14, which sits directly beneath
VB's entire post-14 delta. The in-box `%WINDIR%\Microsoft.NET\Framework64` compilers give native
ceilings at VB 11 (`v4.0.30319`, which reports itself as "for Visual Basic 2012"), VB 9 (`v3.5`) and
VB 8 (`v2.0.50727`). Only VB 10 and 12 have no compiler and report `UNPROVEN`.

`Baseline/` is `EXEMPT` from the floor probe, since it spans several eras and no single floor is
meaningful; it gets the own-version check only.

**Locale hardening.** The probe currently locates diagnostics by matching the literal `": error "`.
The in-box compilers emit localized text on a non-English machine. Keying on `\b(BC|CS)\d{4}\b`
together with the process exit code is locale-independent, and fixes the same latent problem on the
existing C# path.

**`scripts/verify-project-namespaces.cs`** already accepts `.vbproj` and requires a `RootNamespace`,
so the new projects are covered with no change. It gains one inverse rule: a `.vb` file under a
family `src/` must not begin its namespace with a `Net10_` or `Net48_` prefix. VB prepends
`RootNamespace` itself, so such a file would compile to a doubled namespace — and under a shared tree
it would corrupt every pin at once.

**New orphan-row test.** Every row folder under each family's `src/` must be selected by at least one
project's `Compile` items. Without it, a row can be added to the corpus and compiled by nothing.

## Documentation

`MANIFEST.md`'s `Target Projects` column cannot enumerate a project per pin, so `VbFw48` and
`VbLatest` are redefined to name the two families, and the manifest gains a measured-floor column
recording the lowest pin at which each row compiles, together with whether that floor was confirmed
against a native compiler ceiling or only observed under the current SDK. That column is the
ladder's payoff: it turns "VB 16.9 feature" into a probed claim, and it keeps the two strengths of
evidence distinguishable rather than collapsing them into one number.

Two prose corrections in the same file. The claim that the two VB projects' sources are
byte-identical is replaced by a description of the shared tree and the four genuine divergences; the
current sentence names two of them. The VB section heading miscounts its point versions.

Fixed counts are removed from the documents rather than corrected — in `MANIFEST.md`'s VB and C#
section headings, and in `AGENTS.md` and `docs/HANDOFF.md` where they describe how many projects the
build matrix discovers. A count is stale the moment a project or row is added, and the surrounding
convention is already to state current truth without narration that dates itself.

`CLAUDE.md` needs its corpus-layout section updated, and its paragraph on VB's namespace exemption
extended with the new inverse rule. `docs/design/language-feature-showcase-design.md` already
predicted the `Baseline/` problem and already carries a probe-exempt table; that table gains
`PrivateProtectedAccessModifier`, and its VB cautions absorb the measured result that most post-14
VB rows are ungated.

## Definition of done

1. Every VB project builds at 0 errors and 0 warnings through `CorpusProjectBuildTests`.
2. `dotnet scripts/verify-project-namespaces.cs` is clean, including the new inverse VB rule.
3. `dotnet scripts/verify-feature-floors.cs` runs the VB ladder with no `MISPLACED` and no
   `NOT-VERSION-SPECIFIC`.
4. The orphan-row test passes.
5. `dotnet scripts/verify-no-vendored-content.cs` is clean. `CLAUDE.md` requires this before any
   change that adds files in bulk.
6. The C# half of the suite is untouched and still green.

## Risks

- **net48 floors are measured, not inherited.** The probe table above is net10. net48 carries an
  extra row and a different ref-return subject.
- **Whole-project probing under-reports.** It already produced one wrong floor in this spec. The
  probe must compile one row at a time, and a result derived any other way is not evidence.
- **A floor from `/langversion:` alone is a fact about the installed SDK.** It will drift as SDKs
  ship. Only floors confirmed against a native compiler ceiling are stable, and the escalation
  reaches native ceilings at VB 14, 11, 9 and 8 — not at 10, 12, or anywhere above 14.
- **The in-box `vbc` compilers reading net48 reference assemblies is partly shown.** The native
  VB 14 compiler compiled a corpus row against net48 reference assemblies during design. The older
  in-box compilers have not been exercised, and assembling a correct reference set for them is
  itself fiddly — an incomplete set yields ordinary `BC30002`/`BC30451` errors that read like
  gating but are not.
- **Linux buildability is unverified from the authoring machine.** The reference-assemblies package
  is the known prerequisite; whether `MyType=Windows` survives there is open, with the fallback
  above.
- **Build time grows** with a project per pin per family. If the suite becomes slow enough to
  discourage running it, the ladder is the thing to trim, and the rungs that add no rows are the
  candidates.

## Out of scope

The C# tree. Its physically duplicated sources are a consequence of C#'s per-copy namespace line and
cannot adopt the shared-tree model without a separate decision. The only C# change here is adding
the reference-assemblies package to `CSharp_v8.0`, which the net48 VB family references.
