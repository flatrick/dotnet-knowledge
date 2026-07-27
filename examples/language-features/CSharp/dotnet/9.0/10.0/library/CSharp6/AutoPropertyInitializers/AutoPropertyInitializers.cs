using System.Collections.Generic;

namespace Net9_CSharp10_Library.CSharp6.AutoPropertyInitializers
{
    public class Config
    {
        // An auto-property may be given an initializer, which runs before the
        // constructor body just as a field initializer does.
        public string Name { get; set; } = "default";

        public int Retries { get; set; } = 3;

        // A getter-only auto-property has no setter at all. Before C# 6.0 the
        // nearest equivalent needed an explicit readonly backing field.
        public IReadOnlyList<string> Tags { get; } = new List<string>();

        // A getter-only auto-property may also be assigned from a constructor:
        // the compiler writes straight to the backing field.
        public string Id { get; }

        public Config(string id)
        {
            Id = id;
        }
    }
}
