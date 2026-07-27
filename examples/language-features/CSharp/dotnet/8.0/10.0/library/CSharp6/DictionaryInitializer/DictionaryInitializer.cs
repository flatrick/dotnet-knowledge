using System.Collections.Generic;

namespace Net8_CSharp10.CSharp6.DictionaryInitializer
{
    public class Slots
    {
        private readonly string[] _items = new string[3];

        public string this[int index]
        {
            get { return _items[index]; }
            set { _items[index] = value; }
        }
    }

    public class IndexInitializers
    {
        // The C# 6.0 index initializer assigns through the indexer.
        public static Dictionary<string, int> ByIndexer()
        {
            return new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
        }

        // The C# 3.0 collection initializer calls Add instead. The two are not
        // interchangeable: this form throws on a duplicate key.
        public static Dictionary<string, int> ByAdd()
        {
            return new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };
        }

        // Assignment overwrites, so a repeated key is legal here.
        public static Dictionary<string, int> DuplicateKeyOverwrites()
        {
            return new Dictionary<string, int> { ["k"] = 1, ["k"] = 2 };
        }

        // Nothing about the form is dictionary-specific — any settable indexer
        // works, and the elements need not be contiguous.
        public static Slots CustomIndexer()
        {
            return new Slots { [0] = "a", [2] = "c" };
        }
    }
}
