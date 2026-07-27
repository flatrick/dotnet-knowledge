using System.Diagnostics.CodeAnalysis;

namespace Net10_CSharp13_Library.CSharp11.RequiredMembers
{
    public class Person
    {
        // required forces every construction to set this member, so an object
        // initializer can be mandatory rather than merely available. Before
        // C# 11.0 the only way to require a value was a constructor parameter.
        public required string Name { get; init; }

        public required int Age { get; init; }

        // Not required: optional as usual.
        public string Nickname { get; init; }
    }

    public class Employee
    {
        public required string Name { get; init; }

        // A constructor that does set every required member can say so, which
        // exempts its callers from the initializer requirement.
        [SetsRequiredMembers]
        public Employee(string name)
        {
            Name = name;
        }
    }

    public class Usage
    {
        // Omitting Name or Age here would be a compile error.
        public static Person Create()
        {
            return new Person { Name = "Ada", Age = 36 };
        }

        // No initializer needed, because the constructor is attributed.
        public static Employee Hire()
        {
            return new Employee("Grace");
        }
    }
}
