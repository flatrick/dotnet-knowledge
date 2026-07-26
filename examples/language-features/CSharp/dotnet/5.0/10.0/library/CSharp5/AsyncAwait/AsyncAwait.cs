using System;
using System.Threading.Tasks;

namespace CSharpNet6_10.CSharp5.AsyncAwait
{
    public class AsyncSamples
    {
        // An async method returns to its caller at the first await that has not
        // already completed, and resumes where it left off when that task ends.
        public static async Task<int> AddAsync(int left, int right)
        {
            await Task.Yield();
            return left + right;
        }

        // async Task is the form with no result.
        public static async Task DelayAsync()
        {
            await Task.Delay(1);
        }

        // Starting both tasks before awaiting either runs them concurrently;
        // awaiting each in turn would serialize them.
        public static async Task<int> SumConcurrentlyAsync()
        {
            Task<int> first = AddAsync(1, 2);
            Task<int> second = AddAsync(3, 4);
            int[] results = await Task.WhenAll(first, second);
            return results[0] + results[1];
        }

        // async void exists for event handlers only: nothing can await it, so
        // an exception escaping here is raised on the synchronization context
        // rather than captured in a task.
        public static async void FireAndForget(Action onDone)
        {
            await Task.Yield();
            onDone();
        }
    }
}
