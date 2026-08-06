using System;

// This is a support assembly, not a corpus project: it holds no feature examples and never appears
// in a manifest row's target column. It exists so the VB 15 row "Consuming C# reference return
// values" has something to consume on net48. VB can call a ref-returning method but cannot declare
// one, so that row needs a C#-authored subject; net10's version of the row uses
// CollectionsMarshal.GetValueRefOrNullRef, which has no net48 backport.
//
// It is a separate assembly rather than a reference to the corpus's own C# 7.0 ref-returns row
// because that row's project is the legacy non-SDK CSharp_v7.0, which dotnet build cannot traverse
// to. Pointing at a cumulative project that merely contains the row instead would make a corpus
// project double as a build dependency, and couple the VB family to a C# 8.0 coordinate for a
// C# 7.0 subject.
namespace CSharpRefReturnLib
{
    public class RefSamples
    {
        // A ref return hands back the storage location itself rather than a copy of the value in
        // it. No unsafe context is involved: the compiler proves the reference cannot outlive what
        // it points into.
        public static ref int Find(int[] values, int target)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                {
                    return ref values[i];
                }
            }

            throw new ArgumentException("not found", nameof(target));
        }

        // A ref local aliases that location, so assigning through it writes into the original
        // array. This is the half VB cannot express, which is why the write has to happen here.
        public static int[] ReplaceInPlace(int[] values, int target, int replacement)
        {
            ref int slot = ref Find(values, target);
            slot = replacement;
            return values;
        }
    }
}
