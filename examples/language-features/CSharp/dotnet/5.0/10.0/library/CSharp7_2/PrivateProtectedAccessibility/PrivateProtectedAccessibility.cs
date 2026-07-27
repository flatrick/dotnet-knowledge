namespace Net5_CSharp10.CSharp7_2.PrivateProtectedAccessibility
{
    public class Repository
    {
        // private protected = derived types, but only those in THIS assembly.
        // It is the intersection of protected and internal.
        private protected int ConnectionCount;

        // protected internal is the union instead: derived types anywhere, OR
        // any type in this assembly. The contrast is the point of this pair.
        protected internal int RetryCount;

        public Repository()
        {
            ConnectionCount = 1;
            RetryCount = 3;
        }
    }

    // Derived AND in the same assembly, so it can reach both members.
    public class SqlRepository : Repository
    {
        public int Total()
        {
            return ConnectionCount + RetryCount;
        }
    }
}
