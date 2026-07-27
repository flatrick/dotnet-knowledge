using System.Collections.Generic;

namespace Net10_CSharpLatest_Library.CSharp9.ExtensionGetEnumerator
{
    public class Bag
    {
        private readonly List<int> _items = new List<int>();

        public void Add(int value)
        {
            _items.Add(value);
        }

        public List<int> Items
        {
            get { return _items; }
        }
    }

    // foreach now finds a GetEnumerator supplied as an extension method, so a
    // type gains enumerability without being modified or implementing an
    // interface. Before C# 9.0 the method had to be an instance member.
    public static class BagExtensions
    {
        public static List<int>.Enumerator GetEnumerator(this Bag bag)
        {
            return bag.Items.GetEnumerator();
        }
    }

    public class Usage
    {
        // Bag declares no GetEnumerator and implements no interface, yet this
        // compiles.
        public static int Sum(Bag bag)
        {
            int total = 0;
            foreach (int value in bag)
            {
                total += value;
            }

            return total;
        }
    }
}
