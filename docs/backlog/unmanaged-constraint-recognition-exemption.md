# UnmanagedConstraintRecognition may need an exemption

The `UnmanagedConstraintRecognition` row is filed at VB 17.13 and its measured floor is VB 11.
No VB compiler rejects it at any version.

That is not a defect in the sample. VB cannot express an `unmanaged` constraint at all — the
row's subject is the compiler *recognizing* a constraint a C# assembly emitted. A VB compiler
that predates the feature does not reject such metadata; it ignores it. So there is nothing any
`LangVersion` can gate, and nothing a period compiler can reject.

## Why it matters

The row occupies a category the floor probe cannot speak to, and it currently reports
`UNPROVEN` — which reads as "we could not establish this" rather than "no compiler version
could establish this." Those are different claims, and the second is the true one.

`ExemptionReason` in `scripts/verify-feature-floors.cs` already exists for exactly this
category. `EmbeddedInteropTypes` is the C# precedent: NoPIA is a property of the reference
rather than the source, so no compiler version reveals it.

## Evidence

Compiling the row directly with the cached native VB 14 compiler — a 2016 binary predating the
`unmanaged` constraint entirely — succeeds with exit 0 and no diagnostics.

The row's own comment records that `Span`, the natural subject, is unusable from VB.

## Suggested fix

Add a group exemption for the row, with a reason naming the category: the feature is metadata
recognition, so the source contains nothing any VB compiler can reject.

This is a corpus decision rather than a tooling one — it changes what the corpus claims about
a row, so it belongs with whoever owns the manifest. The two sibling VB 17.13 consumption rows
(`CallerArgumentExpressionConsumption`, `OverloadResolutionPriorityConsumption`) are worth
examining at the same time; they may be the same category.

## Related

- `docs/design/language-feature-showcase-design.md`, probe-exempt table
