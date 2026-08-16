using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotNetKnowledge.Yaml;

public sealed record FaqQuestion(string Question, string Answer);

public sealed record FaqSection(string Name, IReadOnlyList<FaqQuestion> Questions);

/// <summary>
/// A Microsoft Learn structured-FAQ document as plain data. Input is YAML text; output carries no
/// YamlDotNet type, so the dependency stops at this assembly's boundary.
/// </summary>
public sealed record FaqDocument(string? Title, string? Summary, IReadOnlyList<FaqSection> Sections)
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static FaqDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        FaqYaml? raw;
        try
        {
            raw = Deserializer.Deserialize<FaqYaml>(text);
        }
        catch (YamlException exception)
        {
            throw new FaqParseException(
                $"The document declares YamlMime:{LearnYamlMime.Faq} but is not valid YAML: {exception.Message}",
                exception);
        }

        // IgnoreUnmatchedProperties is what lets the metadata block pass without a model for every
        // Learn key. Its cost is that a wholly unrecognized document deserializes to all-null
        // properties and reports success, so the absence of sections has to be rejected here.
        // Learn's schema requires them, which is what makes this unambiguous.
        if (raw?.Sections is not { Count: > 0 })
        {
            throw new FaqParseException(
                $"The document declares YamlMime:{LearnYamlMime.Faq} but has no sections.");
        }

        var sections = new List<FaqSection>(raw.Sections.Count);
        foreach (var section in raw.Sections)
        {
            var name = section.Name ?? string.Empty;
            if (section.Questions is not { Count: > 0 })
                throw new FaqParseException($"FAQ section '{name}' has no questions.");

            sections.Add(new FaqSection(
                name,
                section.Questions
                    .Select(question => new FaqQuestion(question.Question ?? string.Empty, question.Answer ?? string.Empty))
                    .ToArray()));
        }

        return new FaqDocument(raw.Title, raw.Summary, sections);
    }

    // The wire shape, private so no caller depends on the deserializer's mutable model.
    private sealed class FaqYaml
    {
        public string? Title { get; set; }

        public string? Summary { get; set; }

        public List<SectionYaml>? Sections { get; set; }
    }

    private sealed class SectionYaml
    {
        public string? Name { get; set; }

        public List<QuestionYaml>? Questions { get; set; }
    }

    private sealed class QuestionYaml
    {
        public string? Question { get; set; }

        public string? Answer { get; set; }
    }
}
