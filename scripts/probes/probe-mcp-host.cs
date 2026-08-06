#!/usr/bin/env dotnet
#:property PublishAot=false
#:property NoWarn=MCPEXP001%3BMCPEXP002
#:package ModelContextProtocol@2.0.0
#:package ModelContextProtocol.Extensions.Tasks@2.0.0
#:package Microsoft.Extensions.Hosting@10.0.8

// A throwaway MCP stdio server that answers the two questions blocking the server's design, both of
// which are properties of the host rather than of this repository's code:
//
//   1. Does the MCP client negotiate the io.modelcontextprotocol/tasks extension? The Tasks
//      extension only engages when the client declares it, so whether `sync_source` can become a
//      long-running task is the client's decision, not ours. `probe_host` dumps the raw request so
//      the declaration is visible if it is there at all.
//      `probe_task_required` settles the same question without interpretation: it runs in Required
//      mode, so a client that declares the extension gets a task handle and one that does not gets
//      JSON-RPC error -32021. Only that tool is Required; the rest stay synchronous so this probe
//      keeps working against a client with no Tasks support at all.
//   2. Why does every git subprocess hang under a stdio host? `probe_process` runs one command with
//      a hard timeout and reports the child's process id without necessarily killing it, so an idle
//      child can be inspected from outside while it is still stuck.
//   3. Does the MCP client surface progress notifications at all, and in what order? `sync_source`
//      reports five stages, and progress is the only thing separating a slow first clone from a hung
//      one — but a client that sends no progress token gets a no-op reporter and the server cannot
//      tell. `probe_progress` sends a known sequence with a delay between steps and returns that
//      exact sequence, so what the client rendered can be diffed against what was sent.
//
// It is deliberately separate from src/DotNetKnowledge.Mcp: the fault is environmental, and a probe
// that shares none of the real server's state cannot be accused of carrying the fault with it.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the protocol channel here exactly as it is in the real server.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithTasks(new InMemoryMcpTaskStore(), options =>
    {
        // Only probe_task_required demands the extension. Making every tool Required would make
        // this probe unusable against exactly the clients it is meant to interrogate.
        options.ExecutionModeSelector = context =>
            context.Params?.Name == "probe_task_required"
                ? McpTaskExecutionMode.Required
                : McpTaskExecutionMode.Synchronous;
    });

await builder.Build().RunAsync().ConfigureAwait(false);

