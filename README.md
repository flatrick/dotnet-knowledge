# dotnet-knowledge

Version-pinned reference material about C#, VB.NET and Roslyn, for both people and agents.

**An MCP server.** Serves API and language-design documentation to coding agents, fetched on demand
from upstream Microsoft repositories at commits this repository pins. Every answer states which
revision it came from, so an agent can tell a pinned fact from a moving one.

The bundled language-feature example corpus that used to live here has moved to
[flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples); it has no
dependency on this project and stands on its own.

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

When a server is still running, use `-- reinstall`: it stops every process launched from the
installed shim, then installs. Windows needs that, because a running server holds an exclusive lock
on the shim executable and the plain install fails against it; on Linux and macOS the install works
either way and stopping merely retires a server still answering with the old build.

To build or launch from a checkout instead, without installing:

```bash
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp
```

Upstream documentation is fetched explicitly — call `list_sources` to see what is available and
where it is cached, then `sync_source` before an API lookup.

`roslyn-api-docs` synchronizes two things as one generation: the pinned documentation checkout, and
a pinned NuGet package — `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.3.0 — whose compiler XML and
assembly metadata are normalized into a queryable corpus per target framework. The four API tools
take an optional `framework` (`net472`, `net8.0`, `net9.0`, `net10.0`, defaulting to `net10.0`) to
choose which of the package's surfaces to answer from, and report the one they queried. Where both
halves document the same declaration the repository text wins; every match states whether it came
from Git or from the package, naming the commit or the verified package hash.

Every cataloged package must belong to the Roslyn cohort the checkout's manifest names, so the
catalog cannot yet carry a package versioned independently of Roslyn — the gap is recorded in
[`docs/backlog/`](docs/backlog/README.md).

Fetched sources land in a per-user cache shared by every checkout and client on the machine:
`%LOCALAPPDATA%\dotnet-knowledge\sources` on Windows,
`$XDG_DATA_HOME/dotnet-knowledge/sources` (default `~/.local/share/...`) on Linux, and
`~/Library/Application Support/dotnet-knowledge/sources` on macOS. That is the user *data*
directory rather than the cache directory, deliberately: a synced source must not vanish when a
cache cleaner runs. Set `DOTNET_KNOWLEDGE_CACHE` to put the cache somewhere else.

## Verifying the server

The MCP server's unit and redirected-stdio tests need SDK 10 and Git, with no network access:

```powershell
dotnet test DotNetKnowledge.slnx
```

## Status

The server's source, API-doc, and doc tools are implemented and work under an MCP client:
`list_sources`, `sync_source`, `search_api`, `lookup_api`, `search_api_text`,
`find_api_references`, `search_docs`, `get_doc`, and `get_doc_outline`
all answer correctly over stdio, including a first sync of a large upstream repository.
API answers about Roslyn draw on the pinned `Microsoft.CodeAnalysis.Workspaces.MSBuild` package
beside the documentation checkout, selectable per target framework. The document tools also serve
Microsoft Learn structured-FAQ documents alongside markdown, rendering a `YamlMime:FAQ` file's
sections and questions to a heading tree.

Bundled-example queries remain future work; their intended surface is recorded in
[`docs/design/mcp-tool-surface.md`](docs/design/mcp-tool-surface.md).

## License

MIT — see [`LICENSE`](LICENSE).

Everything in this repository is authored here. The upstream Microsoft repositories this server
draws on are **fetched at runtime** into a per-user cache outside the working tree; none of their
content is vendored, submoduled, or redistributed here, and each stays under its own license in its
own clone. [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) sets out that boundary, and
`dotnet scripts/verify-no-vendored-content.cs` checks the tree against it.

## Provenance

The API-doc query logic was extracted from
[flatrick/dotnet-mcp](https://github.com/flatrick/dotnet-mcp), where it was a development aid
carried as git submodules. It is useful independently of that project, which is why it lives here
now.
