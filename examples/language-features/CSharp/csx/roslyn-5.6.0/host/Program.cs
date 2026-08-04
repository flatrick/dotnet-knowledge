using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis.Scripting;

namespace DotNetKnowledge.CSharpScriptHost;

internal static class Program
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            WriteFailure(new ScriptFailure(
                "validation",
                typeof(ArgumentException).FullName!,
                "Exactly one scenario descriptor path is required.",
                []));
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.Token,
            timeout.Token);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var descriptor = ScenarioDescriptorLoader.Load(args[0]);
            var scenarioDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0]))
                ?? throw new InvalidDataException($"Could not determine the scenario directory for {args[0]}.");
            var result = await new ScriptScenarioRunner().RunAsync(
                descriptor,
                scenarioDirectory,
                linkedCancellation.Token);
            Console.Out.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
            return 0;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            WriteFailure(new ScriptFailure(
                "timeout",
                typeof(TimeoutException).FullName!,
                "Script execution timed out after 30 seconds.",
                []));
            return 3;
        }
        catch (OperationCanceledException exception)
        {
            WriteFailure(new ScriptFailure(
                "cancelled",
                TypeName(exception),
                exception.Message,
                []));
            return 2;
        }
        catch (CompilationErrorException exception)
        {
            WriteFailure(new ScriptFailure(
                "compilation",
                TypeName(exception),
                exception.Message,
                exception.Diagnostics
                    .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
                    .ToArray()));
            return 1;
        }
        catch (Exception exception)
        {
            WriteFailure(new ScriptFailure(
                "runtime",
                TypeName(exception),
                exception.Message,
                []));
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string TypeName(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    private static void WriteFailure(ScriptFailure failure) =>
        Console.Error.WriteLine(JsonSerializer.Serialize(failure, SerializerOptions));
}
