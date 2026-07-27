namespace Net6_CSharp10.CSharp1.OperatorOverloading
{
    public struct Money
    {
        private readonly decimal _amount;

        public Money(decimal amount)
        {
            _amount = amount;
        }

        public decimal Amount
        {
            get { return _amount; }
        }

        public static Money operator +(Money left, Money right)
        {
            return new Money(left._amount + right._amount);
        }

        public static Money operator -(Money left, Money right)
        {
            return new Money(left._amount - right._amount);
        }

        public static bool operator ==(Money left, Money right)
        {
            return left._amount == right._amount;
        }

        public static bool operator !=(Money left, Money right)
        {
            return left._amount != right._amount;
        }

        // User-defined implicit conversion: decimal widens into Money silently.
        public static implicit operator Money(decimal amount)
        {
            return new Money(amount);
        }

        // User-defined explicit conversion: Money narrows to decimal only via a cast.
        public static explicit operator decimal(Money money)
        {
            return money._amount;
        }

        // Overloading == obliges the type to override Equals and GetHashCode.
        public override bool Equals(object obj)
        {
            return obj is Money && this == (Money)obj;
        }

        public override int GetHashCode()
        {
            return _amount.GetHashCode();
        }
    }
}
