namespace Net10_CSharp11_Library.CSharp8.DefaultInterfaceMembers
{
    // An interface member may now carry a body. The implementation is inherited
    // by any implementer that does not supply its own, which lets an interface
    // gain a member without breaking existing implementers.
    //
    // This row is absent from the net48 project on purpose: the feature needs
    // runtime support that .NET Framework never gained, so it is a hard
    // compiler error there rather than a policy choice.
    public interface ILogger
    {
        void Log(string message);

        // Default implementation, expressed in terms of the abstract member.
        void LogWarning(string message)
        {
            Log("WARN: " + message);
        }

        // Interfaces may also declare static members with bodies.
        static string Prefix()
        {
            return "log";
        }
    }

    // Implements only the abstract member and inherits LogWarning.
    public class CollectingLogger : ILogger
    {
        private string _last = string.Empty;

        public string Last
        {
            get { return _last; }
        }

        public void Log(string message)
        {
            _last = message;
        }
    }

    // Overrides the default to show it is a default, not a seal.
    public class ShoutingLogger : ILogger
    {
        private string _last = string.Empty;

        public string Last
        {
            get { return _last; }
        }

        public void Log(string message)
        {
            _last = message;
        }

        public void LogWarning(string message)
        {
            _last = "!!! " + message;
        }
    }

    public class Usage
    {
        // The default member is only reachable through the interface, because
        // CollectingLogger does not declare it.
        public static string CallInherited()
        {
            ILogger logger = new CollectingLogger();
            logger.LogWarning("disk");
            return ((CollectingLogger)logger).Last;
        }
    }
}
