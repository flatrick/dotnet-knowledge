# Third-party content

**This repository redistributes no third-party content.** Every tracked file is authored here and
covered by [`LICENSE`](LICENSE) (MIT). There is no vendored source, no submodule, and no copied
specification, documentation, or sample from any upstream project.

That is a claim about the current tree, and it is mechanically checked:

```bash
dotnet scripts/verify-no-vendored-content.cs             # the tracked tree
dotnet scripts/verify-no-vendored-content.cs -- --history # every blob in every commit
dotnet scripts/verify-no-vendored-content.cs -- --json    # machine-readable
```

`--history` matters because deleting a file does not remove it from the repository — a clone still
carries the blob, and a published history is as much a distribution as a release is. A clean working
tree is therefore not by itself evidence of a clean clone.

**History checked clean** at the time of writing, across every commit: no third-party copyright or
license headers, no upstream document shapes, no path that ever sat under a fetch-cache or upstream
directory, and no submodule at any point.

## Why this file exists even though there is nothing to notice

The MIT grant in `LICENSE` says the whole tree is ours to license. That statement is true today and
would become false the moment somebody else's content landed beside it — without anyone intending a
licensing decision. The realistic ways it happens are mundane:

- pointing the fetch cache at the working tree for debugging and then committing it,
- re-adding one of the upstream repositories as a submodule, out of habit from `dotnet-mcp`,
- pasting a paragraph of specification or a documentation sample into a doc "for convenience".

None of those look like a licensing event at the time, and none announce themselves in review of a
large diff. The guard exists so the tree is checked rather than trusted.

## The upstream sources are fetched, never copied

`sources.json` pins a commit for each upstream repository. The MCP server clones them **at runtime**
into a per-user cache outside any working tree — `%LOCALAPPDATA%\dotnet-knowledge\sources` on
Windows, the XDG cache directory elsewhere.

| Source | Upstream repository |
|---|---|
| `csharplang` | `dotnet/csharplang` |
| `vblang` | `dotnet/vblang` |
| `roslyn-api-docs` | `dotnet/roslyn-api-docs` |
| `dotnet-api-docs` | `dotnet/dotnet-api-docs` |
| `roslyn-wiki` | `dotnet/roslyn` |

Each remains under its own upstream license, in its own clone, on the machine that fetched it. This
repository records a commit hash and a URL — it does not carry their content, so it makes no
sublicensing claim about any of it and ships no copy for a notice to attach to.

**Do not assume a license for these from the family they belong to.** Microsoft's *code*
repositories and their *documentation* repositories are not licensed alike — documentation content
is commonly under a Creative Commons license rather than MIT, which matters most for the two
`*-api-docs` entries. If content from any of them is ever proposed for vendoring, read that
repository's `LICENSE` at the pinned commit and record what it actually says. Do not copy the table
above into a claim about licenses; it deliberately does not make one.

## The example corpus is original work

`examples/language-features/` is the one part of this repository that could be mistaken for derived
content, so it is worth being explicit.

**What comes from Microsoft's documents:** the *checklist*. Which features exist, which language
version introduced each one, and what each is called — taken from `Language-Version-History.md` in
`dotnet/csharplang` and the VB.NET what's-new pages. `MANIFEST.md` cites the section each row came
from, so the sourcing is auditable rather than implied.

**What does not:** the code. Every example is written here to demonstrate the feature named in its
row. None is copied or adapted from an upstream sample, a specification listing, or a documentation
page. Several carry comments recording hazards found by *running* the code — findings that exist
nowhere upstream because they were produced here.

A feature's name and the version that shipped it are facts. The examples that demonstrate them are
this project's expression, and are MIT-licensed along with everything else in the tree.

## If third-party content is ever added

The order matters — the notice goes in with the content, not after it:

1. Read the upstream `LICENSE` **at the commit being taken from**, and confirm the license actually
   covers the specific files. A repository-wide license does not always cover every subdirectory.
2. Record it in this file: what was taken, from which repository and commit, under which license,
   and where it now lives in this tree.
3. Reproduce the required attribution and license text wherever that license requires it.
4. Add an entry to the exemption list in `scripts/verify-no-vendored-content.cs`, with a comment
   naming the reason — so the guard keeps failing on everything that has *not* been through steps 1
   to 3.

Never silence a finding by deleting or loosening a rule. The finding is the mechanism working; the
exemption list is where a reviewed, documented exception is recorded, and every entry in it is a
file nobody is checking any more.

This file records the project's policy and the factual state of the tree. It is not legal advice.
