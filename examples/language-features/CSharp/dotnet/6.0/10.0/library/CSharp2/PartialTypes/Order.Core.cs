namespace Net6_CSharp10_Library.CSharp2.PartialTypes
{
    // One half of the type. The other half is in Order.Validation.cs, and the
    // compiler merges both into a single class.
    public partial class Order
    {
        private readonly string _id;
        private readonly decimal _total;

        public Order(string id, decimal total)
        {
            _id = id;
            _total = total;
        }

        public string Id
        {
            get { return _id; }
        }

        public decimal Total
        {
            get { return _total; }
        }
    }
}
