using System;

namespace Net10_CSharp10_Library.CSharp7.ThrowExpressions
{
    public class ThrowExpressionSamples
    {
        private readonly string _name;

        // throw became usable where an expression is expected, so a null check
        // fuses into the assignment instead of needing its own if statement.
        public ThrowExpressionSamples(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name
        {
            get { return _name; }
        }

        // The second and third operands of ?: are expressions, so either may
        // now be a throw.
        public static string FirstOrThrow(string[] values)
        {
            return values != null && values.Length > 0
                ? values[0]
                : throw new ArgumentException("empty", nameof(values));
        }

        public static int Required(int? value)
        {
            return value ?? throw new InvalidOperationException("missing");
        }

        // An expression-bodied member whose whole body is a throw — the usual
        // way to write a deliberately unimplemented member.
        public string Unsupported() => throw new NotSupportedException();
    }
}
