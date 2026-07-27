namespace Net10_CSharpLatest_Library.CSharp12.RefReadonlyParameters
{
    public readonly struct Large
    {
        public Large(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }

    public class RefReadonly
    {
        // ref readonly sits between in and ref: like in, it forbids writing;
        // like ref, it requires the caller to be explicit and to pass a
        // variable rather than a value. It exists mainly so APIs that used ref
        // for performance can forbid mutation without breaking their callers.
        public static double Length(ref readonly Large value)
        {
            return value.X + value.Y + value.Z;
        }

        // in permits a call with a literal or expression; ref readonly wants a
        // variable and the modifier at the call site.
        public static double InParameter(in Large value)
        {
            return value.X;
        }

        public static double CallRefReadonly()
        {
            Large value = new Large(1, 2, 3);
            return Length(in value);
        }

        public static double CallInWithTemporary()
        {
            return InParameter(new Large(1, 2, 3));
        }
    }
}
