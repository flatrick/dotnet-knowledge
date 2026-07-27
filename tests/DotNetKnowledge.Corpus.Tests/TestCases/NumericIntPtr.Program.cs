using System;
using Net10_CSharpLatest_Library.CSharp11.NumericIntPtr;

Console.WriteLine($"From constant: {NumericPointerSized.FromConstant()}");
Console.WriteLine($"Multiply: {NumericPointerSized.Multiply((IntPtr)6, (IntPtr)7)}");
Console.WriteLine(
    $"Classify: {NumericPointerSized.Classify((IntPtr)0)}, " +
    $"{NumericPointerSized.Classify((IntPtr)1)}, " +
    $"{NumericPointerSized.Classify((IntPtr)2)}");
