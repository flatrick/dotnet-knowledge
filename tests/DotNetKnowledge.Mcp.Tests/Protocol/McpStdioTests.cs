using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Protocol;

[TestClass]
public sealed class McpStdioTests
{
    [TestMethod]
    public async Task ServerListsSourceToolsOverStdio()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(typeof(SourceCatalog).Assembly.Location);
        process.StartInfo.Environment[SourceCache.CacheEnvironmentVariable] = cacheRoot;

        try
        {
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await WriteMessageAsync(process, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tests","version":"1.0"}}}
                """);
            using var initialize = await ReadResponseAsync(process, 1, timeout.Token);
            Assert.IsTrue(initialize.RootElement.TryGetProperty("result", out _));

            await WriteMessageAsync(process, """
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                """);
            await WriteMessageAsync(process, """
                {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
                """);
            using var toolsResponse = await ReadResponseAsync(process, 2, timeout.Token);
            var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
            var names = tools.EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToArray();

            var listSourcesTool = tools.EnumerateArray().Single(tool =>
                tool.GetProperty("name").GetString() == "list_sources");
            StringAssert.Contains(
                listSourcesTool.GetProperty("description").GetString(),
                "start of every MCP session");

            CollectionAssert.Contains(names, "list_sources");
            CollectionAssert.Contains(names, "sync_source");
            CollectionAssert.Contains(names, "lookup_api");
            CollectionAssert.Contains(names, "search_api");
            CollectionAssert.Contains(names, "search_docs");
            CollectionAssert.Contains(names, "get_doc");
            CollectionAssert.Contains(names, "get_doc_outline");
            AssertOptional(tools, "sync_source", "ref");
            AssertOptional(tools, "lookup_api", "source");
            AssertOptional(tools, "search_api", "limit");
            AssertOptional(tools, "search_api", "cursor");
            AssertOptional(tools, "search_docs", "regex");
            AssertOptional(tools, "search_docs", "source");
            AssertOptional(tools, "get_doc", "section");
            AssertOptional(tools, "get_doc_outline", "cursor");
            AssertOptional(tools, "lookup_api", "framework");
            AssertOptional(tools, "search_api", "framework");
            AssertOptional(tools, "search_api_text", "framework");
            AssertOptional(tools, "find_api_references", "framework");

            await WriteMessageAsync(process, """
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_sources","arguments":{}}}
                """);
            using var listResponse = await ReadResponseAsync(process, 3, timeout.Token);
            var listText = listResponse.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
            Assert.IsNotNull(listText);
            using var listResult = JsonDocument.Parse(listText);
            Assert.IsTrue(listResult.RootElement.GetProperty("sources").GetArrayLength() > 0);
            Assert.IsTrue(listResult.RootElement.TryGetProperty("nextStep", out _));
            var source = listResult.RootElement.GetProperty("sources")[0];
            Assert.AreEqual("sync_source", source.GetProperty("nextAction").GetProperty("tool").GetString());
            Assert.AreEqual(
                source.GetProperty("name").GetString(),
                source.GetProperty("nextAction").GetProperty("arguments").GetProperty("name").GetString());

            await WriteMessageAsync(process, """
                {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"sync_source","arguments":{"name":"unknown"}}}
                """);
            using var syncResponse = await ReadResponseAsync(process, 4, timeout.Token);
            var syncText = syncResponse.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
            Assert.IsNotNull(syncText);
            using var syncResult = JsonDocument.Parse(syncText);
            Assert.AreEqual("unknown_source", syncResult.RootElement.GetProperty("error").GetString());

            _ = stderrTask;
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);

            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static async Task WriteMessageAsync(Process process, string message)
    {
        await process.StandardInput.WriteLineAsync(message);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        Process process,
        int id,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            Assert.IsNotNull(line, "The MCP server closed stdout before returning a response.");
            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number
                && responseId.GetInt32() == id)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static void AssertOptional(JsonElement tools, string toolName, string propertyName)
    {
        var tool = tools.EnumerateArray()
            .Single(candidate => string.Equals(
                candidate.GetProperty("name").GetString(),
                toolName,
                StringComparison.Ordinal));
        var schema = tool.GetProperty("inputSchema");
        Assert.IsTrue(
            schema.GetProperty("properties").TryGetProperty(propertyName, out _),
            $"{toolName}.{propertyName} should be declared.");
        if (!schema.TryGetProperty("required", out var required))
            return;

        Assert.IsFalse(
            required.EnumerateArray().Any(property => string.Equals(
                property.GetString(),
                propertyName,
                StringComparison.Ordinal)),
            $"{toolName}.{propertyName} should be optional.");
    }
}
