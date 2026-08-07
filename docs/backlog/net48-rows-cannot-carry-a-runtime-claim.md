# A net48 row cannot carry a runtime claim, and nothing says so

`AGENTS.md` and `CLAUDE.md` state the rule without qualification: every runtime-behavior claim needs
a `// Runtime verification: <case-id>` marker in the canonical authored source, and that exact case
needs a nonempty `runtimes` array.

`RuntimeClaimCoverageTests.FindCanonicalMarkers` looks for those markers under two roots only:

- `examples/language-features/CSharp/dotnet/10/latest`
- `examples/language-features/VB.NET/dotnet/Net10`

Both net48 trees are outside them. A marker written in a net48 row is not discovered, so the guard
that is supposed to pair a claim with a case never sees the claim at all.

## Why it matters

The failure is silent and points the wrong way. An author who adds a runtime claim to a net48 row
and marks it correctly gets a green suite — not because the case exists, but because the marker was
never read. The rule reads as universal and is enforced over less than half the corpus.

There is a live instance. `Vb15/ConsumingCSharpRefReturnValues` in the net48 family asserts
observable behavior in its comment:

> The reference is real on the C# side — `ReplaceInPlace` assigns through it and the array changes.

Nothing runs it. That claim rests on the build gate, which proves the row compiles and says nothing
about what it does. Its net10 sibling is a different sample against a different subject
(`CollectionsMarshal.GetValueRefOrNullRef`), so it does not cover the net48 claim either.

## Evidence

`FindCanonicalMarkers` builds its roots from `"dotnet", "10", "latest"` and `"dotnet", "Net10"`
(`tests/DotNetKnowledge.Corpus.Tests/RuntimeClaimCoverageTests.cs:131`).

`Runtime verification:` occurs **zero** times under `examples/language-features/CSharp/dotNetFramework/`
and `examples/language-features/VB.NET/dotNetFramework/` — consistent with the guard never having
been able to see one.

The suite holds two `*.case.json` files, `ModernCompilerOldTarget` and `NumericIntPtr`. Neither names
a net48 row.

## Suggested fix

Two parts, and the first is worth doing on its own:

1. **Extend the marker roots to the net48 trees**, so a marker written there is discovered and an
   unpaired claim fails the way the rule says it should. This is where the silence is.
2. **Decide whether a net48 runtime case is runnable at all**, and record the answer. Executing a
   net48 assembly needs a .NET Framework host rather than the private `dotnet` host the suite uses,
   which is a different constraint from the one that made the net48 *build* cases work. If it is not
   runnable, the honest outcome is an explicit exemption naming the reason — the same shape as the
   probe's `ExemptionReason` table — rather than a rule that quietly does not apply.

Until part 2 is settled, the claim in `ConsumingCSharpRefReturnValues` should say what actually backs
it, or the sentence asserting observable mutation should go.
