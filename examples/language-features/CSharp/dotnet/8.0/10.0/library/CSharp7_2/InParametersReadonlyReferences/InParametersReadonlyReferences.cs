using System;

namespace Net8_CSharp10.CSharp7_2.InParametersReadonlyReferences
{
    public readonly struct Vector3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public class InParameters
    {
        private static readonly Vector3 _origin = new Vector3(0, 0, 0);

        // in passes by reference but forbids writing through it: the caller's
        // copy is avoided without giving the callee permission to mutate.
        public static double Length(in Vector3 value)
        {
            return Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        }

        // The modifier is optional at the call site; naming it documents intent.
        public static double CallWithIn()
        {
            Vector3 value = new Vector3(1, 2, 2);
            return Length(in value);
        }

        public static double CallWithoutModifier()
        {
            Vector3 value = new Vector3(3, 0, 4);
            return Length(value);
        }

        // A ref readonly return hands back a reference the caller may read but
        // not write, so a large shared value need not be copied out.
        public static ref readonly Vector3 Origin()
        {
            return ref _origin;
        }

        public static double ReadThroughRefReadonly()
        {
            ref readonly Vector3 origin = ref Origin();
            return origin.X;
        }
    }
}
