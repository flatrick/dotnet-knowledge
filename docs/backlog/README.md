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
| [Every document search rescans every file in every source](document-search-rescans-every-file.md) | server | Unmeasured whether the cost is a real problem; an index would need sync-time invalidation |
| [Every API query revalidates the checkout with git](every-api-query-revalidates-the-checkout.md) | server | The validation is what makes an answer trustworthy; which of the three commands costs the second is unmeasured |
| [API package supplements are limited to the Roslyn cohort](api-packages-are-limited-to-the-roslyn-cohort.md) | sources | The cohort check is worth keeping for cohort packages; an opt-out needs deciding together with what a package outside it merges against |
