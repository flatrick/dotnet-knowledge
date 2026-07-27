using System;

namespace Net10_CSharp11_Library.CSharp11.GenericAttributes
{
    // An attribute class may now be generic. Before C# 11.0 a type had to be
    // passed as a System.Type argument, which lost compile-time checking.
    public class ValidatorAttribute<T> : Attribute
        where T : class
    {
        public Type ValidatedType
        {
            get { return typeof(T); }
        }
    }

    // The pre-C#11 shape, kept as contrast: the argument is just a Type, so
    // nothing constrains what may be passed.
    public class LegacyValidatorAttribute : Attribute
    {
        public LegacyValidatorAttribute(Type validatedType)
        {
            ValidatedType = validatedType;
        }

        public Type ValidatedType { get; }
    }

    public class Model
    {
    }

    [Validator<Model>]
    [LegacyValidator(typeof(Model))]
    public class Subject
    {
    }
}
