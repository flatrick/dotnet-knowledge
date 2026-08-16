# Backlog

One file per known issue or deferred decision. Each states a current condition, why it
matters, the evidence for it, and a suggested fix — not a history of how it was found.

An item lives here when it is real, understood, and deliberately not fixed yet. Delete the
file when the item is resolved; `git log` is the record.

| Item | Area | Why it is deferred |
|---|---|---|
| [The MCP Tasks extension is not adopted](mcp-tasks-extension-is-not-adopted.md) | server | Client-negotiated and unsupported by the target client; progress notifications cover the need |
| [No client has been observed rendering `sync_source`'s progress notifications](sync-source-progress-is-unverified.md) | server | The server emits them correctly; only the client's rendering is unknown, and it needs a person watching one |
| [Unfiltered document search has no relevance signal](cross-source-search-has-no-relevance-signal.md) | server | The `nuget-docs` tiering works around the worst case; scoring against the query is real design work not yet done |
| [`lookup_api` orders overloads by signature ordinal](lookup-api-orders-overloads-by-signature-ordinal.md) | server | Every field is correct and the ordering is deterministic; what "the useful overload first" ranks on still needs deciding. Best taken together with the dedup item below |
| [`search_api_text`'s dedup key never collapses overloads](search-api-text-dedup-key-never-collapses-overloads.md) | server | The direction is settled — collapse on the visible identity and report a count — but the payload change wants doing deliberately. Best taken together with the ordering item above |
| [Error messages are raw .NET exception text](error-messages-are-raw-dotnet-exception-text.md) | server | The codes are right and the errors are recoverable; authoring a remedy for every forwarded exception is the work |
| [`limit` names three different quantities across one surface](limit-names-three-different-quantities.md) | server | The ranges each encode a real constraint; the cheap fix is documentation and the clean one is a breaking rename |
| [API remarks carry snippet references nothing here can resolve](api-remarks-carry-unresolvable-snippet-references.md) | server | Needs deciding what an unresolvable pointer becomes, and dropping content has to be reported rather than silent |
| [Every document search rescans every file in every source](document-search-rescans-every-file.md) | server | Unmeasured whether the cost is a real problem; an index would need sync-time invalidation |
| [Every API query revalidates the checkout with git](every-api-query-revalidates-the-checkout.md) | server | The validation is what makes an answer trustworthy; which of the three commands costs the second is unmeasured |
| [API package supplements are limited to the Roslyn cohort](api-packages-are-limited-to-the-roslyn-cohort.md) | sources | The cohort check is worth keeping for cohort packages; an opt-out needs deciding together with what a package outside it merges against |
