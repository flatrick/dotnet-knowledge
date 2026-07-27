using System;
using System.IO;

namespace Net48_CSharp8_Library.CSharp1.ControlFlowStatements
{
    public class Resource : IDisposable
    {
        private bool _disposed;

        public bool Disposed
        {
            get { return _disposed; }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    public class ControlFlow
    {
        // using over an existing variable: disposal still happens on every exit path.
        public static bool DisposesDeterministically()
        {
            Resource resource = new Resource();
            using (resource)
            {
                // The resource is live for the duration of this block.
            }

            return resource.Disposed;
        }

        // using with a declaration: the common form.
        public static string ReadFirstLine(string path)
        {
            using (StreamReader reader = new StreamReader(path))
            {
                return reader.ReadLine();
            }
        }

        // goto case transfers to another switch section; goto label jumps out of the switch.
        public static int Classify(int value)
        {
            switch (value)
            {
                case 0:
                    goto case 1;
                case 1:
                    return 1;
                default:
                    goto Fallback;
            }

        Fallback:
            return -1;
        }
    }
}
