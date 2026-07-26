namespace CSharpNet6_10.CSharp9.PartialMethodsWithReturnedValues
{
    // A C# 3.0 partial method had to return void, take no out parameters, and
    // be implicitly private, so the compiler could erase it when unimplemented.
    // C# 9.0 lifts all three restrictions — at the cost of requiring an
    // implementation, since there is now a value that must come from somewhere.
    public partial class Validator
    {
        // Explicit accessibility, a return value, and an out parameter are all
        // legal now, and all three make the implementing half mandatory.
        public partial bool TryValidate(string input, out string error);

        public bool Check(string input)
        {
            return TryValidate(input, out _);
        }
    }

    public partial class Validator
    {
        public partial bool TryValidate(string input, out string error)
        {
            if (string.IsNullOrEmpty(input))
            {
                error = "empty";
                return false;
            }

            error = null;
            return true;
        }
    }
}
