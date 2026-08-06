# C# has no measured-floor column

`MANIFEST.md`'s VB tables carry a `Measured floor` column: the lowest pin whose project
compiles each row, and which kind of evidence that rests on. The C# tables do not.

## Why it matters

The column is what turns a version claim from an assertion into a probed fact, and it is where
the difference between "the current SDK does not gate this" and "this feature genuinely did not
exist then" is recorded. The C# half of the corpus makes the same version claims with none of
that support.

## Two different quantities

A **placement-derived** floor is the lowest pin whose *project* compiles the row — the VB column's
quantity, derivable there because every VB row is placed at every pin that compiles it.
A **probe-derived** floor is the lowest `/langversion` the compiler accepts the row's source at,
which is what `scripts/verify-feature-floors.cs` is built around.
They must never share a column name.

The C# projects are cumulative ceilings that place a row at and above its own version, so the
placement question answers "its own version" for nearly every row — which is the claim being
checked, not evidence for it.
The net48 tree does hold a few deliberate above-pin rows (`GeneralizedAsyncReturnTypes` in
`CSharp_v6.0`, `ForeachEnhancements` in `CSharp_v1.0`), so the model is not foreign to it.

## What the sweep measured

Every distinct row was compiled at every project pin at or below its own version, against that
pin project's MSBuild-resolved reference set — `verify-feature-floors.cs`'s compile method with
the descent walked all the way down instead of stopping one rung short.
90 distinct rows, 1,022 compiles across the two readings below, about three minutes.
Every row compiles in isolation at its home pin, and acceptance is monotone in the pin for all 90,
so "the lowest pin that compiles this row" is a well-defined single number here.

**Seven of 90 rows compile below the pin that currently houses them.** The other 83 are held to
their own version by the pin one rung down, so per-pin placement would leave them exactly where
they are.

| Row | Filed | Housed at | Placement floor | Floor probe's verdict |
|---|---|---|---|---|
| `Variance` | 2.0 | 2.0 | 1.0 | `UNGATED`, legacy-pin |
| `LockStatement` | 3.0 | 3.0 | 2.0 | `EXEMPT` |
| `CallerInfoAttributes` | 5.0 | 5.0 | 4.0 | `UNPROVEN`, legacy-pin |
| `ForeachLoopVariableScope` | 5.0 | 5.0 | 3.0 | `UNPROVEN`, legacy-pin |
| `GeneralizedAsyncReturnTypes` | 7.0 | 6.0 | 5.0 | `UNGATED`, native-ceiling |
| `AsyncMain` | 7.1 | 7.2 | 5.0 | `UNPROVEN`, sdk-pin |
| `BackingFieldAttributes` | 7.3 | 7.3 | 3.0 | `UNPROVEN`, sdk-pin |

**The placement floor is worth less than it looks for four of those seven.** `Variance` and
`GeneralizedAsyncReturnTypes` have period-compiler evidence that *contradicts* it — a real C# 2.0
compiler rejects `Variance` with `CS0410` and a real C# 6.0 compiler rejects
`GeneralizedAsyncReturnTypes` with `CS1983` — so housing either at its placement floor would encode
in the directory structure a claim the stronger evidence tier already refutes. `LockStatement`'s
floor of 2.0 is set by the generics in the example rather than by `lock`, which shipped in C# 1.0.
`AsyncMain`'s floor of 5.0 is a harness artifact: the probe compiles `/target:library`, so the C#
7.1 entry-point gate never fires — the same "features that live outside the source are invisible"
limitation `scripts/verify-feature-floors.md` records.

## The two readings agree

Each pin was compiled twice: once against that pin project's reference set (placement-derived) and
once against the row's home project's reference set with only `/langversion` varying
(probe-derived).
**They disagree on 1 row of 90, and it is `EmbeddedInteropTypes`** — every C# project from
`CSharp_v4.0` up references `CSharpComTypeLib`, so the row fails at 3.0 and below with `CS0246`
rather than with a language diagnostic. That is a project reference, not a language version, and
the floor probe exempts the row anyway.

So a probe-derived column costs one extra descent inside `verify-feature-floors.cs` and reproduces
the placement reading everywhere it means anything, while the restructure that would make a
placement column derivable moves 7 rows and buys the same numbers.

## Suggested fix

Add a **probe-derived** column to `MANIFEST.md`'s C# tables, populated by extending
`verify-feature-floors.cs` to walk the ladder to its bottom rather than stopping one rung down, and
carrying the same evidence tier the VB column uses. Name it so it cannot be read as the VB column's
placement-derived quantity.

Do not restructure the C# corpus into per-pin placement for this. It would relocate 7 of 90 rows,
2 of them into positions a period compiler contradicts, to obtain numbers a cheaper probe already
produces.

## Related

- [Under-placement is unguarded](under-placement-is-unguarded.md) — the guard that would keep
  such a column honest once it exists
