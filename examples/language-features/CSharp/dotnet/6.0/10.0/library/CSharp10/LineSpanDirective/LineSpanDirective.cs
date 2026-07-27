namespace Net6_CSharp10.CSharp10.LineSpanDirective
{
    public class Mapping
    {
        // The C# 10.0 #line form maps a span of generated code back to an exact
        // region of an original file — start line and column, end line and
        // column, an optional character offset, and the file name. The older
        // #line form could only redirect a line number.
        //
        // It exists for source generators and DSL compilers, so that a
        // diagnostic or a debugger step lands on the author's real source
        // rather than on generated text. Nothing about it changes what runs.
        public static int Mapped()
        {
#line (7, 5) - (7, 25) 3 "Original.template"
            int value = 21;
            return value * 2;
#line default
        }

        // Outside the directive, positions are the ordinary ones for this file.
        public static int Unmapped()
        {
            return 42;
        }
    }
}
