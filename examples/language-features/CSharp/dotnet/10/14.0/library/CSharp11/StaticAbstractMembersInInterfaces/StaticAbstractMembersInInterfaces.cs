namespace Net10_CSharp14_Library.CSharp11.StaticAbstractMembersInInterfaces
{
    // An interface may declare static abstract members, so an implementing TYPE
    // rather than an instance supplies them. This is what makes generic math
    // possible: a constraint can now demand an operator.
    public interface IIdentity<TSelf>
        where TSelf : IIdentity<TSelf>
    {
        static abstract TSelf Zero { get; }

        static abstract TSelf Add(TSelf left, TSelf right);

        // A static abstract operator is the headline case.
        static abstract TSelf operator +(TSelf left, TSelf right);
    }

    public readonly struct Meters : IIdentity<Meters>
    {
        public Meters(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public static Meters Zero
        {
            get { return new Meters(0); }
        }

        public static Meters Add(Meters left, Meters right)
        {
            return new Meters(left.Value + right.Value);
        }

        public static Meters operator +(Meters left, Meters right)
        {
            return Add(left, right);
        }
    }

    public class Summation
    {
        // The constraint gives this method access to the type's own operator,
        // with no interface instance and no virtual dispatch.
        public static T Sum<T>(T[] values)
            where T : IIdentity<T>
        {
            T total = T.Zero;
            foreach (T value in values)
            {
                total = total + value;
            }

            return total;
        }

        public static int SumMeters()
        {
            Meters total = Sum(new[] { new Meters(1), new Meters(2) });
            return total.Value;
        }
    }
}
