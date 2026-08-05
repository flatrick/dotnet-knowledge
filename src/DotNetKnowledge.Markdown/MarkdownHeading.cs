namespace DotNetKnowledge.Markdown;

public sealed record MarkdownHeading(int Level, string Text, string Path, int StartLine, int EndLine);
