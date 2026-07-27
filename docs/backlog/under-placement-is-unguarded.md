# Under-placement is unguarded

`MANIFEST.md` states the placement invariant in both directions: a project holds **every**
row that compiles at its pin, and a row lives in its family's projects from its measured
floor upward. Only one direction is enforced.

`scripts/verify-feature-floors.cs` reports `MISPLACED` when a project claims a row it cannot
build at its pin. There is no converse check for a row a project *should* claim and does not.

## Why it matters

Delete a `<Compile Include>` for a row placed below its own version — say
`Vb15/ConsumingCSharpRefReturnValues` from `dotnet/Net10/11/library/library.vbproj` — and
every guard stays green:

- the project still builds, because it now compiles strictly less;
- `VbSourceCoverageTests` still passes, because it requires only that *some* project compiles
  each source file;
- the floor probe still passes, because it only classifies rows a project already claims.

Meanwhile that row's `Measured floor` cell in `MANIFEST.md` becomes false, and the corpus
quietly stops demonstrating the thing the cell asserts. The failure is silent in exactly the
way the pinned-project model exists to prevent.

## Evidence

The invariant is stated in `examples/language-features/MANIFEST.md` and in
`docs/superpowers/specs/2026-07-27-vb-langversion-pinned-projects-design.md`. The tree
satisfies it today — placement was derived empirically by building each candidate row at each
pin — but nothing re-checks it.

## Suggested fix

Symmetric with `MISPLACED`: for each project, compile the `src/` rows it does **not** claim at
its pin, and report an `UNDER-PLACED` outcome when one succeeds. That reuses the machinery the
above-pin check already added, and it makes the manifest's floor column self-enforcing.

The cost is one extra compile per (unclaimed row, pin) pair, which is the same order as the
placement sweep that produced the tree.

## Related

- [C# has no measured-floor column](csharp-has-no-measured-floor-column.md)
