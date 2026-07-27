using System;
using System.Diagnostics.CodeAnalysis;

namespace Net7_CSharp10.CSharp11.ExtendedNameofScopeInAttributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public sealed class DependsOnAttribute : Attribute
{
    public DependsOnAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public class Extended
{
    // A method's own parameters are now in scope for nameof inside an
    // attribute ON that method. Before C# 11.0 the parameter was not in
    // scope there, so the name had to be a bare string that no rename
    // would follow.
    [DependsOn(nameof(input))]
    public static int Measure(string input)
    {
        return input.Length;
    }

    // The same scope applies to a type parameter.
    [DependsOn(nameof(TValue))]
    public static string Describe<TValue>(TValue value)
    {
        return typeof(TValue).Name + value;
    }

    // The nullable-analysis attributes are the motivating case: they name
    // parameters, and now do it in a rename-safe way.
    public static bool TryParse(string text, [NotNullWhen(true)] out string result)
    {
        result = string.IsNullOrEmpty(text) ? null : text;
        return result != null;
    }
}
