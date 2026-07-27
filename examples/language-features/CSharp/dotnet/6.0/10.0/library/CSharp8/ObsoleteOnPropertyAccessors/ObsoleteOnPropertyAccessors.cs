using System;

namespace Net6_CSharp10.CSharp8.ObsoleteOnPropertyAccessors
{
    public class Settings
    {
        private string _theme = "light";

        // C# 8.0 allows Obsolete on an individual accessor rather than only on
        // the whole property, so a setter can be retired while the getter stays
        // supported. Reading Theme produces no diagnostic; assigning it warns.
        public string Theme
        {
            get { return _theme; }

            [Obsolete("Set the theme through ApplyTheme instead.")]
            set { _theme = value; }
        }

        // The supported replacement for the retired setter.
        public void ApplyTheme(string theme)
        {
            _theme = theme;
        }

        // Reading the property is unaffected by the attribute on the setter.
        public string CurrentTheme()
        {
            return Theme;
        }
    }
}
