# MCP Session Source Handshake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Require an MCP caller to begin each connection with `list_sources`, then give it machine-readable instructions for synchronizing only the source it needs.

**Architecture:** Keep the handshake within the existing `list_sources` MCP tool. Extend each serialized source status with a `nextAction` object derived from its existing validated `synced` state, and replace the conditional top-level hint with a stable session rule. No tool implicitly fetches sources and no new MCP tool, prompt, or resource is added.

**Tech Stack:** C# / .NET, ModelContextProtocol SDK, MSTest, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-08-21-mcp-session-source-handshake-design.md`

## Global Constraints

- `list_sources` is read-only and idempotent; only `sync_source` may fetch or write source state.
- The `list_sources` tool description must require it before every other dotnet-knowledge tool call on a new MCP connection.
- An unsynced source returns `nextAction` with tool `sync_source` and its exact `name` argument.
- A synced source returns `nextAction` with `tool: null`; it does not offer a redundant synchronization action.
- The top-level `nextStep` is always present, explains how to use `nextAction`, and states that omitting `ref` chooses the pinned revision while `ref: "head"` accepts drift.
- Query-tool synchronization errors remain unchanged as the race-safe fallback after the snapshot.
- Keep output compact, camel-cased, and machine-parseable. Do not add decorative formatting or implicit behavior.

---

## File Structure

- Modify: `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs` — advertise the session-start requirement and serialize the per-source action from the validated synchronization state.
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs` — direct unit coverage for the unsynced and ready action payloads.
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs` — public MCP coverage that `tools/list` advertises the handshake and `tools/call` returns it over stdio.
- Modify: `docs/design/mcp-tool-surface.md` — make the session-start handshake and `nextAction` response contract part of the server surface.

### Task 1: Publish and verify the session-start handshake

**Files:**

- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs:629-660`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs:47-80`
- Modify: `src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs:21-79,348-365`
- Modify: `docs/design/mcp-tool-surface.md:30-43`

**Interfaces:**

- Consumes: `SourceStatus.Synced`, the existing validated snapshot boolean produced by `GetSourceStatusAsync`.
- Produces: `SourceStatus.NextAction` serialized as `nextAction`, a `SourceNextAction` object whose `tool` is `"sync_source"` with `{ "name": sourceName }` arguments when unsynced, or null when ready.
- Produces: a non-null top-level `ListSourcesResult.NextStep` containing the session rule for both ready and unsynced catalogs.

- [x] **Step 1: Write direct failing assertions for both action states**

  In `ListSourcesReportsValidatedSynchronizationState`, after each source's `synced` assertion, require the returned action to name the source exactly:

  ```csharp
  var nextAction = source.GetProperty("nextAction");
  Assert.AreEqual("sync_source", nextAction.GetProperty("tool").GetString());
  Assert.AreEqual(
      source.GetProperty("name").GetString(),
      nextAction.GetProperty("arguments").GetProperty("name").GetString());
  ```

  Replace the current final `nextStep` check with assertions that it is present and mentions both the action field and pin-preserving default:

  ```csharp
  var nextStep = document.RootElement.GetProperty("nextStep").GetString();
  Assert.IsNotNull(nextStep);
  StringAssert.Contains(nextStep, "nextAction");
  StringAssert.Contains(nextStep, "Omit ref");
  ```

  In the existing synchronized-local-source block in `SyncSourceReturnsCachePathAndProvenance`, after obtaining `status`, add:

  ```csharp
  var nextAction = status.GetProperty("nextAction");
  Assert.AreEqual(JsonValueKind.Null, nextAction.GetProperty("tool").ValueKind);
  Assert.IsFalse(nextAction.TryGetProperty("arguments", out _));
  Assert.IsTrue(statusDocument.RootElement.TryGetProperty("nextStep", out _));
  ```

- [x] **Step 2: Run the source-tool tests to confirm the new contract is absent**

  Run:

  ```bash
  dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo --filter "FullyQualifiedName~SourcesToolTests"
  ```

  Expected: FAIL because `nextAction` is not yet present on the serialized source object; do not change synchronization behavior to satisfy the test.

