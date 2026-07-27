namespace Net10_CSharpLatest_Library.CSharp13.ImplicitIndexerInObjectInitializers
{
    public class Buffer
    {
        private readonly int[] _items = new int[4];

        public int Length
        {
            get { return _items.Length; }
        }

        public int this[int index]
        {
            get { return _items[index]; }
            set { _items[index] = value; }
        }
    }

    public class Holder
    {
        public Buffer Values { get; } = new Buffer();
    }

    public class Initializers
    {
        // The ^ index-from-end operator may now appear in an object
        // initializer's indexer position. Before C# 13.0 the implicit receiver
        // was not in scope for it, so the last element had to be assigned after
        // construction.
        public static Holder FromEnd()
        {
            return new Holder
            {
                Values =
                {
                    [0] = 1,
                    [^1] = 9,
                },
            };
        }

        public static int LastValue()
        {
            return FromEnd().Values[3];
        }
    }
}
