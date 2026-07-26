using System.Runtime.InteropServices;

// ImportedFromTypeLib marks this assembly as standing in for a COM type library.
// That is what makes its types eligible for embedding by a consumer that
// references it with EmbedInteropTypes="true" — without it the compiler reports
// CS1752 and refuses to embed.
[assembly: ImportedFromTypeLib("CSharpComTypeLib")]
[assembly: Guid("8f1c4f2a-6d3b-4e21-9f7a-2c5b8e0d1a34")]

namespace CSharpComTypeLib
{
    [ComImport]
    [Guid("2b7e6c19-4a8d-4f53-b1c6-7d9e3f0a5b28")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMeasurable
    {
        int Measure(int scale);
    }
}
