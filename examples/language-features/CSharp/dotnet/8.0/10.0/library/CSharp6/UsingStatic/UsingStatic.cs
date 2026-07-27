using static System.Math;
using static System.String;

namespace Net8_CSharp10.CSharp6.UsingStatic
{
    // using static imports a type's static members directly, so they can be
    // named without the type qualifier.
    public static class Geometry
    {
        public static double CircleArea(double radius)
        {
            return PI * Pow(radius, 2);
        }

        public static double Hypotenuse(double a, double b)
        {
            return Sqrt(Pow(a, 2) + Pow(b, 2));
        }

        public static int Larger(int left, int right)
        {
            return Max(left, right);
        }
    }

    public static class Text
    {
        // Members of two different static imports coexist; overload resolution
        // treats them as if they were declared in the same scope.
        public static string JoinNonEmpty(string separator, string[] parts)
        {
            if (IsNullOrEmpty(separator))
            {
                return Concat(parts);
            }

            return Join(separator, parts);
        }
    }
}
