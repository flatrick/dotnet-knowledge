// Top-level statements let a file's statements stand as the program's entry
// point, with no namespace, class, or Main declaration around them. The
// compiler synthesizes all three.
//
// This row lives in the corpus's one executable project because the feature
// requires it: a library compilation is rejected with CS8805, "Program using
// top-level statements must be an executable". At most one file per
// compilation may use the form.
using System;

// The arguments are available as `args`, and a value returned from the last
// statement becomes the process exit code — both supplied by the synthesized
// entry point rather than declared here.
Console.WriteLine($"top-level statements: {args.Length} argument(s)");

// Local functions may be declared among top-level statements, and are scoped
// to the synthesized method.
int Doubled(int value) => value * 2;

// Types declared in the same file come after the statements.
Console.WriteLine(Helper.Describe(Doubled(21)));

return 0;

internal static class Helper
{
    public static string Describe(int value) => $"value={value}";
}
