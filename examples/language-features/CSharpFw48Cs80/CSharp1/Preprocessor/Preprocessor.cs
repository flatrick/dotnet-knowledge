#define SHOWCASE

namespace CSharpFw48Cs80.CSharp1.Preprocessor
{
    public class ConditionalCompilation
    {
        // #define above puts SHOWCASE in scope for this file only.
        public static string Describe()
        {
#if SHOWCASE
            return "compiled with SHOWCASE defined";
#else
            return "compiled without SHOWCASE";
#endif
        }

        // DEBUG comes from the build configuration, not from this file.
        // RELEASE is not one of the SDK's default symbols — it defines DEBUG+TRACE for
        // Debug and only TRACE for Release — so the #elif branch below is dead in every
        // configuration here. It is shown only as an example of a user-defined symbol
        // a project could define itself via DefineConstants.
        public static string DescribeBuildConfiguration()
        {
#if DEBUG
            return "debug";
#elif RELEASE
            return "release";
#else
            return "unspecified";
#endif
        }

        #region Grouping for the editor
        // #region/#endregion name a collapsible span. They are the one preprocessor
        // directive with no effect whatever on compilation — the compiler skips them
        // and only editors read them.
        //
        // #pragma is deliberately absent here: it arrived in C# 2.0, and this row is
        // C# 1.0. Reaching for it would make the sample compile at a version that
        // predates the directive it shows.
        public static string Grouped()
        {
            return "regions are for readers, not the compiler";
        }
        #endregion
    }
}
