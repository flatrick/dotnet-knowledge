namespace Net10_CSharp12_Library.CSharp7_1.InferredTupleElementNames
{
    public class Person
    {
        public string Name { get; set; }

        public int Age { get; set; }
    }

    public class InferredNames
    {
        // The element names are inferred from the variables, so they need not
        // be spelled out the way C# 7.0 required.
        public static string FromLocals(string name, int count)
        {
            var pair = (name, count);
            return pair.name + ":" + pair.count;
        }

        // A property access infers the property's name.
        public static int FromProperties(Person person)
        {
            var projected = (person.Name, person.Age);
            return projected.Age + projected.Name.Length;
        }

        // An explicit name still wins over the inferred one.
        public static int ExplicitWins(int count)
        {
            var pair = (total: count, count);
            return pair.total + pair.count;
        }
    }
}
