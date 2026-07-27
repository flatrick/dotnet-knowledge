using System;

namespace Net9_CSharp10_Library.CSharp7_3.OverloadResolutionImprovements73
{
    public class OverloadImprovements
    {
        public static void Handle(Action action)
        {
            action();
        }

        public static void Handle(Func<int> function)
        {
            function();
        }

        private static void NoResult()
        {
        }

        // C# 7.3 removes a method-group-conversion candidate whose return
        // type does not match the delegate's return type before overload
        // resolution runs, instead of leaving it in the set. NoResult is
        // void, so it can convert only to Action, never to Func<int>; before
        // 7.3 both Handle overloads still stayed in play for this call and
        // neither was a better match, making it CS0121-ambiguous. C# 7.3
        // discards Handle(Func<int>) up front, leaving Handle(Action) as the
        // only candidate.
        public static void PassMethodGroup()
        {
            Handle(NoResult);
        }

        public void Overloaded(int x, long y)
        {
        }

        public static void Overloaded(long x, int y)
        {
        }

        // C# 7.3 discards the static members of a method group when the call
        // has an instance receiver, instead of leaving them as candidates.
        // Overloaded(int, long) and Overloaded(long, int) are equally good
        // conversions for the call below (each argument is int, one
        // parameter list wins on the first position, the other on the
        // second), so before 7.3 - with the static overload still a
        // candidate - this was CS0121-ambiguous even though the call has an
        // explicit instance receiver. C# 7.3 discards the static overload
        // because of that receiver, leaving the instance overload as the
        // only candidate.
        public static void CallWithInstanceReceiver()
        {
            OverloadImprovements instance = new OverloadImprovements();
            instance.Overloaded(1, 1);
        }
    }
}
