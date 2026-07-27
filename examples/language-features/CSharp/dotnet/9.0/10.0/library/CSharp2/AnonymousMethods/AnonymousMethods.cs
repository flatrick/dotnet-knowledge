using System;
using System.Collections.Generic;

namespace Net9_CSharp10_Library.CSharp2.AnonymousMethods
{
    public class AnonymousMethodSamples
    {
        // An anonymous method supplies the delegate body inline.
        public static List<int> WhereEven(List<int> values)
        {
            return values.FindAll(delegate(int value) { return value % 2 == 0; });
        }

        // Anonymous methods capture enclosing locals, forming a closure.
        public static Predicate<int> GreaterThan(int limit)
        {
            return delegate(int value) { return value > limit; };
        }

        // The parameter list may be omitted entirely when the body ignores it.
        public static EventHandler IgnoreArguments(Action onRaised)
        {
            return delegate { onRaised(); };
        }
    }
}
