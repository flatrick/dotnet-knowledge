using System;

namespace Net10_CSharp12_Library.CSharp11.Utf8StringLiterals
{
    public class Utf8Literals
    {
        // A u8 suffix produces UTF-8 bytes at compile time, typed as
        // ReadOnlySpan<byte>. Before C# 11.0 the same thing meant calling
        // Encoding.UTF8.GetBytes at run time, on every call.
        public static ReadOnlySpan<byte> Literal()
        {
            return "hello"u8;
        }

        public static int Length()
        {
            return "hello"u8.Length;
        }

        // The bytes really are UTF-8: a non-ASCII character takes more than
        // one byte, so the byte length exceeds the character count.
        public static bool MultiByteCharacter()
        {
            return "é"u8.Length == 2;
        }

        // The result is a span over static data, so it allocates nothing.
        public static byte First()
        {
            ReadOnlySpan<byte> bytes = "abc"u8;
            return bytes[0];
        }
    }
}
