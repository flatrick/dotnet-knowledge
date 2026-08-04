using System.Text.Json;

namespace DotNetKnowledge.CSharpScriptHost;

internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine(
            JsonSerializer.Serialize(new ScriptFailure(
                "not_implemented",
                nameof(NotImplementedException),
                "Script execution is not implemented.",
                [])));
        return 1;
    }
}
