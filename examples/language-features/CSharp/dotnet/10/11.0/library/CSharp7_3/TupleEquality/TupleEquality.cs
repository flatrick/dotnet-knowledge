namespace Net10_CSharp11_Library.CSharp7_3.TupleEquality
{
    public class TupleComparison
    {
        // == and != on tuples compare element by element. Before C# 7.3 the
        // operators were not defined for tuples at all.
        public static bool Equal()
        {
            var left = (1, "x");
            var right = (1, "x");
            return left == right;
        }

        public static bool NotEqual()
        {
            return (1, 2) != (1, 3);
        }

        // Element names take no part in the comparison; only positions and
        // values do, so these two differently-named tuples are equal.
        public static bool NamesAreIgnored()
        {
            var left = (count: 1, name: "x");
            var right = (id: 1, label: "x");
            return left == right;
        }

        // The comparison lifts over nullable tuples.
        public static bool LiftedOverNullable()
        {
            (int, int)? value = (1, 2);
            return value == (1, 2);
        }

        // Elements are compared with their own ==, and each is converted first,
        // so an int and a long element compare fine.
        public static bool ElementsConvert()
        {
            (int, int) left = (1, 2);
            (long, long) right = (1L, 2L);
            return left == right;
        }
    }
}
