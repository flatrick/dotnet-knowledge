namespace Net10_CSharp11_Library.CSharp11.FileLocalTypes
{
    // A file-local type is visible only inside its own file, so a generator can
    // emit a helper without risking a name collision anywhere else in the
    // assembly. It is narrower than internal, which spans the whole assembly.
    file class Helper
    {
        public static int Double(int value)
        {
            return value * 2;
        }
    }

    // Another file may declare its own Helper with no conflict, which is the
    // property the modifier exists for.
    public class Usage
    {
        public static int Call()
        {
            return Helper.Double(21);
        }
    }
}
