using System;
using System.Threading.Tasks;

namespace Net48_CSharp7_1_Library.CSharp6.AwaitInCatchFinally
{
    public class Recovery
    {
        // Before C# 6.0 await was banned in catch and finally blocks, which
        // forced asynchronous cleanup to be hoisted out of the handler by hand.
        public static async Task<string> AwaitInCatchAsync()
        {
            try
            {
                await FailAsync();
                return "no-throw";
            }
            catch (InvalidOperationException ex)
            {
                await Task.Yield();
                return "recovered:" + ex.Message;
            }
        }

        // The finally block still runs on every exit path, and may now await.
        public static async Task<string> AwaitInFinallyAsync()
        {
            string result;
            try
            {
                result = await ValueAsync();
            }
            finally
            {
                await Task.Yield();
            }

            return result;
        }

        private static async Task FailAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        private static async Task<string> ValueAsync()
        {
            await Task.Yield();
            return "value";
        }
    }
}
