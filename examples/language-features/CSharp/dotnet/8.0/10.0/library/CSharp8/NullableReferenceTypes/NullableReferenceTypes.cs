#nullable enable

namespace Net8_CSharp10_Library.CSharp8.NullableReferenceTypes
{
    // The corpus disables nullable reference types tree-wide, because they are
    // themselves a C# 8.0 feature and would otherwise be retrofitted onto every
    // earlier example. This file is the one place that opts back in, with the
    // directive above.
    public class Account
    {
        // Not annotated: the compiler now treats this as non-nullable and will
        // warn if it can see it being assigned null.
        public string Owner { get; set; }

        // Annotated with ?: null is an expected value here.
        public string? Nickname { get; set; }

        public Account(string owner)
        {
            Owner = owner;
        }

        // Flow analysis narrows a nullable to non-nullable after a null test,
        // so the dereference below needs no annotation and no operator.
        public string Describe()
        {
            if (Nickname is null)
            {
                return Owner;
            }

            return Owner + " (" + Nickname.Length + ")";
        }

        private bool HasNickname()
        {
            return Nickname != null;
        }

        // The null-forgiving operator ! suppresses a warning the author judges
        // to be wrong. It earns its place only where flow analysis cannot
        // reach: the check here happens inside HasNickname, which the compiler
        // does not look through, so without ! this line would warn.
        public int NicknameLength()
        {
            if (!HasNickname())
            {
                return 0;
            }

            return Nickname!.Length;
        }
    }
}
