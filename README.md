# dotnet-knowledge

Version-pinned reference material about C#, VB.NET and Roslyn, for both people and agents.

Two halves:

**A language-feature example corpus.** One worked example for every C# and VB.NET language feature,
at the version that introduced it, across four TFM and project-format combinations — modern SDK-style
`net10.0`, SDK-style `net48`, legacy non-SDK `net48`, and dedicated `/unsafe` projects. 169 C# rows
and 58 VB rows, every project building at 0 errors and 0 warnings. Browse it in
[`examples/language-features/`](examples/language-features/); [`MANIFEST.md`](examples/language-features/MANIFEST.md)
is the index and says exactly which features are absent from which project and why.

**An MCP server.** Serves that corpus plus API and language-design documentation to coding agents,
fetched on demand from upstream Microsoft repositories at commits this repository pins. Every answer
states which revision it came from, so an agent can tell a pinned fact from a moving one.

## For readers

The corpus is plain source files, organized `<project>/<version>/<feature>/`. Nothing needs to be
built or installed to read it. Each example is commented with what the feature does, and — where it
matters — what it does *not* do; several comments record hazards found by running the code rather
than by compiling it.

## For agents

Start at [`AGENTS.md`](AGENTS.md), then [`docs/HANDOFF.md`](docs/HANDOFF.md).

```bash
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
```

```json
{
  "mcpServers": {
    "dotnet-knowledge": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/dotnet-knowledge/src/DotNetKnowledge.Mcp"]
    }
  }
}
```

The example corpus is bundled and needs no setup. Upstream documentation is fetched explicitly —
call `list_sources` to see what is available and where it is cached.

## Status

The corpus is complete. The server is early: `list_sources` works; `sync_source`, the API-doc
lookups, and the example queries are not built yet. See [`docs/HANDOFF.md`](docs/HANDOFF.md).

## Provenance

The corpus and the API-doc query logic were extracted from
[flatrick/dotnet-mcp](https://github.com/flatrick/dotnet-mcp), where they were development aids
carried as git submodules. They are useful independently of that project, which is why they live
here now.
