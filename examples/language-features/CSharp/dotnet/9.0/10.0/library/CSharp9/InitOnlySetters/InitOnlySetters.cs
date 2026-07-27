namespace Net9_CSharp10_Library.CSharp9.InitOnlySetters
{
    public class Settings
    {
        // An init accessor may be set in an object initializer but not
        // afterwards, which makes a property settable at construction and
        // immutable from then on. Before C# 9.0 that required a constructor
        // parameter for every such member.
        public string Theme { get; init; }

        public int Retries { get; init; }

        // init works with a backing field and validation too.
        private readonly string _region;

        public string Region
        {
            get { return _region; }
            init { _region = value ?? "global"; }
        }
    }

    public class Usage
    {
        // Legal: the object initializer runs during construction.
        public static Settings Create()
        {
            return new Settings { Theme = "dark", Retries = 3, Region = null };
        }

        // Assigning Theme here would be a compile error, which is the whole
        // point — the object is immutable once the initializer finishes.
        public static string ReadOnly(Settings settings)
        {
            return settings.Theme + settings.Region;
        }
    }
}
