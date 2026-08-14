# One undecodable member fails the whole package

`MetadataApiReader` raises `InvalidDataException` from the middle of its walk whenever it meets a
shape it does not model, and nothing catches it per member or per type. The exception unwinds
`ReadTypes`, so the corpus build fails, so `sync_source` fails, so the source is unsynchronized —
and the coverage lost is the whole package, not the one declaration that could not be read.

## Why it matters

The reader was validated against a repository-authored fixture, which is built to its assumptions
rather than against them. Every assumption it encodes that real assemblies violate is therefore
found only when a real package is cataloged, and is found as total failure rather than as a gap.

Five such shapes have already been fixed. The set that remains is not small, and it is not closing.

## Evidence

At 5.6.0, three of the four core Roslyn packages failed to read at all, each for a different reason:
an external enum as an attribute argument (`[EditorBrowsable]`), an `in` operand on an operator, and
a `NullableAttribute` position for a value type. Two more surfaced immediately behind them: a public
static `op_Implicit` without `SpecialName` on `SyntaxList<T>`, and `[AsyncStateMachine]` naming a
generated type whose name is not expressible in C#. All five are legal, all five ship upstream, and
each one alone cost an entire package.

With all five fixed, `scripts/probes/sweep-api-reader.cs` over one machine's NuGet cache — 1353
assemblies across 184 packages — still refuses one assembly in five:

| | |
|---|---|
| Assemblies read | 1072 |
| Assemblies refused | 281 (20.8%) |
| Packages with at least one refusal | 42 of 184 |
| **First-party packages refused** | **27 of 144 (19%)** |

The first-party rate is the one that matters, because the server only ever reads packages an
operator catalogs — and it is no better than the overall rate. Those 27 include `microsoft.build`,
`system.collections.immutable`, `system.memory`, `system.objectmodel`,
`system.threading.tasks.dataflow` and most of `microsoft.extensions.*`: not exotic packages, but the
obvious candidates for the next `apiPackages` entry.

The reasons are also mostly new ones, so fixing the five did not approach the end of the list:

| Count | Reason |
|---|---|
| 174 | The accessors for a property have incompatible modifiers |
| 63 | A serialized `System.Type` argument uses an unsupported compound type form |
| 38 | An external enum's underlying type cannot be determined |
| 4 | An eight-element `ValueTuple` has a non-tuple rest element |
| 1 | An accessor's staticness disagrees with its property |
| 1 | A required signature modifier on a constraint is missing |

Two of these are instructive. The external-enum count is *after* the framework enums were tabulated,
which is what a table buys: it fixed the framework cases and left every third-party and cross-package
enum failing. And the compound-`System.Type` count is what remains after `[AsyncStateMachine]` and
`[IteratorStateMachine]` were skipped, so the same shape arrives through attributes that cannot be
skipped as implementation detail.

The failure is at least visible now: `InvalidDataException` reaches the caller as a structured
`sync_failed` carrying its message, rather than as an unhandled crash.

## Suggested fix

Stop fixing shapes one at a time — at a 19% first-party refusal rate with the reasons still
turning over, enumerating them in advance is not a strategy that terminates. Decide instead what an
undecodable declaration should cost. Failing the package is defensible for a corrupt archive and
clearly wrong for one member using a shape the reader has not met yet.

The fix is to skip the declaration and report it, which needs a field:
`docs/design/mcp-tool-surface.md` forbids silent truncation, and a member that quietly vanished is
indistinguishable from one that was never there. That means a corpus schema change — a per-corpus
list of skipped declarations with the reason, surfaced through the API tools' coverage the way
`isPartial` already is — and the schema bump forces a resynchronization, which is why it is a
decision rather than a patch.

The individual shapes are still worth fixing afterwards, but as improvements to coverage rather than
as the thing standing between a package and being cataloged at all.

Until then, catalog a new package only after running `scripts/probes/probe-api-package-supplement.cs`
against it, which turns a sync failure into a five-second check.
`scripts/probes/sweep-api-reader.cs` re-measures the rate.
