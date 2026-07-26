namespace CSharpNet7_10.CSharp10.SealedRecordToString
{
    // A record's ToString is generated and normally overridden again by each
    // derived record, so a base cannot fix the printed form. C# 10.0 allows
    // sealing it, which stops derived records from regenerating their own.
    public record Money(decimal Amount, string Currency)
    {
        public sealed override string ToString()
        {
            return Amount + " " + Currency;
        }
    }

    // Inherits the sealed ToString rather than generating a record one, so it
    // prints in the base's format instead of listing its own members.
    public record Salary(decimal Amount, string Currency, int Year)
        : Money(Amount, Currency);

    public class Usage
    {
        public static string PrintBase()
        {
            return new Money(10m, "SEK").ToString();
        }

        // Prints in the base format, not "Salary { Amount = ..., Year = ... }".
        public static string PrintDerived()
        {
            return new Salary(10m, "SEK", 2026).ToString();
        }
    }
}
