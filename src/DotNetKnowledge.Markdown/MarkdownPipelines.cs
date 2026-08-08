using Markdig;

namespace DotNetKnowledge.Markdown;

/// <summary>
/// The one Markdig configuration this library parses with. Every parse site resolves it from here:
/// <see cref="MarkdownOutline"/> and <see cref="MarkdownAtomicBlocks"/> must agree on what the
/// document is, and two builders configured separately is how they stop agreeing.
/// </summary>
/// <remarks>
/// <c>UseYamlFrontMatter</c> is not cosmetic. Without it a Microsoft Learn article's closing
/// <c>---</c> is a setext underline for the metadata paragraph above it, so the front matter
/// becomes a level-2 heading whose text is the whole block — and every section path beneath it
/// inherits that text. Classifying the block does not move any line number: heading positions come
/// from character spans over the normalized text, which are identical either way.
/// </remarks>
internal static class MarkdownPipelines
{
    public static MarkdownPipeline Default { get; } =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseYamlFrontMatter()
            .Build();
}
