using System.Threading.Tasks;

namespace CSharpFw48Cs80.CSharp7.GeneralizedAsyncReturnTypes
{
    public class ValueTaskSamples
    {
        // Before C# 7.0 an async method could return only void, Task, or
        // Task<T>. Now any type marked with AsyncMethodBuilderAttribute
        // qualifies; ValueTask<T> is the one the BCL ships for it.
        //
        // This method is not async at all — it wraps an already-known result,
        // which is the case ValueTask exists to keep allocation-free.
        public static ValueTask<int> CachedAsync(int value)
        {
            return new ValueTask<int>(value);
        }

        // The same type as the return of a genuinely asynchronous method. Here
        // a state machine and a backing task do get allocated, so ValueTask
        // pays off only when the synchronous path is the common one.
        public static async ValueTask<int> ComputeAsync(int left, int right)
        {
            await Task.Yield();
            return left + right;
        }

        // ValueTask is awaited exactly like Task.
        public static async Task<int> ConsumeAsync()
        {
            int cached = await CachedAsync(1);
            int computed = await ComputeAsync(2, 3);
            return cached + computed;
        }

        // The non-generic form, for the no-result case.
        public static async ValueTask DoAsync()
        {
            await Task.Yield();
        }
    }
}
