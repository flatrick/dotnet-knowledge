namespace DotNetKnowledge.Yaml;

/// <summary>
/// A document declared itself a Learn FAQ and then could not be read as one. Distinct from "this
/// file is not a FAQ", which is not an error: a caller that cannot tell those apart reports a
/// broken document as an absent one.
/// </summary>
public sealed class FaqParseException : Exception
{
    public FaqParseException(string message)
        : base(message)
    {
    }

    public FaqParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
