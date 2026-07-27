namespace Net9_CSharp10_Library.CSharp10.ExtendedPropertyPatterns
{
    public class Address
    {
        public string City { get; set; }

        public string Country { get; set; }
    }

    public class Person
    {
        public string Name { get; set; }

        public Address Address { get; set; }
    }

    public class ExtendedPatterns
    {
        // C# 10.0 allows a dotted path inside a property pattern, so nesting a
        // pattern per level is no longer required.
        public static bool IsLocal(Person person)
        {
            return person is { Address.Country: "SE" };
        }

        // The C# 8.0 form, kept as contrast: one brace level per member.
        public static bool IsLocalNested(Person person)
        {
            return person is { Address: { Country: "SE" } };
        }

        // The dotted form composes with the other pattern kinds.
        public static string Describe(Person person) => person switch
        {
            { Address.City: "Stockholm", Address.Country: "SE" } => "capital",
            { Address.Country: "SE" } => "domestic",
            { Address: null } => "unknown",
            _ => "foreign",
        };
    }
}
