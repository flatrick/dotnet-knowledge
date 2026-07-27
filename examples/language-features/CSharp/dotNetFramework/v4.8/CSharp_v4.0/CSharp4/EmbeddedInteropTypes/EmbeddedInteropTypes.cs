using CSharpComTypeLib;

namespace Net48_CSharp4_Library.CSharp4.EmbeddedInteropTypes
{
    // IMeasurable comes from CSharpComTypeLib, referenced with
    // EmbedInteropTypes="true". The compiler copies the interface's shape into
    // this assembly rather than keeping an assembly reference, so no
    // CSharpComTypeLib.dll is needed — or even produced — in the output folder.
    // That absence is how you can tell the embedding actually happened.
    public class Measurement
    {
        public static int Measure(IMeasurable measurable, int scale)
        {
            return measurable.Measure(scale);
        }

        // Each embedding assembly gets its own copy of the type. The runtime
        // treats those copies as the same type because their GUIDs match, not
        // because they share an assembly identity.
        public static string InterfaceName()
        {
            return typeof(IMeasurable).FullName;
        }
    }
}
