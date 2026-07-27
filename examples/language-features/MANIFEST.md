# Language-feature showcase — coverage manifest

Every row is sourced from `external/csharplang/Language-Version-History.md` (C#) or
`https://learn.microsoft.com/dotnet/visual-basic/whats-new/` + `external/vblang/spec/` (VB.NET —
see the design doc's sourcing-strategy section for why VB.NET needs two sources). The **Target
project(s)** column names the project(s) each feature is destined for — it is a plan, not a record of
what exists on disk today. The **Authoring status** section near the end of this file is the record
of what has actually been authored so far: a row counts as authored for a project once its example
file(s) exist under that project's matching version/group folder and the project builds with 0
errors and 0 warnings; until then, that row is simply not yet authored there — not a placeholder,
the accurate current state of an in-progress corpus.

**Project codes:** `Fw73` = Net48_CSharp7_3_Library, `Fw80` = Net48_CSharp8_Library,
`Latest` = Net10_CSharpLatest_Library, `Fw80Unsafe` = Net48_CSharp8_Unsafe,
`Net10Unsafe` = Net10_CSharpLatest_Unsafe, `Net10Exe` = Net10_CSharpLatest_Exe,
`VbFw48` = the net48 VB **family**, `VbLatest` = the net10 VB **family**.

Every project's `RootNamespace` is its coordinate — runtime, language version, output kind — and
every C# file under it declares that value as its first namespace segment, so an open file names the
project it belongs to. `dotnet scripts/verify-project-namespaces.cs` enforces the pairing; C#'s
`RootNamespace` seeds only new-file templates, so nothing else would.

**The C# codes above name the ceiling projects only.** The corpus also carries a C# project per
pinned `<LangVersion>` — `Net48_CSharp1_Library` … `Net48_CSharp7_2_Library`,
`Net10_CSharp10_Library` … `Net10_CSharp14_Library`, and `Net5_CSharp10_Library` …
`Net9_CSharp10_Library`. The **Target project(s)** column below has not been extended to them.

**The two VB codes name families, not single projects.** A VB family is one shared `src/` tree plus
a project per pinned `<LangVersion>`:

| Code | Family root | Projects |
|---|---|---|
| `VbFw48` | `VB.NET/dotNetFramework/v4.8/` | `src/`, `<pin>/library/` at every pin, plus `11/my/` and `latest/my/` |
| `VbLatest` | `VB.NET/dotnet/Net10/` | `src/`, `<pin>/library/` at every pin |

The pins are `11, 14, 15, 15.3, 15.5, 16, 16.9, 17.13, latest`. `vbc` rejects `17` and `17.0` with
`BC2014`, so those rungs do not exist. Each project's `Compile` globs name the rows it holds, and a
project holds **every** row that compiles at its pin — including rows filed above it whose feature
`LangVersion` does not gate. A row targeting `VbFw48` or `VbLatest` therefore lives in that family's
projects from the pin its **Measured floor** cell names upward.

**`MyType=Windows` lives only in the `my/` projects.** It is a per-compilation switch, structurally
the same constraint as `AllowUnsafeBlocks` and `OutputType` on the C# side, so the
`MyNamespaceHelpers` row is housed apart instead of imposing the setting on every mainline project.
Every mainline `library.vbproj` `Compile Remove`s that row. The `my/` kind exists at the `11` and
`latest` rungs of the net48 family only.

**The net48 VB family and `CSharp_v8.0` carry `Microsoft.NETFramework.ReferenceAssemblies`**, so
they build without a machine-installed .NET Framework targeting pack. The net48 VB family
project-references `CSharp_v8.0` for its ref-return subject, so both halves need the package.

**The Measured floor column is probed, not asserted.** It names the lowest pin at which the row
compiles, and in parentheses the kind of evidence that floor rests on, in the vocabulary
`dotnet scripts/verify-feature-floors.cs -- --language vb` emits:

- `native-ceiling` — a compiler whose native ceiling is the rung below settled it. Stable: it does
  not drift as SDKs ship.
- `sdk-pin` — only the installed SDK's compiler under `/langversion` bears on the floor. That is a
  fact about today's toolchain rather than about the language, and it drifts as SDKs ship.
- `none` — nothing the probe compiled bears on a floor. Every `Baseline` row is here: the bucket is
  `EXEMPT` from the floor probe because it spans VS.NET 2002 to VS2012, and `11` is the ladder's
  lowest rung in any case.

Both families measure the same floor and the same evidence for every row they share, so one column
serves both. A floor below the row's own version is not a defect — it is the measured statement that
`LangVersion` does not gate that feature, which the lower project's green build then keeps true.

**`Net10_CSharpLatest_Exe` carries only what needs an entry point.** `OutputType` is a per-project setting
and cannot be scoped to a folder, in exactly the way `/unsafe` cannot. Top-level statements require
the compilation to be an executable — a library is rejected outright with `CS8805` — so that row
lives here rather than forcing every other example in a mainline project to share an entry point.
A compilation may contain at most one file with top-level statements, so this project can never
hold more than one such row.

**The two `*Unsafe` projects carry only what needs `/unsafe`.** `AllowUnsafeBlocks` is a
per-compilation switch and cannot be scoped to a folder, so a feature requiring it would force the
switch on every example sharing its project. The mainline projects therefore stay on default
compilation, and the handful of unsafe-requiring rows live in `Fw80Unsafe` (net48, C# 8.0 ceiling)
and `Net10Unsafe` (net10.0, latest) instead. Those rows show the mainline projects in the "Excluded
from" column with that reason — a **policy** exclusion, not a capability one: the feature compiles
fine on that TFM, it is simply housed elsewhere. See the design doc's applicability rule, clause 3.

**`CSharpComTypeLib` is a support assembly, not a corpus project.** It holds no feature examples and
never appears in a target column. It exists so the *Embedded interop types (NoPIA)* row can embed
from something: NoPIA is a property of a reference rather than a source construct, so demonstrating
it needs an assembly marked `[assembly: ImportedFromTypeLib]` to reference with
`EmbedInteropTypes="true"`.

**A blank "Excluded from" cell is the common case, not an omission.** When a row's "Target
project(s)" cell leaves out a project, and no reason is given, the convention is that the omission is
simply because that project's language-version ceiling is below the feature's version — an
unremarkable, self-evident exclusion — or because the project is one of the two `*Unsafe` projects,
per the paragraph above. Only *non-obvious* exclusions — a capability gap (2) or a policy choice (3)
requiring an explicit note, per the design doc's applicability rule — get a reason spelled out in this column.

**Non-feature bullets in the source documents are not rows here.** Some source-document entries
describe tooling or process rather than a language construct — for example C# 6.0's "Draft
Specification online" and "Compiler-as-a-service (Roslyn)" bullets in
`Language-Version-History.md`. Those are intentionally not enumerated as rows in this manifest.

**Per-project caveat (stated once, not per-row):** `Fw73` (legacy XML, non-SDK) must be built with
Visual Studio's `MSBuild.exe` on Windows, not with `dotnet build`. The .NET SDK's MSBuild restores
the project's `PackageReference` items and then resolves none of them, because a non-SDK project
consumes package assets only through the NuGet targets that ship with Visual Studio; the result is
`CS0246` on `Span` and `ValueTask` rather than a message about the toolchain. See the design doc's
build-verification section. Every row naming `Fw73` in "Target project(s)" is subject to that same
caveat; it is not repeated per row.

**Every C# project is hand-authored.** `scripts/generate-net48-examples.cs` targets a project layout
that no longer exists and must not be run against this tree; the `GENERATED-COMPILE-ITEMS` markers
inside the net48 csproj files are inherited artifacts that nothing regenerates. Edit the file you
mean to change, in the project you mean to change it in.

**Each VB family holds one copy of every row, under its own `src/` tree.** VB prepends
`RootNamespace` to every declaration in the compilation, so a VB sample declares a version-relative
namespace (`Namespace Vb15.Tuples`) and takes its project prefix at compile time — which is why
every pinned project in a family globs the same files, and why no VB source names a project.
`dotnet scripts/verify-project-namespaces.cs` enforces the inverse of the C# rule here: a `.vb` file
under a family's `src/` must **not** begin its namespace with a `Net10_` or `Net48_` prefix, which
would compile to a doubled namespace in every pin of that family at once.

The two families keep separate `src/` trees because four rows genuinely diverge:

| Row | Divergence |
|---|---|
| `MyNamespaceHelpers` | `VbFw48` only, in the `my/` kind. No net10 project can carry it — see its Note. |
| `ConsumingCSharpRefReturnValues` | In both, over different ref-returning subjects — see its Note. |
| `CallerArgumentExpressionConsumption` | `VbLatest` only. net48 has no attributed API to consume — see its Note. |
| `OverloadResolutionPriorityConsumption` | `VbLatest` only. No net48 assembly here can supply a prioritized API — see its Note. |

## C#

| Version | Feature(s) | Group folder | Target project(s) | Excluded from (reason) | Source | Note |
|---|---|---|---|---|---|---|
| C# 1.0 | Classes, Structs, Enums | `ClassesStructsEnums` | Fw73, Fw80, Latest | | Language-Version-History.md § C# 1.0 | |
| C# 1.0 | Interfaces | `Interfaces` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Events, Delegates | `EventsAndDelegates` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Operator overloading, user-defined conversion operators | `OperatorOverloading` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Properties, Indexers | `PropertiesAndIndexers` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Output parameters (out/ref), params arrays | `ParameterModifiers` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | using statement, goto statement | `ControlFlowStatements` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Preprocessor directives | `Preprocessor` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Unsafe code and pointers | `UnsafeCodeAndPointers` | Fw80Unsafe, Net10Unsafe | Fw73, Fw80, Latest (policy: needs `/unsafe`, housed in the `*Unsafe` projects) | § C# 1.0 | |
| C# 1.0 | Attributes | `Attributes` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Literals, verbatim identifiers, unsigned integer types, expressions | `LiteralsAndExpressions` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.0 | Boxing and unboxing | `Boxing` | Fw73, Fw80, Latest | | § C# 1.0 | |
| C# 1.2 | Dispose in foreach, foreach over string specialization | `ForeachEnhancements` | Fw73, Fw80, Latest | | § C# 1.2 | |
| C# 2.0 | Generics | `Generics` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Partial types | `PartialTypes` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Anonymous methods | `AnonymousMethods` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Iterators (yield) | `Iterators` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Nullable value types | `NullableValueTypes` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Getter/setter separate accessibility | `PropertyAccessorAccessibility` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Method group conversions, delegate inference | `DelegateInferenceAndConversions` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Static classes | `StaticClasses` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 2.0 | Type and namespace aliases | `NamespaceAndTypeAliases` | Fw73, Fw80, Latest | | § C# 2 | The source document files using-alias directives under C# 2.0, but they shipped in C# 1.0; what C# 2.0 genuinely added is the `global::`/`::` qualifier and extern aliases. |
| C# 2.0 | Covariance and contravariance | `Variance` | Fw73, Fw80, Latest | | § C# 2 | |
| C# 3.0 | Implicitly typed locals (`var`) | `ImplicitlyTypedLocals` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Object and collection initializers | `ObjectCollectionInitializers` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Auto-implemented properties | `AutoImplementedProperties` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Anonymous types | `AnonymousTypes` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Extension methods | `ExtensionMethods` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Query expressions (LINQ) | `Linq` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Lambda expressions | `LambdaExpressions` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Expression trees | `ExpressionTrees` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Partial methods | `PartialMethods` | Fw73, Fw80, Latest | | § C# 3 | |
| C# 3.0 | Lock statement | `LockStatement` | Fw73, Fw80, Latest | | § C# 3 | The source document files this under C# 3.0, but the `lock` statement shipped in C# 1.0 (ECMA-334 1st edition). csharplang commit `f4b11176` split C# 1.0's catch-all "Statements" bullet into named statements, moving `using` and `goto` into C# 1.0 and landing `lock` in the C# 3 list; its link targets the general `lock` reference page, not a C# 3 change note. |
| C# 4.0 | Dynamic binding | `DynamicBinding` | Fw73, Fw80, Latest | | § C# 4 | |
| C# 4.0 | Named and optional arguments | `NamedAndOptionalArguments` | Fw73, Fw80, Latest | | § C# 4 | |
| C# 4.0 | Co/contra-variance for generic delegates/interfaces | `GenericDelegateVariance` | Fw73, Fw80, Latest | | § C# 4 | |
| C# 4.0 | Embedded interop types (NoPIA) | `EmbeddedInteropTypes` | Fw73, Fw80, Latest | | § C# 4 | |
| C# 5.0 | Asynchronous methods | `AsyncAwait` | Fw73, Fw80, Latest | | § C# 5 | |
| C# 5.0 | Caller info attributes | `CallerInfoAttributes` | Fw73, Fw80, Latest | | § C# 5 | |
| C# 5.0 | foreach loop variable per-iteration scoping | `ForeachLoopVariableScope` | Fw73, Fw80, Latest | | § C# 5 | |
| C# 6.0 | `using static` | `UsingStatic` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Exception filters | `ExceptionFilters` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Await in catch/finally | `AwaitInCatchFinally` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Auto property initializers, default values for getter-only properties | `AutoPropertyInitializers` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Expression-bodied members | `ExpressionBodiedMembers` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Null-conditional operator | `NullConditionalOperator` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | String interpolation | `StringInterpolation` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | `nameof` operator | `NameOfOperator` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 6.0 | Dictionary initializer | `DictionaryInitializer` | Fw73, Fw80, Latest | | § C# 6 | |
| C# 7.0 | Out variables | `OutVariables` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Pattern matching (`is` type pattern) | `PatternMatching70` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Tuples, deconstruction | `TuplesAndDeconstruction` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Discards | `Discards` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Local functions | `LocalFunctions` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Binary literals, digit separators | `NumericLiteralImprovements` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Ref returns and locals | `RefReturnsAndLocals` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Generalized async return types | `GeneralizedAsyncReturnTypes` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | More expression-bodied members | `ExpressionBodiedMembersExtended` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.0 | Throw expressions | `ThrowExpressions` | Fw73, Fw80, Latest | | § C# 7.0 | |
| C# 7.1 | Async main | `AsyncMain` | Fw73, Fw80, Latest | | § C# 7.1 | |
| C# 7.1 | Default literal expressions | `DefaultLiteralExpressions` | Fw73, Fw80, Latest | | § C# 7.1 | |
| C# 7.1 | Inferred tuple element names | `InferredTupleElementNames` | Fw73, Fw80, Latest | | § C# 7.1 | |
| C# 7.1 | Pattern matching with generics | `GenericPatternMatching` | Fw73, Fw80, Latest | | § C# 7.1 | |
| C# 7.1 | Reference assemblies | — | | Fw73, Fw80, Latest (compiler/tooling output feature, not a source-level construct) | § C# 7.1 | |
| C# 7.2 | Span and ref-like types | `SpanAndRefLikeTypes` | Fw73*, Fw80 (needs `System.Memory`), Latest | | § C# 7.2 | |
| C# 7.2 | In parameters, readonly references | `InParametersReadonlyReferences` | Fw73, Fw80, Latest | | § C# 7.2 | |
| C# 7.2 | Ref conditional expressions | `RefConditionalExpressions` | Fw73, Fw80, Latest | | § C# 7.2 | |
| C# 7.2 | Non-trailing named arguments | `NonTrailingNamedArguments` | Fw73, Fw80, Latest | | § C# 7.2 | |
| C# 7.2 | Private protected accessibility | `PrivateProtectedAccessibility` | Fw73, Fw80, Latest | | § C# 7.2 | |
| C# 7.2 | Digit separator after base specifier | `DigitSeparatorAfterBaseSpecifier` | Fw73, Fw80, Latest | | § C# 7.2 | |
| C# 7.3 | `unmanaged`/`Enum`/`Delegate` constraints | `ExtendedGenericConstraints` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 7.3 | Ref local reassignment | `RefLocalReassignment` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 7.3 | Stackalloc initializers (Span) | `StackallocInitializers` | Fw73*, Fw80 (needs `System.Memory`), Latest | | § C# 7.3 | |
| C# 7.3 | Indexing movable fixed buffers | `FixedBufferIndexing` | Fw80Unsafe, Net10Unsafe | Fw73, Fw80, Latest (policy: needs `/unsafe`, housed in the `*Unsafe` projects) | § C# 7.3 | |
| C# 7.3 | Custom `fixed` statement (pattern-based) | `CustomFixedStatement` | Fw80Unsafe, Net10Unsafe | Fw73, Fw80, Latest (policy: needs `/unsafe`, housed in the `*Unsafe` projects) | § C# 7.3 | |
| C# 7.3 | Improved overload candidates | `OverloadResolutionImprovements73` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 7.3 | Expression variables in initializers/queries | `ExpressionVariablesInInitializers` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 7.3 | Tuple comparison | `TupleEquality` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 7.3 | Attributes on backing fields | `BackingFieldAttributes` | Fw73, Fw80, Latest | | § C# 7.3 | |
| C# 8.0 | Nullable reference types | `NullableReferenceTypes` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Default interface members | `DefaultInterfaceMembers` | Latest | Fw80 (CS8701/CS8703 — target runtime doesn't support default interface implementation; net48 doesn't advertise `RuntimeFeature.DefaultImplementationsOfInterfaces`; verified via `dotnet build` probe) | § C# 8.0 | |
| C# 8.0 | Recursive patterns, switch expressions | `RecursivePatternsSwitchExpressions` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Async streams (`await foreach`/`await using`, async iterators) | `AsyncStreams` | Fw80 (needs `Microsoft.Bcl.AsyncInterfaces`), Latest | | § C# 8.0 | |
| C# 8.0 | Enhanced `using` declarations | `EnhancedUsingDeclarations` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Ranges and indexes | `RangesAndIndexes` | Latest | Fw80 (`System.Index`/`System.Range` have no official net48 backport package — CS0518/CS0656 confirmed via probe even with `System.Memory` referenced) | § C# 8.0 | |
| C# 8.0 | Null-coalescing assignment | `NullCoalescingAssignment` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Static local functions | `StaticLocalFunctions` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Unmanaged generic structs | `UnmanagedGenericStructs` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Readonly members | `ReadonlyMembers` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Stackalloc in nested contexts | `StackallocNestedContexts` | Fw80 (needs `System.Memory`), Latest | | § C# 8.0 | |
| C# 8.0 | Alternative interpolated verbatim strings (`@$"..."`) | `AlternativeInterpolatedVerbatimStrings` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | Obsolete on property accessors | `ObsoleteOnPropertyAccessors` | Fw80, Latest | | § C# 8.0 | |
| C# 8.0 | `is null` on unconstrained type parameter | `IsNullOnUnconstrainedTypeParameter` | Fw80, Latest | | § C# 8.0 | |
| C# 9.0 | Records, `with` expressions | `RecordsAndWithExpressions` | Latest | | § C# 9.0 | |
| C# 9.0 | Init-only setters | `InitOnlySetters` | Latest | | § C# 9.0 | |
| C# 9.0 | Top-level statements | `TopLevelStatements` | Net10Exe | Fw73, Fw80, Latest (capability: the feature requires an executable compilation — a library is rejected with `CS8805`) | § C# 9.0 | |
| C# 9.0 | Pattern matching enhancements (relational, combinator, parenthesized, type patterns) | `PatternMatchingEnhancements9` | Latest | | § C# 9.0 | |
| C# 9.0 | Native sized integers (`nint`/`nuint`) | `NativeSizedIntegers` | Latest | | § C# 9.0 | |
| C# 9.0 | Function pointers | `FunctionPointers` | Net10Unsafe | Latest (policy: needs `/unsafe`, housed in `Net10Unsafe`) | § C# 9.0 | |
| C# 9.0 | `[SkipLocalsInit]` | `SkipLocalsInit` | Net10Unsafe | Latest (policy: needs `/unsafe` despite being an attribute rather than a pointer construct — CS0227 confirmed via probe; housed in `Net10Unsafe`) | § C# 9.0 | |
| C# 9.0 | Target-typed `new` expressions | `TargetTypedNewExpressions` | Latest | | § C# 9.0 | |
| C# 9.0 | Static anonymous functions | `StaticAnonymousFunctions` | Latest | | § C# 9.0 | |
| C# 9.0 | Target-typed conditional expressions | `TargetTypedConditionalExpressions` | Latest | | § C# 9.0 | |
| C# 9.0 | Covariant return types | `CovariantReturnTypes` | Latest | | § C# 9.0 | |
| C# 9.0 | Lambda discard parameters | `LambdaDiscardParameters` | Latest | | § C# 9.0 | |
| C# 9.0 | Attributes on local functions | `AttributesOnLocalFunctions` | Latest | | § C# 9.0 | |
| C# 9.0 | Module initializers | `ModuleInitializers` | Latest | | § C# 9.0 | |
| C# 9.0 | Extension `GetEnumerator` | `ExtensionGetEnumerator` | Latest | | § C# 9.0 | |
| C# 9.0 | Partial methods with returned values | `PartialMethodsWithReturnedValues` | Latest | | § C# 9.0 | |
| C# 9.0 | Source generators | — | | Latest (requires a separate analyzer/generator project, not a single-file example; out of scope for this corpus — see `testData/CSharpInMemoryGen` for the existing generator fixture) | § C# 9.0 | |
| C# 10.0 | Record structs, `with` on structs/anonymous types | `RecordStructsAndWithExpressions` | Latest | | § C# 10.0 | |
| C# 10.0 | Global using directives | `GlobalUsingDirectives` | Latest | | § C# 10.0 | |
| C# 10.0 | Improved definite assignment | `ImprovedDefiniteAssignment` | Latest | | § C# 10.0 | |
| C# 10.0 | Constant interpolated strings | `ConstantInterpolatedStrings` | Latest | | § C# 10.0 | |
| C# 10.0 | Extended property patterns | `ExtendedPropertyPatterns` | Latest | | § C# 10.0 | |
| C# 10.0 | Sealed record `ToString` | `SealedRecordToString` | Latest | | § C# 10.0 | |
| C# 10.0 | Incremental source generators | — | | Latest (tooling/generator-pipeline feature, same reasoning as C# 9.0 source generators) | § C# 10.0 | |
| C# 10.0 | Mixed deconstructions | `MixedDeconstructions` | Latest | | § C# 10.0 | |
| C# 10.0 | Method-level `AsyncMethodBuilder` | `MethodLevelAsyncMethodBuilder` | Latest | | § C# 10.0 | |
| C# 10.0 | `#line` span directive | `LineSpanDirective` | Latest | | § C# 10.0 | |
| C# 10.0 | Lambda improvements (attributes, return types, natural delegate type) | `LambdaImprovements` | Latest | | § C# 10.0 | |
| C# 10.0 | Interpolated string handlers | `InterpolatedStringHandlers` | Latest | | § C# 10.0 | |
| C# 10.0 | File-scoped namespaces | `FileScopedNamespaces` | Latest | | § C# 10.0 | |
| C# 10.0 | Parameterless struct constructors | `ParameterlessStructConstructors` | Latest | | § C# 10.0 | |
| C# 10.0 | `CallerArgumentExpression` | `CallerArgumentExpression` | Latest | | § C# 10.0 | |
| C# 11.0 | Raw string literals | `RawStringLiterals` | Latest | | § C# 11.0 | |
| C# 11.0 | UTF-8 string literals | `Utf8StringLiterals` | Latest | | § C# 11.0 | |
| C# 11.0 | Pattern match `Span<char>` on constant string | `SpanCharPatternMatching` | Latest | | § C# 11.0 | |
| C# 11.0 | Newlines in interpolations | `NewlinesInInterpolations` | Latest | | § C# 11.0 | |
| C# 11.0 | List patterns | `ListPatterns` | Latest | | § C# 11.0 | |
| C# 11.0 | File-local types | `FileLocalTypes` | Latest | | § C# 11.0 | |
| C# 11.0 | Ref fields, `scoped`, `[UnscopedRef]` | `RefFields` | Latest | | § C# 11.0 | |
| C# 11.0 | Required members | `RequiredMembers` | Latest | | § C# 11.0 | |
| C# 11.0 | Static abstract members in interfaces | `StaticAbstractMembersInInterfaces` | Latest | | § C# 11.0 | |
| C# 11.0 | Unsigned right-shift operator | `UnsignedRightShiftOperator` | Latest | | § C# 11.0 | |
| C# 11.0 | `checked` user-defined operators | `CheckedUserDefinedOperators` | Latest | | § C# 11.0 | |
| C# 11.0 | Relaxed shift operator requirements | `RelaxedShiftOperatorRequirements` | Latest | | § C# 11.0 | |
| C# 11.0 | Numeric IntPtr | `NumericIntPtr` | Latest | | § C# 11.0 | |
| C# 11.0 | Auto-default structs | `AutoDefaultStructs` | Latest | | § C# 11.0 | |
| C# 11.0 | Generic attributes | `GenericAttributes` | Latest | | § C# 11.0 | |
| C# 11.0 | Extended `nameof` scope in attributes | `ExtendedNameofScopeInAttributes` | Latest | | § C# 11.0 | |
| C# 12.0 | Collection expressions | `CollectionExpressions` | Latest | | § C# 12.0 | |
| C# 12.0 | Primary constructors | `PrimaryConstructors` | Latest | | § C# 12.0 | |
| C# 12.0 | Inline arrays | `InlineArrays` | Latest | | § C# 12.0 | |
| C# 12.0 | Using aliases for any type | `UsingAliasesForAnyType` | Latest | | § C# 12.0 | |
| C# 12.0 | Ref readonly parameters | `RefReadonlyParameters` | Latest | | § C# 12.0 | |
| C# 12.0 | `nameof` accessing instance members | `NameofAccessingInstanceMembers` | Latest | | § C# 12.0 | |
| C# 12.0 | Lambda optional parameters | `LambdaOptionalParameters` | Latest | | § C# 12.0 | |
| C# 13.0 | `\e` ESC escape sequence | `EscEscapeSequence` | Latest | | § C# 13.0 | |
| C# 13.0 | Method group natural type improvements | `MethodGroupNaturalTypeImprovements` | Latest | | § C# 13.0 | |
| C# 13.0 | `Lock` object | `LockObject` | Latest | | § C# 13.0 | |
| C# 13.0 | Implicit indexer access in object initializers | `ImplicitIndexerInObjectInitializers` | Latest | | § C# 13.0 | |
| C# 13.0 | `params` collections | `ParamsCollections` | Latest | | § C# 13.0 | |
| C# 13.0 | `ref`/`unsafe` in iterators/async | `RefUnsafeInIteratorsAsync` | Net10Unsafe | Latest (policy: needs `/unsafe`, housed in `Net10Unsafe`) | § C# 13.0 | |
| C# 13.0 | `ref struct` interfaces, `allows ref struct` | `RefStructInterfaces` | Latest | | § C# 13.0 | |
| C# 13.0 | Overload resolution priority | `OverloadResolutionPriority` | Latest | | § C# 13.0 | |
| C# 13.0 | Partial properties | `PartialProperties` | Latest | | § C# 13.0 | |
| C# 13.0 | Better conversion from collection expression element | `BetterConversionFromCollectionExpressionElement` | Latest | | § C# 13.0 | |
| C# 14.0 | Extension methods and properties | `ExtensionMethodsAndProperties` | Latest | | § C# 14.0 | |
| C# 14.0 | Extension operators | `ExtensionOperators` | Latest | | § C# 14.0 | |
| C# 14.0 | `field` keyword in properties | `FieldKeywordInProperties` | Latest | | § C# 14.0 | |
| C# 14.0 | Partial events and constructors | `PartialEventsAndConstructors` | Latest | | § C# 14.0 | |
| C# 14.0 | User-defined compound assignment operators | `UserDefinedCompoundAssignmentOperators` | Latest | | § C# 14.0 | |
| C# 14.0 | First-class `Span` types | `FirstClassSpanTypes` | Latest | | § C# 14.0 | |
| C# 14.0 | Null-conditional assignment | `NullConditionalAssignment` | Latest | | § C# 14.0 | |
| C# 14.0 | Unbound generic types in `nameof` | `UnboundGenericTypesInNameof` | Latest | | § C# 14.0 | |
| C# 14.0 | Simple lambda parameters with modifiers | `SimpleLambdaParametersWithModifiers` | Latest | | § C# 14.0 | |
| C# 14.0 | Ignored directives (`#:`) | `IgnoredDirectives` | Latest | | § C# 14.0 | |
| C# 14.0 | Optional/named arguments in expression trees | `OptionalAndNamedArgumentsInExpressionTrees` | Latest | | § C# 14.0 | |

\* `Fw73` inclusion for `System.Memory`-dependent rows is by the full-cumulative rule (project 1's
ceiling is 7.3, which covers 7.2/7.3). Those rows build there: `Fw73` declares `System.Memory` for
`Span` and `System.Threading.Tasks.Extensions` for the C# 7.0 generalized-async-return-types row's
`ValueTask`, which `System.Memory` does not bring in on its own.

## VB.NET

### Baseline (VB.NET 1.0 through VB 11 / Visual Studio .NET 2002 – 2013)

Sourced from the Microsoft Learn bucket summaries (coarse, non-itemized) plus the local
`external/vblang/spec/` topic index (topic-complete, not version-attributed) — per the design doc's
verified sourcing strategy. All rows live under one `Baseline/` folder (no per-version attribution;
the "Version" column below is informational provenance only, not a folder split).

| Version (informational) | Feature(s) | Group folder | Target project(s) | Measured floor (evidence) | Excluded from (reason) | Source |
|---|---|---|---|---|---|---|
| Baseline (VS.NET 2002) | Classes, Structures, Standard Modules, Interfaces, Enumerations | `TypesAndDeclarations` | VbFw48, VbLatest | 11 (none) | | vblang/spec/types.md |
| Baseline (VS.NET 2002) | Methods, Constructors, Events, Constants, Properties, Operators, instance/shared variables | `TypeMembers` | VbFw48, VbLatest | 11 (none) | | vblang/spec/type-members.md |
| Baseline (VS.NET 2002) | Inheritance, Implementation, Polymorphism, Accessibility, generic types/methods | `GeneralConcepts` | VbFw48, VbLatest | 11 (none) | | vblang/spec/general-concepts.md |
| Baseline (VS.NET 2002) | If/Select Case/loop control flow | `ControlFlowStatements` | VbFw48, VbLatest | 11 (none) | | vblang/spec/statements.md |
| Baseline (VS.NET 2002) | `With` statement | `WithStatement` | VbFw48, VbLatest | 11 (none) | | vblang/spec/statements.md |
| Baseline (VS.NET 2002) | `Imports`, namespaces, `Option Strict`/`Explicit`/`Compare`/`Infer` | `ImportsAndNamespaces` | VbFw48, VbLatest | 11 (none) | | vblang/spec/source-files-and-namespaces.md |
| Baseline (VS.NET 2002) | Widening/narrowing/user-defined conversions | `Conversions` | VbFw48, VbLatest | 11 (none) | | vblang/spec/conversions.md |
| Baseline (VS.NET 2002) | Attributes | `Attributes` | VbFw48, VbLatest | 11 (none) | | vblang/spec/attributes.md |
| Baseline (VS.NET 2002) | `#Const`, `#If`, `#Region`, `#ExternalSource` | `PreprocessingDirectives` | VbFw48, VbLatest | 11 (none) | | vblang/spec/preprocessing-directives.md |
| Baseline (VS.NET 2002) | Expression grammar, operator precedence | `ExpressionsAndOperators` | VbFw48, VbLatest | 11 (none) | | vblang/spec/expressions.md |
| Baseline (VS.NET 2002) | Structured (`Try`/`Catch`/`Finally`) and unstructured (`On Error`) exception handling | `ErrorHandling` | VbFw48, VbLatest | 11 (none) | | vblang/spec/statements.md |
| Baseline (VS.NET 2002) | Array declaration and basic collection usage | `ArraysAndCollectionsBasics` | VbFw48, VbLatest | 11 (none) | | vblang/spec/types.md |
| Baseline (VS2005) | `My` namespace and helper types | `MyNamespaceHelpers` | VbFw48 | 11 (none) | VbLatest (capability: the SDK passes `_MyType=Empty` for a net10.0 VB class library, so no `My` member exists; setting `MyType=Console` then fails because `Microsoft.VisualBasic.ApplicationServices.*` and `Devices.Computer` are not in the net10.0 shared framework) | Learn whats-new (VS2005 bucket) |
| Baseline (VS2008) | LINQ (query expressions) | `Linq` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | XML literals | `XmlLiterals` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | Local type inference, object initializers | `LocalTypeInferenceAndObjectInitializers` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | Anonymous types | `AnonymousTypes` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | Extension methods | `ExtensionMethods` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | Lambda expressions | `LambdaExpressions` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | `If` operator, partial methods | `IfOperatorAndPartialMethods` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2008) | Nullable value types | `NullableValueTypes` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2008 bucket) |
| Baseline (VS2010) | Auto-implemented properties, collection initializers | `AutoImplementedPropertiesAndCollectionInitializers` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2010 bucket) |
| Baseline (VS2010) | Implicit line continuation | `ImplicitLineContinuation` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2010 bucket) |
| Baseline (VS2010) | `dynamic` consumption | `DynamicBinding` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2010 bucket) |
| Baseline (VS2010) | Generic co/contravariance | `GenericCoContravariance` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2010 bucket) |
| Baseline (VS2010) | Global namespace access | `GlobalNamespaceAccess` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2010 bucket) |
| Baseline (VS2012) | `Async`/`Await`, iterators | `AsyncAwaitAndIterators` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2012 bucket) |
| Baseline (VS2012) | Caller info attributes | `CallerInfoAttributes` | VbFw48, VbLatest | 11 (none) | | Learn whats-new (VS2012 bucket) |

