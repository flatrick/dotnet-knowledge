using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Net48_CSharp7_3_Library.CSharp7.GeneralizedAsyncReturnTypes
{
    // Before C# 7.0 the return type of an async method could only be void, Task, or Task<T> —
    // the compiler hard-coded those three. C# 7.0 replaced the hard-coding with a lookup: any
    // type carrying [AsyncMethodBuilder(typeof(SomeBuilder))] becomes a legal async return type,
    // and the compiler drives that builder to run the generated state machine.
    //
    // ValueTask<T> is the type the BCL ships for it — on net48 it comes from the
    // System.Threading.Tasks.Extensions package — so it is what real code uses. But ValueTask on
    // its own cannot show that the rule is *general* rather than a second hard-coded special
    // case, so MyTask<T> further down implements the mechanism from scratch.
    //
    // Caveat worth knowing: <LangVersion> does not police this row. Roslyn gates a feature on
    // language version only where its binder explicitly asks it to, and generalized async return
    // types never got that check — every construct in this file still compiles clean under
    // /langversion:5. Only a genuinely pre-7.0 compiler rejects them, with CS1983.

    public class ValueTaskSamples
    {
        // THIS is the C# 7.0 feature: an async method returning neither void, Task, nor Task<T>.
        // A state machine and a backing task do get allocated here, so ValueTask pays off only
        // when the synchronous path is the common one.
        public static async ValueTask<int> ComputeAsync(int left, int right)
        {
            await Task.Yield();
            return left + right;
        }

        // The non-generic form, for the no-result case. Also the feature.
        public static async ValueTask DoAsync()
        {
            await Task.Yield();
        }

        // NOT the feature, kept for contrast: no `async` keyword, so no state machine and no
        // builder is involved. It is an ordinary method that happens to return a struct wrapping
        // an already-known result — the allocation-free case ValueTask exists for. (The -Async
        // suffix is still correct here; TAP names methods for what they return, not for the
        // keyword they use.)
        public static ValueTask<int> CachedAsync(int value)
        {
            return new ValueTask<int>(value);
        }

        // Also NOT the feature: awaiting a non-Task type has been legal since C# 5.0, which made
        // `await` pattern-based — any type exposing a suitable GetAwaiter will do. C# 7.0
        // generalized the *return* side; the await side was already open.
        public static async Task<int> ConsumeAsync()
        {
            int cached = await CachedAsync(1);
            int computed = await ComputeAsync(2, 3);
            return cached + computed;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The mechanism itself, with no BCL task type involved.
    // ---------------------------------------------------------------------------------------

    // The attribute is the entire eligibility test. Delete it and AddAsync below stops compiling
    // with CS1983 — "the return type of an async method must be void, Task, Task<T>, a task-like
    // type, IAsyncEnumerable<T>, or IAsyncEnumerator<T>". That is the same error code a pre-7.0
    // compiler raises for ValueTask, back when the message stopped at Task<T>.
    [AsyncMethodBuilder(typeof(MyTaskBuilder<>))]
    public class MyTask<T>
    {
        private T _result;

        public T Result
        {
            get { return _result; }
        }

        public static MyTask<T> FromResult(T value)
        {
            MyTask<T> task = new MyTask<T>();
            task._result = value;
            return task;
        }

        // Awaiting is pattern-based, so exposing GetAwaiter is all it takes to be awaitable.
        // That part is C# 5.0; the attribute above is what makes the type *returnable*.
        public MyTaskAwaiter<T> GetAwaiter()
        {
            return new MyTaskAwaiter<T>(this);
        }

        internal void Complete(T result)
        {
            _result = result;
        }
    }

    public struct MyTaskAwaiter<T> : INotifyCompletion
    {
        private readonly MyTask<T> _task;

        internal MyTaskAwaiter(MyTask<T> task)
        {
            _task = task;
        }

        // This sample always completes synchronously, so the state machine never suspends. A
        // real implementation would track completion and queue continuations.
        public bool IsCompleted
        {
            get { return true; }
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }

        public T GetResult()
        {
            return _task.Result;
        }
    }

    // The compiler requires this shape by member name, not by interface — it emits calls to
    // these members directly into the state machine it generates.
    public class MyTaskBuilder<T>
    {
        private readonly MyTask<T> _task = new MyTask<T>();

        // 1. The compiler creates the builder,
        public static MyTaskBuilder<T> Create()
        {
            return new MyTaskBuilder<T>();
        }

        // 2. starts the state machine through it,
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        // 3. and hands the caller whatever Task returns — that value is what the async method
        //    appears to return at the call site.
        public MyTask<T> Task
        {
            get { return _task; }
        }

        public void SetResult(T result)
        {
            _task.Complete(result);
        }

        public void SetException(Exception exception)
        {
            throw exception;
        }

        // Called when an await suspends. Nothing to schedule here, because MyTaskAwaiter always
        // reports IsCompleted.
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
        }

        // Required by the pattern; it matters only to the struct state machine the compiler
        // emits in release builds, which boxes itself on first suspension.
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }
    }

    public class CustomTaskLikeSamples
    {
        // MyTask<T> is not a Task, is not in the BCL, and the compiler has no special knowledge
        // of it. The attribute alone makes this signature legal.
        public static async MyTask<int> AddAsync(int left, int right)
        {
            int seed = await MyTask<int>.FromResult(100);
            return seed + left + right;
        }
    }
}
