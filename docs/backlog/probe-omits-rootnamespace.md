# The probe omits RootNamespace

`scripts/verify-feature-floors.cs` compiles each row's sources in isolation with the project's
resolved reference set, defines, and `Option*` settings. It does not pass `/rootnamespace:`.

The real build does. So the probe compiles a row in a different namespace than the project
that ships it.

## Why it matters

The probe's value rests on reproducing what the project build does closely enough that its
verdict transfers. Every other input was brought into line — reference set, defines, imports,
compiler options — because omitting them made rows fail for reasons unrelated to language
version. `RootNamespace` is the remaining divergence.

## Evidence

No current row is affected. Rows are compiled one group at a time, so a row never references a
sibling row, and no VB sample qualifies a name through its root namespace —
`GlobalNamespaceAccess` uses `Global.System.*`, which is unaffected.

It would matter for a row that referenced another row by fully-qualified name, or that used
the root namespace to disambiguate.

## Suggested fix

Pass `/rootnamespace:` from the project's resolved `RootNamespace` property, alongside the
`Option*` settings the probe already resolves. It is the same MSBuild query that supplies them.

Cheap, and it removes a class of future surprise rather than fixing a present bug.
