using System.Linq;
using System.Text.Json;

using (var document = JsonDocument.Parse(System.IO.File.ReadAllText(Args[0])))
{
    var names = document.RootElement.GetProperty("items")
        .EnumerateArray()
        .Where(item => item.GetProperty("enabled").GetBoolean())
        .Select(item => item.GetProperty("name").GetString()!)
        .OrderBy(name => name, System.StringComparer.Ordinal)
        .ToArray();

    System.Console.WriteLine(JsonSerializer.Serialize(new { enabledNames = names }));
}
