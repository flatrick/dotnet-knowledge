using System.Threading.Tasks;

namespace Net9_CSharp10.CSharp7_1.AsyncMain
{
    public class Program
    {
        // C# 7.1 let an entry point be async and return Task or Task<int>, so a
        // console app no longer had to block on .GetAwaiter().GetResult() in a
        // synchronous Main.
        //
        // This project is a class library. The compiler applies entry-point
        // rules only when OutputType is Exe, so here this is an ordinary method
        // that merely has the signature the feature introduced.
        public static async Task<int> Main(string[] args)
        {
            await Task.Yield();
            return args.Length;
        }
    }
}
