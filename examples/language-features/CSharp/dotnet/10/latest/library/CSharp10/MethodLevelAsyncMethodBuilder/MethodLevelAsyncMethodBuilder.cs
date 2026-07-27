using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Net10_CSharpLatest_Library.CSharp10.MethodLevelAsyncMethodBuilder
{
    // C# 7.0 let a TYPE opt into a custom async method builder, which applied
    // to every async method returning it. C# 10.0 allows the attribute on a
    // single METHOD, so one method can use a different builder without a new
    // return type.
    //
    // The builder below simply forwards to the built-in one; a real one would
    // pool state machines or add instrumentation. It is written out because the
    // feature is precisely the ability to substitute it.
    public struct ForwardingBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> _inner;

        public static ForwardingBuilder<T> Create()
        {
            return new ForwardingBuilder<T> { _inner = AsyncTaskMethodBuilder<T>.Create() };
        }

        public Task<T> Task
        {
            get { return _inner.Task; }
        }

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            _inner.Start(ref stateMachine);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _inner.SetStateMachine(stateMachine);
        }

        public void SetResult(T result)
        {
            _inner.SetResult(result);
        }

        public void SetException(Exception exception)
        {
            _inner.SetException(exception);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }

    public class Builders
    {
        // The attribute sits on the method, not on Task<int>, so only this
        // method's state machine is built by ForwardingBuilder.
        [AsyncMethodBuilder(typeof(ForwardingBuilder<>))]
        public static async Task<int> AddAsync(int left, int right)
        {
            await System.Threading.Tasks.Task.Yield();
            return left + right;
        }

        // An ordinary async method alongside it, using the default builder.
        public static async Task<int> SubtractAsync(int left, int right)
        {
            await System.Threading.Tasks.Task.Yield();
            return left - right;
        }
    }
}
