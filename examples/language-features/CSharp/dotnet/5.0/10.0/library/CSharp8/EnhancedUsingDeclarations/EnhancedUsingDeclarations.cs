using System;
using System.Collections.Generic;

namespace Net5_CSharp10_Library.CSharp8.EnhancedUsingDeclarations
{
    public class Resource : IDisposable
    {
        private readonly List<string> _log;
        private readonly string _name;

        public Resource(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Dispose()
        {
            _log.Add(_name);
        }
    }

    public class UsingDeclarations
    {
        // A using DECLARATION disposes at the end of the enclosing scope, with
        // no block and no extra nesting. The statement form still exists and is
        // still correct when the lifetime must be narrower than the scope.
        public static List<string> Declaration()
        {
            List<string> log = new List<string>();
            using Resource outer = new Resource("outer", log);
            using Resource inner = new Resource("inner", log);
            log.Add("body");
            return log;
        }

        // Disposal order is reverse of declaration, exactly as nested blocks
        // would give.
        public static List<string> StatementFormForContrast()
        {
            List<string> log = new List<string>();
            using (Resource outer = new Resource("outer", log))
            {
                using (Resource inner = new Resource("inner", log))
                {
                    log.Add("body");
                }
            }

            return log;
        }
    }
}
