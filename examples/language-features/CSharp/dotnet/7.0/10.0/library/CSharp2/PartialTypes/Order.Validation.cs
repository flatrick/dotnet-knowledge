namespace Net7_CSharp10_Library.CSharp2.PartialTypes
{
    // The second half reaches the first half's private fields directly:
    // after merging there is only one class.
    public partial class Order
    {
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(_id) && _total >= 0m;
        }
    }
}
