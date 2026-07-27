using System;

namespace Net5_CSharp10.CSharp6.NameOfOperator
{
    public class Account
    {
        private readonly string _owner;

        public string Owner
        {
            get { return _owner; }
        }

        // nameof produces the name as a compile-time constant, so renaming the
        // parameter updates this string too — a literal "owner" would not.
        public Account(string owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            _owner = owner;
        }

        // Only the final identifier is produced, not the qualified path.
        public static string MemberName()
        {
            return nameof(Account.Owner);
        }

        public static string TypeName()
        {
            return nameof(Account);
        }

        public static string LocalName()
        {
            int total = 0;
            return nameof(total) + total;
        }

        // A namespace works too, and yields only its last segment.
        public static string NamespaceName()
        {
            return nameof(System.Collections);
        }
    }
}