[McpServerToolType]
public static class HostProbe
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "probe_host", ReadOnly = true, Idempotent = true)]
    [Description(
        "Report what this MCP client negotiated and how the server process's standard streams are " +
        "wired. Look in rawRequest for an io.modelcontextprotocol/tasks declaration.")]
    public static string ProbeHost(RequestContext<CallToolRequestParams> context)
    {
        var messageContext = context.JsonRpcRequest.Context;
        return JsonSerializer.Serialize(
            new
            {
                protocolVersion = messageContext?.ProtocolVersion,
                clientInfo = messageContext?.ClientInfo,
                clientCapabilities = messageContext?.ClientCapabilities,

                // The SDK decides whether to create a task from what arrives on the request, so
                // dumping the request verbatim sees whatever the SDK would have seen.
                rawRequest = new
                {
                    method = context.JsonRpcRequest.Method,
                    parameters = context.JsonRpcRequest.Params,
                },

                process = new
                {
                    processId = Environment.ProcessId,
                    stdinRedirected = Console.IsInputRedirected,
                    stdoutRedirected = Console.IsOutputRedirected,
                    stderrRedirected = Console.IsErrorRedirected,
                    workingDirectory = Environment.CurrentDirectory,
                    commandLine = Environment.CommandLine,
                },
            },
            WriteOptions);
    }

    [McpServerTool(Name = "probe_process", ReadOnly = true)]
    [Description(
        "Run one command with a hard timeout and report whether it completed. Defaults to " +
        "`git rev-parse HEAD`. Set command to `cmd` with arguments `/c echo hello` to test whether " +
        "any subprocess hangs or only git. stdinMode is `inherit` (the child inherits this " +
        "server's stdin), `redirect-close` (own pipe, closed at once) or `redirect-open` (own pipe, " +
        "left open). Set killOnTimeout false to leave a hung child alive for external inspection.")]
    public static async Task<string> ProbeProcess(
        string command = null,
        string arguments = null,
        string workingDirectory = null,
        int timeoutSeconds = 15,
        string stdinMode = "inherit",
        bool killOnTimeout = true)
    {
        var redirectStdin = stdinMode is "redirect-close" or "redirect-open";
        var startInfo = new ProcessStartInfo
        {
            FileName = command ?? "git",
            Arguments = arguments ?? "rev-parse HEAD",
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(
                new { started = false, error = exception.Message }, WriteOptions);
        }

        // Captured before any kill, because the whole point of killOnTimeout: false is to hand this
        // id to a wait-chain or native-stack tool while the child is still blocked.
        var childProcessId = process.Id;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (stdinMode == "redirect-close")
            process.StandardInput.Close();

        var timedOut = false;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }
        }

        stopwatch.Stop();

        // Only read the pipes once the child is gone. A hung child leaves these tasks pending
        // forever, and awaiting them here would hang the probe in exactly the way it exists to
        // report on.
        var stdout = timedOut ? null : await stdoutTask.ConfigureAwait(false);
        var stderr = timedOut ? null : await stderrTask.ConfigureAwait(false);

        var killed = false;
        if (timedOut && killOnTimeout && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            killed = true;
        }

        return JsonSerializer.Serialize(
            new
            {
                started = true,
                command = startInfo.FileName,
                arguments = startInfo.Arguments,
                workingDirectory = startInfo.WorkingDirectory,
                stdinMode,
                childProcessId,
                timedOut,
                killed,
                stillRunning = timedOut && !killed,
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                exitCode = timedOut ? (int?)null : process.ExitCode,
                stdout,
                stderr,
            },
            WriteOptions);
    }

    [McpServerTool(Name = "probe_task_required", ReadOnly = true)]
    [Description(
        "Requires the io.modelcontextprotocol/tasks client extension. Succeeds only if this client " +
        "declares it; otherwise the client sees JSON-RPC error -32021 naming the missing capability.")]
    public static async Task<string> ProbeTaskRequired(int seconds = 2)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        return $"This client declares io.modelcontextprotocol/tasks. Ran {seconds}s as a task.";
    }

    [McpServerTool(Name = "probe_progress", ReadOnly = true)]
    [Description(
        "Send a known number of progress notifications, one every delayMilliseconds, then return " +
        "the exact sequence that was sent. Diff what this client rendered against sentNotifications: " +
        "nothing rendered means the client dropped them, and a different order means it reordered " +
        "them. progressToken is null when the client asked for no progress at all, in which case " +
        "the SDK hands the tool a no-op reporter — reporterType names the object that received the " +
        "reports, so a silent client can be told from a silent server.")]
    public static async Task<string> ProbeProgress(
        IProgress<ProgressNotificationValue> progress,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken,
        int steps = 5,
        int delayMilliseconds = 800)
    {
        var stopwatch = Stopwatch.StartNew();
        var sent = new List<object>();
        var cancelled = false;
        try
        {
            for (var step = 1; step <= steps; step++)
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                var message = $"probe step {step} of {steps}";
                progress.Report(new ProgressNotificationValue
                {
                    Progress = step,
                    Total = steps,
                    Message = message,
                });
                sent.Add(new
                {
                    progress = step,
                    total = steps,
                    message,
                    atMilliseconds = stopwatch.ElapsedMilliseconds,
                });
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        return JsonSerializer.Serialize(
            new
            {
                requestedSteps = steps,
                delayMilliseconds,
                progressToken = context.Params?.ProgressToken?.ToString(),
                reporterType = progress.GetType().FullName,
                cancelled,
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                sentNotifications = sent,
            },
            WriteOptions);
    }

    [McpServerTool(Name = "probe_sleep", ReadOnly = true)]
    [Description(
        "Do nothing for the given number of seconds. Shows what this client does with a tool call " +
        "that outlives its patience: whether it cancels, times out, or converts the call to a task.")]
    public static async Task<string> ProbeSleep(
        CancellationToken cancellationToken,
        int seconds = 30)
    {
        var stopwatch = Stopwatch.StartNew();
        var cancelled = false;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        return JsonSerializer.Serialize(
            new
            {
                requestedSeconds = seconds,
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                cancelled,
            },
            WriteOptions);
    }
}
