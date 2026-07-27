# Probe detail strings are nondeterministic

`dotnet scripts/verify-feature-floors.cs -- --json` does not produce byte-identical output
across runs of unchanged code. The `Detail` string for a row can differ between runs.

`Outcome` and `Evidence` are stable. Only `Detail` varies, and it currently reaches one row.

## Why it matters

Diffing two `--json` runs is not a valid regression technique, which is the obvious way to
check that a change to the script left classification alone. A reviewer who diffs two runs and
sees a difference cannot tell a real regression from this.

`Detail` also carries the first diagnostic, and the environment-error check reads that
diagnostic to decide whether a rejection is a language gate or a broken toolchain. Today every
diagnostic that rotates is a genuine language rejection, so the branch never flips — but the
nondeterministic value does feed a classification decision.

## Evidence

`ConsumingCSharpRefReturnValues` has produced `BC30643`, `BC30657` and `BC36954` across runs on
the same machine with no edits, all of them genuine rejections of the same row by the native
VB 14 compiler.

Root cause: `Microsoft.Net.Compilers` 1.3.2's `vbc` binds method bodies concurrently, so which
of several real errors the probe reads first varies per process.

## Suggested fix

Pass `/parallel-` on the probe response file. That serializes binding and makes the first
diagnostic deterministic.

The reason it has not been done: the response file is shared by every compile in both
languages, so the flag changes every probe rather than the one row that needs it. Measure the
runtime cost before adopting it.

## Related

- `scripts/verify-feature-floors.md`, Limitations
