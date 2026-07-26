// A global using applies to every file in the compilation, so a namespace used
// almost everywhere need not be imported file by file. One file declaring it is
// enough; by convention that file is a dedicated one rather than a feature file.
//
// This directive is deliberately narrow. A global using is project-wide state:
// importing something broad here would change name resolution in every other
// example in this project, which is exactly the kind of action at a distance
// worth being careful with.
global using System.Text;

namespace CSharpNet6_10.CSharp10.GlobalUsingDirectives
{
    public class Builders
    {
        // StringBuilder resolves without a file-level using System.Text,
        // because the global directive above is in effect.
        public static string Build(string value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(value);
            builder.Append('!');
            return builder.ToString();
        }

        // A global using alias is also allowed, and is scoped the same way.
        public static int Length(string value)
        {
            StringBuilder builder = new StringBuilder(value);
            return builder.Length;
        }
    }
}
