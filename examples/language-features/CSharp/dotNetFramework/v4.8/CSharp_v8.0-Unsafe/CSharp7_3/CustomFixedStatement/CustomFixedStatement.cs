namespace CSharpFw48Cs80Unsafe.CSharp7_3.CustomFixedStatement
{
    // A type becomes usable in a fixed statement by exposing GetPinnableReference.
    public class PixelBuffer
    {
        private readonly int[] _pixels = new int[4];

        public ref int GetPinnableReference()
        {
            return ref _pixels[0];
        }

        public int Count
        {
            get { return _pixels.Length; }
        }
    }

    public unsafe class Pinning
    {
        public static int ReadFirst(PixelBuffer buffer)
        {
            fixed (int* pointer = buffer)
            {
                return *pointer;
            }
        }
    }
}
