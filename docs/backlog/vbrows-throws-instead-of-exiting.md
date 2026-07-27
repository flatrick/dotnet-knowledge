# VbRows throws instead of exiting

`VbRows` in `scripts/verify-feature-floors.cs` raises `InvalidOperationException` when it meets
a layout it does not expect — a `Compile` glob whose shape is not `<directory>/**/*.vb`, or a
source file at an unexpected depth under `src/`.

The script documents exit codes 0, 1 and 2. A thrown exception produces neither, and prints a
stack trace instead of a diagnostic.

## Why it matters

Failing loudly on a malformed layout is correct and deliberate — a silently skipped row is far
worse, and `VbSourceCoverageTests` throws on the same conditions for the same reason. The
problem is only the shape of the failure: a caller reading the documented exit codes cannot
distinguish this from a crash, and the stack trace buries what is actually a clear, actionable
message.

## Evidence

`scripts/verify-feature-floors.md` documents exit 0 (clean), 1 (findings) and 2 (setup
failure). Neither covers an unexpected layout.

## Suggested fix

Catch it at the top level and exit 2 with the message the exception already carries — the same
treatment the other setup failures get. The layout being wrong is a setup problem, not a
finding about the corpus.
