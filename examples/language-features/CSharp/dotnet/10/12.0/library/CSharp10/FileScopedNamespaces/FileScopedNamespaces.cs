// This is the one file in the corpus that uses a file-scoped namespace. Every
// other example uses the block form, because that is the era-faithful choice
// for its version — here the file-scoped form IS the feature being documented.
namespace Net10_CSharp12_Library.CSharp10.FileScopedNamespaces;

// A file-scoped namespace applies to the whole file and removes one level of
// indentation. Only one may appear per file, and it cannot be combined with a
// block-scoped namespace in the same file.
public class Declarations
{
    public static string Name()
    {
        return typeof(Declarations).Namespace;
    }
}

public class SecondType
{
    // Both types sit in the same namespace without either being nested in a
    // brace block.
    public static string Sibling()
    {
        return Declarations.Name();
    }
}
