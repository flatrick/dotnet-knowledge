using System.Collections.Generic;
using System.Threading.Tasks;

namespace Net6_CSharp10_Library.CSharp8.AsyncStreams
{
    public class Streams
    {
        // An async iterator returns IAsyncEnumerable<T> and may both await and
        // yield. Before C# 8.0 a method could do one or the other, never both.
        public static async IAsyncEnumerable<int> RangeAsync(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }

        // await foreach consumes it, awaiting each MoveNextAsync.
        public static async Task<int> SumAsync(int count)
        {
            int total = 0;
            await foreach (int value in RangeAsync(count))
            {
                total += value;
            }

            return total;
        }
    }

    // await using disposes an IAsyncDisposable, awaiting DisposeAsync.
    public class AsyncResource : System.IAsyncDisposable
    {
        private static int _disposeCount;

        public static int DisposeCount
        {
            get { return _disposeCount; }
        }

        public ValueTask DisposeAsync()
        {
            _disposeCount++;
            return default;
        }
    }

    public class AsyncDisposal
    {
        public static async Task UseAsync()
        {
            await using (AsyncResource resource = new AsyncResource())
            {
                await Task.Yield();
            }
        }
    }
}
