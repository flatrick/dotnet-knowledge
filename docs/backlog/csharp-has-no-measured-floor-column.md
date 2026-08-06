# C# has no measured-floor column

`MANIFEST.md`'s VB tables carry a `Measured floor` column: the lowest pin whose project
compiles each row, and which kind of evidence that rests on. The C# tables do not.

## Why it matters

The column is what turns a version claim from an assertion into a probed fact, and it is where
the difference between "the current SDK does not gate this" and "this feature genuinely did not
exist then" is recorded. The C# half of the corpus makes the same version claims with none of
that support.

## Evidence

The asymmetry is substantive rather than an oversight. A C# measured floor would have no
on-disk referent today:

- The VB column is derivable because every VB row was placed at every pin that compiles it, so
  "lowest pin that compiles this row" names a real project.
- The C# projects are cumulative ceilings that place a row at and above its own version, so the
  same question answers "its own version" for nearly every row — which is the claim being
  checked, not evidence for it.

The C# net48 tree does hold a few deliberate above-pin rows (`GeneralizedAsyncReturnTypes` in
`CSharp_v6.0`, `ForeachEnhancements` in `CSharp_v1.0`), so the model is not foreign to it — it
is just not applied systematically.

## Suggested fix

Two steps, in order:

1. Sweep the C# rows the way the VB rows were swept: for each row and each pin at or below its
   own version, determine by building whether the row compiles there.
2. Add the column, populated from that sweep, with the same evidence tier the VB column uses.

Step 1 is the substantive one and is a larger job than the VB equivalent — the C# corpus has
considerably more rows, and its net48 family includes non-SDK projects that need Visual
Studio's `MSBuild.exe`.

## Related

- `scripts/verify-feature-floors.cs`'s `UNDER-PLACED` outcome — the guard that keeps such a column
  honest. It is VB-only today, because a C# project owns its version folders on disk rather than
  selecting them out of a shared tree; adopting per-pin placement for C# is what would give it
  something to check.
