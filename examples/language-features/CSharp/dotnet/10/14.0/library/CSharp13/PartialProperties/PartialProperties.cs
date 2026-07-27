namespace Net10_CSharp14_Library.CSharp13.PartialProperties
{
    // C# 3.0 gave partial methods and C# 9.0 extended them. C# 13.0 brings the
    // same split to properties and indexers, so a generator can declare one
    // half and hand-written code supply the other.
    public partial class Model
    {
        // The declaring half carries no bodies.
        public partial string Name { get; set; }

        public partial int this[int index] { get; }
    }

    public partial class Model
    {
        private string _name = string.Empty;

        private readonly int[] _values = new int[] { 1, 2, 3 };

        // The implementing half supplies the accessors.
        public partial string Name
        {
            get { return _name; }
            set { _name = value ?? string.Empty; }
        }

        public partial int this[int index]
        {
            get { return _values[index]; }
        }
    }
}
