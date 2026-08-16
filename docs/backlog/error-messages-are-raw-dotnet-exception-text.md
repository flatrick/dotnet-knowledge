# Error messages are raw .NET exception text

Every tool serializes an error by handing the exception's own `Message` to the agent unchanged.
`DocsTool.SerializeArgumentException` maps the exception *type* to an error code and then passes
`exception.Message` through verbatim, and the `RegexParseException`, `NotSupportedException` and
`TimeoutException` handlers beside it do the same.

The error *codes* are deliberate and good. The prose attached to them is whatever .NET happened to
produce.

## Why it matters

The caller is an agent, and an error payload is the only instruction it gets about what to do next.
Two consequences follow from forwarding the framework's text.

`ArgumentException.Message` appends `" (Parameter 'name')"` whenever `ParamName` is set, so every
`invalid_request` ends with plumbing the agent cannot act on — and it duplicates what the message
already said:

```
limit must be between 1 and 100. (Parameter 'limit')
```

Worse, an exception the server did not author carries no remedy at all. A rejected regex answers
with the .NET engine's internal vocabulary:

```
RegexOptions.NonBacktracking is not supported in conjunction with expressions containing:
'backreference (\ number)'.
```

`RegexOptions.NonBacktracking` is an implementation choice recorded nowhere in the tool surface. The
message names it, names the construct, and never says the one thing that helps: rewrite the pattern
without a backreference, or search literally. Compare `path_not_found`, which the server does author
— "Call search_docs, or list_sources for cacheDir." Every message this repository writes ends with
an imperative remedy; every message it forwards does not.

## Evidence

- `Features/Docs/DocsTool.cs:219-222` — `SerializeArgumentException` selects the code from
  `exception.ParamName` and passes `exception.Message` through. `Features/ApiDocs/ApiDocsTool.cs`
  repeats the shape at four call sites.
- `Features/Docs/DocsTool.cs:60-67` — both regex catches serialize `exception.Message` directly.
- Observed against the installed server at `53882bd`: `invalid_request`, `invalid_cursor`,
  `framework_not_available` and `unknown_source` all end in `(Parameter '...')`, including
  `framework_not_available`, whose message is otherwise authored here and already reads well.

## Suggested fix

Separate the sentence from the exception. The throw sites already write complete, well-formed
sentences — the suffix is added by `ArgumentException`, not by this code — so the fix is to stop
reading `.Message` for the payload rather than to rewrite the messages.

Two pieces:

- **Drop the parameter suffix.** Either carry the intended text on a server-owned exception type and
  serialize that, or strip the framework's suffix at the one serialization seam. The latter is
  smaller; the former stops the problem recurring at the next `catch`.
- **Author a remedy for exceptions the server does not raise.** A rejected regex should say what to
  do — drop the backreference or lookbehind, or search without `regex: true`. Keep the engine's
  detail if it is useful, but it belongs after the remedy, not instead of it.

Do not solve this by suppressing the underlying message wholesale: a `git_timeout` or an
`IOException` carries diagnostic detail that has no substitute, and losing it would trade a clumsy
error for an opaque one.
