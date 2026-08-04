namespace DotNetKnowledge.CSharpScriptHost;

internal sealed class ScenarioDescriptorValidationException : Exception
{
    public ScenarioDescriptorValidationException(
        string descriptorPath,
        string message,
        Exception? innerException = null)
        : base(
            $"Scenario descriptor is invalid: {descriptorPath}{Environment.NewLine}- {message}",
            innerException)
    {
        DescriptorPath = descriptorPath;
    }

    public string DescriptorPath { get; }
}
