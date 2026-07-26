using System;

namespace CSharpNet7_10.CSharp9.StaticAnonymousFunctions
{
    public class StaticLambdas
    {
        private const int Offset = 10;

        // A static lambda cannot capture locals or parameters. As with a static
        // local function, the modifier makes an unintended capture a compile
        // error rather than something to notice in review.
        public static Func<int, int> Doubler()
        {
            return static value => value * 2;
        }

        // Constants and static members remain reachable.
        public static Func<int, int> WithConstant()
        {
            return static value => value + Offset;
        }

        // The older anonymous-method syntax takes the modifier too.
        public static Func<int, int> AnonymousMethod()
        {
            return static delegate(int value) { return value * 3; };
        }

        // A non-static lambda may still capture, and is correct when capture is
        // what you want.
        public static Func<int> Capturing(int seed)
        {
            return () => seed + 1;
        }
    }
}
