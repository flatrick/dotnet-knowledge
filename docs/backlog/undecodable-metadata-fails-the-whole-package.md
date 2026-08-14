# One undecodable member fails the whole package

`MetadataApiReader` raises `InvalidDataException` from the middle of its walk whenever it meets a
shape it does not model, and nothing catches it per member or per type. The exception unwinds
`ReadTypes`, so the corpus build fails, so `sync_source` fails, so the source is unsynchronized —
and the coverage lost is the whole package, not the one declaration that could not be read.

## Why it matters

The reader was validated against a repository-authored fixture, which is built to its assumptions
rather than against them. Every assumption it encodes that real assemblies violate is therefore
found only when a real package is cataloged, and is found as total failure rather than as a gap.

Five such shapes have already been fixed. The concerning part is not any of them individually — it
is that each was discovered the same way, by pointing the reader at a Microsoft assembly and
watching it refuse the whole thing, and there is no reason to think the list is complete.

## Evidence

At 5.6.0, three of the four core Roslyn packages failed to read at all, each for a different reason:
an external enum as an attribute argument (`[EditorBrowsable]`), an `in` operand on an operator, and
a `NullableAttribute` position for a value type. Two more surfaced immediately behind them: a public
static `op_Implicit` without `SpecialName` on `SyntaxList<T>`, and `[AsyncStateMachine]` naming a
generated type whose name is not expressible in C#.

All five are legal, all five ship upstream, and each one alone cost an entire package.

The failure is at least visible now: `InvalidDataException` reaches the caller as a structured
`sync_failed` carrying its message, rather than as an unhandled crash.

## Suggested fix

Decide what an undecodable declaration should cost. Failing the package is defensible for a
corrupt archive and clearly wrong for one member using a shape the reader has not met yet.

The alternative is to skip the declaration and report it — which needs a field, because
`docs/design/mcp-tool-surface.md` forbids silent truncation and a member that quietly vanished is
indistinguishable from one that was never there. That means a corpus schema change: a per-corpus
list of skipped declarations with the reason, surfaced through the API tools' coverage the way
`isPartial` already is.

Until then, catalog a new package only after running `scripts/probes/probe-api-package-supplement.cs`
against it, which is what turns this from a sync failure into a five-second check.
