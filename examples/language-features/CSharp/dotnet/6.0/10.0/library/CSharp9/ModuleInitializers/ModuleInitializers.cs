using System.Runtime.CompilerServices;

namespace Net6_CSharp10_Library.CSharp9.ModuleInitializers
{
    public class Startup
    {
        private static bool _initialized;

        public static bool Initialized
        {
            get { return _initialized; }
        }

        // A module initializer runs once, before any other code in the module,
        // without the caller asking. The method must be static, parameterless,
        // return void, be non-generic, and be accessible from the module —
        // the compiler enforces every one of those.
        //
        // It is the supported replacement for a static constructor on a
        // "module" type, which the language had no way to express.
        [ModuleInitializer]
        internal static void Initialize()
        {
            _initialized = true;
        }
    }
}
