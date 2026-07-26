using System;

namespace CSharpNet10Latest.CSharp11.CheckedUserDefinedOperators
{
    // A user-defined operator may now come in a checked variant, so a type can
    // overflow-check the way the built-in numeric types do. The checked form is
    // selected inside a checked context; otherwise the unchecked one is used.
    public readonly struct Cents
    {
        public Cents(int value)
        {
            Value = value;
        }

        public int Value { get; }

        // Wraps, like unchecked arithmetic on int.
        public static Cents operator +(Cents left, Cents right)
        {
            return new Cents(unchecked(left.Value + right.Value));
        }

        // Throws on overflow, like checked arithmetic on int.
        public static Cents operator checked +(Cents left, Cents right)
        {
            return new Cents(checked(left.Value + right.Value));
        }
    }

    public class Usage
    {
        public static int Unchecked()
        {
            Cents result = new Cents(1) + new Cents(2);
            return result.Value;
        }

        // Inside checked, the checked operator is the one chosen.
        public static bool ThrowsOnOverflow()
        {
            try
            {
                Cents overflowing = checked(new Cents(int.MaxValue) + new Cents(1));
                return overflowing.Value == 0;
            }
            catch (OverflowException)
            {
                return true;
            }
        }
    }
}
