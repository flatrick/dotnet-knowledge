using System;

namespace Net6_CSharp10.CSharp6.ExceptionFilters
{
    public class Filters
    {
        private static string _lastLogged = string.Empty;

        public static string LastLogged
        {
            get { return _lastLogged; }
        }

        // A when clause decides whether this catch applies at all. When it is
        // false the search continues to the next handler.
        public static string CatchOnlyMatching(int code)
        {
            try
            {
                throw new InvalidOperationException("code " + code);
            }
            catch (InvalidOperationException ex) when (code == 1)
            {
                return "handled:" + ex.Message;
            }
            catch (InvalidOperationException)
            {
                return "fell-through";
            }
        }

        // A filter that always returns false observes the exception without
        // handling it — and, unlike catch-log-rethrow, it runs before the stack
        // unwinds, so the original stack is still intact when it does.
        public static string LogWithoutHandling()
        {
            try
            {
                throw new ArgumentException("boom");
            }
            catch (Exception ex) when (Log(ex))
            {
                return "unreachable";
            }
            catch (ArgumentException)
            {
                return "outer:" + _lastLogged;
            }
        }

        private static bool Log(Exception ex)
        {
            _lastLogged = ex.Message;
            return false;
        }
    }
}