- [x] **Step 3: Write the public stdio regression assertions**

  In `ServerListsSourceToolsOverStdio`, retain the `tools` JSON array long enough to select the `list_sources` element, then add:

  ```csharp
  var listSourcesTool = tools.EnumerateArray().Single(tool =>
      tool.GetProperty("name").GetString() == "list_sources");
  StringAssert.Contains(
      listSourcesTool.GetProperty("description").GetString(),
      "start of every MCP session");
  ```

  After parsing the `list_sources` call result, assert the public response has both the unconditional top-level instruction and an unsynced action:

  ```csharp
  Assert.IsTrue(listResult.RootElement.TryGetProperty("nextStep", out _));
  var source = listResult.RootElement.GetProperty("sources")[0];
  Assert.AreEqual("sync_source", source.GetProperty("nextAction").GetProperty("tool").GetString());
  Assert.AreEqual(
      source.GetProperty("name").GetString(),
      source.GetProperty("nextAction").GetProperty("arguments").GetProperty("name").GetString());
  ```

- [x] **Step 4: Implement the compact serialized action contract**

  In `SourcesTool.cs`, change the `list_sources` description so its first operational instruction is:

  ```csharp
  "At the start of every MCP session, call this before any other dotnet-knowledge tool " +
  "to see which sources are ready. "
  ```

  In `GetSourceStatusAsync`, calculate the existing source readiness once and pass it to a new final `NextAction` record parameter:

  ```csharp
  var synced = state is not null && supplements.All(supplement => supplement.Synced);
  return new SourceStatus(
      Name: name,
      Repository: definition.Repository,
      Purpose: definition.Purpose,
      Url: definition.Url,
      Pin: definition.Pin,
      HeadBranch: definition.Head,
      Synced: synced,
      CurrentRef: state?.Ref,
      CurrentCommit: state?.Commit,
      FetchedAt: state?.FetchedAt,
      CacheDir: state is null
          ? cache.DirectoryFor(name)
          : cache.RepositoryDirectoryFor(name, state.Generation),
      Supplements: supplements,
      NextAction: synced
          ? new SourceNextAction(Tool: null, Arguments: null)
          : new SourceNextAction("sync_source", new { name }));
  ```

  Define the action record beside the other private response records:

  ```csharp
  private sealed record SourceNextAction(string? Tool, object? Arguments);
  ```

  Change `ListSourcesResult.NextStep` to non-null `string`, remove the `unsynced` list, and always supply this exact rule:

  ```csharp
  "For the source you need, inspect nextAction. Call sync_source only when it names that tool; " +
  "omit ref for the pinned revision, or pass ref: \"head\" only to accept upstream drift."
  ```

  Do not alter `SyncSource`, `SourceSynchronizer`, `SourceCache`, or any query tool.

- [x] **Step 5: Document the externally visible workflow**

  Replace the `list_sources()` entry in `docs/design/mcp-tool-surface.md` with this contract, preserving the source field inventory:

  ```text
  list_sources()
      → first call of every MCP session, before any other dotnet-knowledge
        tool; state can change between sessions
      → per source: name, purpose, pinned commit, currently-synced ref and
        commit, fetchedAt, synced?, cacheDir, nextAction
      → nextAction: sync_source plus { name } when that source is not ready;
        tool: null when it is query-ready
      → nextStep is always present: inspect the chosen source's nextAction;
        omit sync_source ref for the pin, pass "head" only to accept drift
  ```

- [x] **Step 6: Run focused tests and verify the public contract**

  Run:

  ```bash
  dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --nologo --filter "FullyQualifiedName~SourcesToolTests|FullyQualifiedName~McpStdioTests"
  git diff --check
  ```

  Expected: both the direct and stdio tests pass, including the new metadata and JSON assertions; `git diff --check` reports no whitespace errors.

- [x] **Step 7: Run the repository test suite**

  Run:

  ```bash
  dotnet test DotNetKnowledge.slnx --nologo
  ```

  Expected: all tests pass. If a pre-existing failure occurs, record its exact test name and output; do not classify it as caused by this change without reproducing it in the focused suite.

- [x] **Step 8: Review and commit the completed contract**

  Run:

  ```bash
  git diff -- src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs docs/design/mcp-tool-surface.md
  git status --short
  git add src/DotNetKnowledge.Mcp/Features/Sources/SourcesTool.cs tests/DotNetKnowledge.Mcp.Tests/Features/Sources/SourcesToolTests.cs tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs docs/design/mcp-tool-surface.md
  git commit -m "Guide MCP sessions through source synchronization"
  ```

  Expected: the diff contains only the session-start description, response contract, tests, and tool-surface documentation; the commit records the completed feature.

