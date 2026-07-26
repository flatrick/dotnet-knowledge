namespace CSharpNet10Latest.CSharp14.NullConditionalAssignment
{
    public class Settings
    {
        public string Theme { get; set; }

        public int Retries { get; set; }
    }

    public class Assignment
    {
        // ?. may now appear on the LEFT of an assignment. When the receiver is
        // null the whole assignment is skipped and the right side is never
        // evaluated — so the null check no longer needs its own if statement.
        public static void SetTheme(Settings settings, string theme)
        {
            settings?.Theme = theme;
        }

        // Compound assignment works the same way.
        public static void AddRetry(Settings settings)
        {
            settings?.Retries += 1;
        }

        // The pre-C#14 form, kept as contrast.
        public static void SetThemeClassic(Settings settings, string theme)
        {
            if (settings != null)
            {
                settings.Theme = theme;
            }
        }

        // A null receiver is simply a no-op rather than a NullReferenceException.
        public static bool NullReceiverIsSafe()
        {
            Settings none = null;
            SetTheme(none, "dark");
            return true;
        }
    }
}
