namespace Net9_CSharp10_Library.CSharp3.AutoImplementedProperties
{
    public class Customer
    {
        // The compiler generates the backing field. It has an unspeakable name,
        // so nothing outside the property can reach it.
        public string Name { get; set; }

        public int Age { get; set; }

        // An accessor may still be more restrictive than the property.
        public string Id { get; private set; }

        public Customer(string id)
        {
            Id = id;
            Name = string.Empty;
        }
    }

    public static class CustomerFactory
    {
        public static Customer Create(string id, string name, int age)
        {
            Customer customer = new Customer(id);
            customer.Name = name;
            customer.Age = age;
            return customer;
        }
    }
}
