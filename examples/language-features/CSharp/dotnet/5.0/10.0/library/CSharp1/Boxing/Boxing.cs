using System;

namespace Net5_CSharp10.CSharp1.Boxing
{
    public class BoxingConversions
    {
        // Boxing: the value is copied into an object on the managed heap.
        public static object Box(int value)
        {
            object boxed = value;
            return boxed;
        }

        // Unboxing: an explicit cast copies the value back out.
        public static int Unbox(object boxed)
        {
            return (int)boxed;
        }

        // Using a value type through an interface boxes it.
        public static string ThroughInterface(int value)
        {
            IFormattable formattable = value;
            return formattable.ToString(null, null);
        }

        // Calling a virtual member that the struct itself does not override boxes it:
        // ToString() is inherited from System.ValueType. The compiler emits a constrained call,
        // but because PlainStruct provides no ToString() override, the ECMA-335 runtime rule for
        // constrained calls boxes the value and dispatches to System.ValueType.ToString().
        public static string BoxedToString(PlainStruct value)
        {
            return value.ToString();
        }
    }

    // Deliberately does not override ToString(), Equals(), or GetHashCode() — see
    // BoxingConversions.BoxedToString above.
    public struct PlainStruct
    {
        private readonly int _number;

        public PlainStruct(int number)
        {
            _number = number;
        }
    }
}
