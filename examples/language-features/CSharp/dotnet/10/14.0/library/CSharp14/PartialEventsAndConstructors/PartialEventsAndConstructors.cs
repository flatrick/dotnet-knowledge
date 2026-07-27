using System;

namespace Net10_CSharp14_Library.CSharp14.PartialEventsAndConstructors
{
    // Partial members have been extended one version at a time: methods in
    // C# 3.0, methods with results in C# 9.0, properties and indexers in
    // C# 13.0, and now events and constructors. A generator can declare the
    // half it owns and leave the body to hand-written code.
    public partial class Publisher
    {
        // Declaring halves.
        public partial event EventHandler Changed;

        public partial Publisher(string name);

        public string Name { get; private set; }
    }

    public partial class Publisher
    {
        private EventHandler _changed;

        // Implementing halves.
        public partial event EventHandler Changed
        {
            add { _changed += value; }
            remove { _changed -= value; }
        }

        public partial Publisher(string name)
        {
            Name = name;
        }

        public void Raise()
        {
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
