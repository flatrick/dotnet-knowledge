# dotnet-knowledge

Version-pinned reference material about C#, VB.NET and Roslyn, for both people and agents.

Two halves:

**A language-feature example corpus.** One worked example for every C# and VB.NET language feature,
at the version that introduced it, across several TFM and project-format combinations — modern
SDK-style `net10.0`, SDK-style `net48`, legacy non-SDK `net48`, and dedicated `/unsafe` projects,
every project building at 0 errors and 0 warnings. Browse it in
[`examples/language-features/`](examples/language-features/); [`MANIFEST.md`](examples/language-features/MANIFEST.md)
is the index and says exactly which features are absent from which project and why.

**An MCP server.** Serves that corpus plus API and language-design documentation to coding agents,
fetched on demand from upstream Microsoft repositories at commits this repository pins. Every answer
states which revision it came from, so an agent can tell a pinned fact from a moving one.

## For readers

The corpus is plain source files. C# is organized `<project>/<version>/<feature>/`, one source tree
per project. VB.NET keeps one shared source tree per family — `<family>/src/<version>/<feature>/` —
and a project per pinned language version selects rows from it; the pin directories themselves
(`latest/library/`, `11/my/`, …) hold only the project file. Nothing needs to be built or installed
to read it. Each example is commented with what the feature does, and — where it matters — what it
does *not* do; several comments record hazards found by running the code rather than by compiling
it.

## For agents

Start at [`AGENTS.md`](AGENTS.md). For server work, continue with the
[`MCP tool-surface design`](docs/design/mcp-tool-surface.md); known deferred work is indexed in
[`docs/backlog/`](docs/backlog/README.md).

Install the server as a user-global .NET tool. This is machine-global, so one install serves every
client and every checkout:

```bash
dotnet scripts/install-mcp-tool.cs -- install
```

```json
{
  "mcpServers": {
    "dotnet-knowledge": {
      "command": "dotnet-knowledge",
      "args": []
    }
  }
}
```

Re-run the install after changing server code, then restart the MCP connection — a connected client
keeps serving the build it started with. `dotnet scripts/install-mcp-tool.cs` with no arguments
reports which commit the installed tool was built from, and `-- uninstall` removes it.

To build or launch from a checkout instead, without installing:

```bash
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp
```

The example corpus is bundled and needs no setup. Upstream documentation is fetched explicitly —
call `list_sources` to see what is available and where it is cached, then `sync_source` before an
API lookup.

Fetched sources land in a per-user cache shared by every checkout and client on the machine:
`%LOCALAPPDATA%\dotnet-knowledge\sources` on Windows,
`$XDG_DATA_HOME/dotnet-knowledge/sources` (default `~/.local/share/...`) on Linux, and
`~/Library/Application Support/dotnet-knowledge/sources` on macOS. That is the user *data*
directory rather than the cache directory, deliberately: a synced source must not vanish when a
cache cleaner runs. Set `DOTNET_KNOWLEDGE_CACHE` to put the cache somewhere else.

## Verifying the corpus

The corpus suite and the server suite are separate concerns and have separate solutions:
`Corpus.slnx` covers `examples/`, `DotNetKnowledge.slnx` covers `src/`. They require different .NET
hosts, so neither solution is the right way to run the corpus — invoke that suite by project path
through the private host, as below.

The unit-only test subset needs Windows and SDK 10 only; its process tests invoke `cmd`:

```powershell
dotnet test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --filter "TestCategory=Unit"
```

The complete suite requires exact SDK versions 5.0.408, 7.0.410, and 10.0.302, plus runtime bands
5.0, 7.0, and 10.0. Its preflight fails if any requirement is absent. Use `dotnet --list-sdks` and
`dotnet --list-runtimes` to see what a host exposes.

Targeting `net5.0` under SDK 10 selects net5.0 reference APIs; it does not select the SDK 5
compiler. Use `dotnet scripts/install-corpus-test-sdks.cs` to install or check the reusable private
toolchains, then run the [full suite through the private host](scripts/install-corpus-test-sdks.md).

The MCP server's unit and redirected-stdio tests need SDK 10 and Git, with no network access:

```powershell
dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj
```

## Status

The corpus is complete.

The server's source, API-doc, and language-doc tools are implemented and work under an MCP client:
`list_sources`, `sync_source`, `search_api`, `lookup_api`, `search_api_text`,
`find_api_references`, `search_language_docs`, `get_language_doc`, and `get_language_doc_outline`
all answer correctly over stdio, including a first sync of a large upstream repository.

Bundled-example queries remain future work; their intended surface is recorded in
[`docs/design/mcp-tool-surface.md`](docs/design/mcp-tool-surface.md).

## License

MIT — see [`LICENSE`](LICENSE).

Everything in this repository is authored here. The upstream Microsoft repositories this server
draws on are **fetched at runtime** into a per-user cache outside the working tree; none of their
content is vendored, submoduled, or redistributed here, and each stays under its own license in its
own clone. [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) sets out that boundary — including
why the example corpus is original work rather than derived from Microsoft's documentation — and
`dotnet scripts/verify-no-vendored-content.cs` checks the tree against it.

## Provenance

The corpus and the API-doc query logic were extracted from
[flatrick/dotnet-mcp](https://github.com/flatrick/dotnet-mcp), where they were development aids
carried as git submodules. They are useful independently of that project, which is why they live
here now.
