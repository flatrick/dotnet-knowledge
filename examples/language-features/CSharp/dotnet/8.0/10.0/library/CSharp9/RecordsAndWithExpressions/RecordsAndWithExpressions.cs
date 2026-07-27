namespace Net8_CSharp10.CSharp9.RecordsAndWithExpressions
{
    // A record is a reference type whose compiler-generated members give it
    // value semantics: Equals, GetHashCode, ToString, and a copy constructor.
    public record Person(string Name, int Age);

    // The positional form above is shorthand. This is the same idea written out,
    // which is what you reach for when a member needs more than a parameter.
    public record Employee
    {
        public string Name { get; init; }

        public string Department { get; init; }

        public Employee(string name, string department)
        {
            Name = name;
            Department = department;
        }
    }

    // Records participate in inheritance, and equality accounts for the runtime
    // type, so a Manager never equals a plain Employee.
    public record Manager(string Name, int Age, int Reports) : Person(Name, Age);

    public class RecordSamples
    {
        // with produces a copy with the named members replaced. The original is
        // untouched, which is the point of the expression.
        public static Person Older(Person person)
        {
            return person with { Age = person.Age + 1 };
        }

        // Equality is by value, not by reference.
        public static bool EqualByValue()
        {
            Person left = new Person("Ada", 36);
            Person right = new Person("Ada", 36);
            return left == right;
        }

        // ...and the generated ToString prints the members.
        public static string Print()
        {
            return new Person("Ada", 36).ToString();
        }

        // Deconstruction is generated for the positional form.
        public static string Deconstructed()
        {
            Person person = new Person("Ada", 36);
            (string name, int age) = person;
            return name + age;
        }
    }
}