### VB 14 (Visual Studio 2015) onward — itemized per-version deltas

| Version | Feature(s) | Group folder | Target project(s) | Measured floor (evidence) | Excluded from (reason) | Source | Note |
|---|---|---|---|---|---|---|---|
| VB 14 | `NameOf` operator | `NameOfOperator` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | String interpolation | `StringInterpolation` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Null-conditional member access/indexing | `NullConditionalOperators` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Multi-line string literals | `MultiLineStringLiterals` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Comment placement improvements | `CommentPlacementImprovements` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Smarter fully-qualified name resolution | `SmarterNameResolution` | VbFw48, VbLatest | 11 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Year-first date literals | `YearFirstDateLiterals` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Readonly interface properties | `ReadonlyInterfaceProperties` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | `TypeOf ... IsNot ...` | `TypeOfIsNot` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | `#Disable Warning`/`#Enable Warning` | `DisableEnableWarningDirectives` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | XML doc comment improvements | `XmlDocCommentImprovements` | VbFw48, VbLatest | 11 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Partial modules and interfaces | `PartialModulesAndInterfaces` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | `#Region` inside method bodies | `RegionDirectivesInsideMethodBodies` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | `Overrides` implicitly `Overloads` | `OverridesImplicitlyOverloads` | VbFw48, VbLatest | 11 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | `CObj` in attribute arguments | `CObjInAttributeArguments` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 14 | Ambiguous interface method resolution | `AmbiguousInterfaceMethodResolution` | VbFw48, VbLatest | 11 (sdk-pin) | | Learn whats-new#visual-basic-14 | |
| VB 15 | Tuples | `Tuples` | VbFw48, VbLatest | 15 (sdk-pin) | | Learn whats-new#visual-basic-15 | |
| VB 15 | Binary literals, digit separators | `BinaryLiteralsAndDigitSeparators` | VbFw48, VbLatest | 15 (sdk-pin) | | Learn whats-new#visual-basic-15 | |
| VB 15 | Consuming C# reference return values | `ConsumingCSharpRefReturnValues` | VbFw48, VbLatest | 11 (native-ceiling) | | Learn whats-new#visual-basic-15 | The two projects consume different ref-returning subjects. `VbLatest` uses `CollectionsMarshal.GetValueRefOrNullRef`; that type is .NET 5+ with no net48 backport, so `VbFw48` consumes `RefSamples.Find` from `Net48_CSharp8_Library` — this corpus's own C# 7.0 ref-returns row — which suits the row's name at least as well. Both projects carry the same `Span` half. |
| VB 15.3 | Named tuple inference | `NamedTupleInference` | VbFw48, VbLatest | 15.3 (sdk-pin) | | Learn whats-new#visual-basic-153 | |
| VB 15.3 | `-refout`/`-refonly` compiler switches | — | | — | VbFw48, VbLatest (compiler switch, not a source-level construct) | Learn whats-new#visual-basic-153 | |
| VB 15.5 | Non-trailing named arguments | `NonTrailingNamedArguments` | VbFw48, VbLatest | 15.5 (sdk-pin) | | Language-Version-History.md § VB 15.5 | |
| VB 15.5 | `Private Protected` access modifier | `PrivateProtectedAccessModifier` | VbFw48, VbLatest | 15.5 (sdk-pin) | | Language-Version-History.md § VB 15.5 | |
| VB 15.5 | Leading hex/binary/octal digit separator | `LeadingDigitSeparator` | VbFw48, VbLatest | 15.5 (sdk-pin) | | Language-Version-History.md § VB 15.5 | |
| VB 16.0 | Comments allowed in more statement positions | `CommentsInMorePlaces` | VbFw48, VbLatest | 14 (sdk-pin) | | Learn whats-new#visual-basic-160 | |
| VB 16.0 | Optimized floating-point-to-integer conversion | `OptimizedFloatToIntConversion` | VbFw48, VbLatest | 11 (sdk-pin) | | Learn whats-new#visual-basic-160 | |
| VB 16.9 | Consuming init-only properties | `ConsumingInitOnlyProperties` | VbFw48, VbLatest | 16.9 (sdk-pin) | | Learn whats-new#visual-basic-1713 | |
| VB 17.13 | Consuming `CallerArgumentExpression` | `CallerArgumentExpressionConsumption` | VbLatest | 14 (sdk-pin) | VbFw48 (capability: `CallerArgumentExpressionAttribute` is .NET 5+ and has no official net48 backport package, so net48 has no attributed API to consume) | Learn whats-new#visual-basic-1713 | There is no VB 17.0 language version: the compiler rejects both `17` and `17.0` with `BC2014`, and its accepted values step from `16.9` straight to `17.13`. This row is filed at 17.13, matching the section of the source document it is drawn from. |
| VB 17.13 | `unmanaged` constraint recognition | `UnmanagedConstraintRecognition` | VbFw48, VbLatest | 11 (sdk-pin) | | Language-Version-History.md § VB 17.13 | |
| VB 17.13 | Consuming `OverloadResolutionPriorityAttribute` | `OverloadResolutionPriorityConsumption` | VbLatest | 11 (sdk-pin) | VbFw48 (capability: the attribute is .NET 9+ with no net48 backport; *applying* it also needs C# 13, above every net48 C# project's ceiling — CS8400 confirmed via probe — so no net48 assembly here can supply a prioritized API) | Language-Version-History.md § VB 17.13 | |

## Authoring status

A row counts as authored for a project when its group folder exists under that project's version
folder with at least one compilable example file, and that project builds with 0 errors and 0
warnings.

| Project | Authored | Not yet authored |
|---|---|---|
| `Net10_CSharpLatest_Library` | C# 1.0 → 14.0 — 159 group folders under `CSharp1/` … `CSharp14/` — **complete** | — |
| `Net48_CSharp8_Unsafe` | `UnsafeCodeAndPointers` (C# 1.0), `FixedBufferIndexing` and `CustomFixedStatement` (C# 7.3) | — |
| `Net10_CSharpLatest_Unsafe` | all 6 unsafe rows (C# 1.0, 7.3 ×2, 9.0 ×2, 13.0) — **complete** | — |
| `Net10_CSharpLatest_Exe` | `TopLevelStatements` (C# 9.0) | — |
| `CSharpComTypeLib` | support assembly (no feature rows) | — |
| `Net48_CSharp7_3_Library` | C# 1.0 → 7.3 — 75 group folders under `CSharp1/` … `CSharp7_3/` — **complete** | — |
| `Net48_CSharp8_Library` | C# 1.0 → 8.0 — 87 group folders under `CSharp1/` … `CSharp8/` — **complete** | — |
| `VbFw48` | VB baseline → 17.13 — every group folder under `src/Baseline/` … `src/Vb17_13/`, including `MyNamespaceHelpers`, which lives in the `my/` projects and which no net10 project can carry — **complete** | — |
| `VbLatest` | VB baseline → 17.13 — every group folder under `src/Baseline/` … `src/Vb17_13/` — **complete** | — |

A VB family is authored once, under `src/`. Which of its pinned projects compile a given row is the
**Measured floor** column's subject, not this table's; `VbSourceCoverageTests` is what proves no row
under `src/` is compiled by nothing.

The per-`<LangVersion>` C# probe projects listed under **Project codes** are not enumerated here;
their authoring status has not been audited against this manifest.

No row is a placeholder: each names a real, sourced feature, a real group-folder name, and a real
project assignment (or a real, verified exclusion reason).
