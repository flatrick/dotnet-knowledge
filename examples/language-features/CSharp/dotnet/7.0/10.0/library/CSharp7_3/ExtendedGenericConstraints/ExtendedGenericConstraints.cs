using System;

namespace Net7_CSharp10.CSharp7_3.ExtendedGenericConstraints
{
    public enum Level
    {
        Low,
        High
    }

    public class Constraints
    {
        // unmanaged: T is a value type containing no references at any depth.
        public static bool IsDefaultValue<T>(T value) where T : unmanaged
        {
            return value.Equals(default(T));
        }

        // Enum and Delegate became legal constraints in C# 7.3; before that the
        // compiler rejected both by name.
        public static string NameOf<T>(T value) where T : Enum
        {
            return value.ToString();
        }

        public static Delegate Identity<T>(T handler) where T : Delegate
        {
            return handler;
        }

        public static bool CallUnmanaged()
        {
            return IsDefaultValue(0) && IsDefaultValue(Level.Low);
        }

        public static string CallEnum()
        {
            return NameOf(Level.High);
        }

        public static Delegate CallDelegate()
        {
            return Identity<Action>(CallEnumIgnoringResult);
        }

        private static void CallEnumIgnoringResult()
        {
        }
    }
}
