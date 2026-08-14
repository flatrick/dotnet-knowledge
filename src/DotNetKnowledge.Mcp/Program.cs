using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Features.Docs;
using DotNetKnowledge.Mcp.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// An MCP stdio server owns stdout: it is the protocol channel. Anything written there that is not
// a JSON-RPC message corrupts the session, and the symptom is an unhelpful client-side parse error
// rather than anything pointing back here. Every log therefore goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<SourceCatalog>();
builder.Services.AddSingleton<SourceCache>();
builder.Services.AddSingleton(static _ => new HttpClient(new HttpClientHandler
{
    AllowAutoRedirect = false,
}));
builder.Services.AddSingleton<INuGetPackageClient, NuGetPackageClient>();
builder.Services.AddSingleton<PackageApiCorpusBuilder>();
builder.Services.AddSingleton<ISourceGenerationContributor, ApiPackageGenerationContributor>();
builder.Services.AddSingleton<SourceSynchronizer>();
builder.Services.AddSingleton<ApiDocsQueryService>();
builder.Services.AddSingleton<DocsQueryService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
