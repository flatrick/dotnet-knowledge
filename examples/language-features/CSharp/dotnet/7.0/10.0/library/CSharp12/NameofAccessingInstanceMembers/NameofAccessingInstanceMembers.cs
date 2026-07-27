namespace Net7_CSharp10.CSharp12.NameofAccessingInstanceMembers;

public class Person
{
    public string Name { get; set; }

    public Address Home { get; set; }
}

public class Address
{
    public string City { get; set; }
}

public class NameofSamples
{
    // nameof may now reach an instance member through a type name in a
    // static context. Before C# 12.0 this required an instance, so a
    // static member could not name an instance property this way.
    public static string InstanceMemberFromStatic()
    {
        return nameof(Person.Home.City);
    }

    // Only the final identifier is produced, as always.
    public static bool ProducesLastSegment()
    {
        return nameof(Person.Home.City) == "City";
    }

    public static string DirectMember()
    {
        return nameof(Person.Name);
    }
}
