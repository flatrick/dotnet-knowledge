using System.Collections.Generic;

namespace Net10_CSharp14_Library.CSharp9.TargetTypedNewExpressions
{
    public class Order
    {
        public int Id { get; set; }

        public Order()
        {
        }

        public Order(int id)
        {
            Id = id;
        }
    }

    public class TargetTypedNew
    {
        // The type comes from the target, so it is written once rather than
        // twice. var solves the same duplication from the other direction, but
        // only when the type is inferable from the right-hand side.
        public static Order Local()
        {
            Order order = new();
            return order;
        }

        // Especially useful where var cannot help: a field, a parameter
        // default, or a long generic type.
        private readonly Dictionary<string, List<int>> _index = new();

        public int IndexCount()
        {
            return _index.Count;
        }

        // As an argument, the parameter type is the target.
        public static int PassAsArgument()
        {
            return Consume(new(7));
        }

        private static int Consume(Order order)
        {
            return order.Id;
        }

        // In a collection initializer, the element type is the target.
        public static List<Order> Elements()
        {
            return new List<Order> { new(), new(1) };
        }
    }
}
