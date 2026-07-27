using System;

namespace Net48_CSharp1_Library.CSharp1.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ReviewedAttribute : Attribute
    {
        private readonly string _reviewer;
        private string _comment;

        public ReviewedAttribute(string reviewer)
        {
            _reviewer = reviewer;
        }

        // Positional argument, bound by the constructor.
        public string Reviewer
        {
            get { return _reviewer; }
        }

        // Named argument, bound by assigning a settable property at the usage site.
        public string Comment
        {
            get { return _comment; }
            set { _comment = value; }
        }
    }

    [Reviewed("alice")]
    [Reviewed("bob", Comment = "second pass")]
    public class AuditedService
    {
        [Reviewed("carol")]
        [Obsolete("Use Replacement instead.", false)]
        public void Legacy()
        {
        }

        public void Replacement()
        {
        }
    }
}
