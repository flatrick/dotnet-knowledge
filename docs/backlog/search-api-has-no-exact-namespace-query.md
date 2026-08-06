# `search_api` cannot ask for one namespace without its descendants

A namespace pattern matches a run of complete segments anywhere in the fully-qualified name, so it
matches every namespace that *begins* with the requested one:

```
search_api("Microsoft.CSharp")
  Microsoft.CSharp.CSharpCodeProvider                  matchedOn: namespace
  Microsoft.CSharp.RuntimeBinder.Binder                matchedOn: namespace
  Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo    matchedOn: namespace
```

There is no way to ask for the types declared in `Microsoft.CSharp` itself.

## Why it matters

The default is right: a caller naming a namespace almost always wants what is under it, and the
alternative — silently excluding sub-namespaces — would be a plausible absence. So this is a missing
capability rather than a wrong answer.

It matters where a namespace root is large and its direct contents are small. `System` returns most
of the BCL; the handful of types actually declared in `System` are unreachable through any pattern,
because every one of them also matches the descendant reading. A caller who wants them has no query
that expresses it.

## Evidence

`ApiDocsQueryService.ClassifyMatch` locates the pattern's segments as a consecutive run inside the
full name's segments and does not record where the run ended relative to the type name. A run ending
one segment before the type name is a direct member of that namespace; a run ending earlier is a
descendant. The information is available at match time and discarded.

## Suggested fix

Either report the distinction or allow filtering on it.

Reporting is cheaper and consistent with how `matchedOn` already works: a `namespaceDepth` or a
distinct `matchedOn` value for a direct member lets the caller filter locally on the page it already
has. Filtering server-side is friendlier but adds a parameter to a tool whose surface is currently
one pattern.

Whichever is chosen, it should not change the default. The descendant reading is what an agent
naming a namespace means nearly every time.
