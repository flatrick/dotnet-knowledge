namespace Net6_CSharp10.CSharp9.TargetTypedConditionalExpressions
{
    public class Animal
    {
    }

    public class Cat : Animal
    {
    }

    public class Dog : Animal
    {
    }

    public class TargetTyped
    {
        // Before C# 9.0 a conditional needed a common type between its two
        // branches. Cat and Dog have no conversion to one another, so this was
        // an error even though both convert to the target type.
        public static Animal PickAnimal(bool first)
        {
            return first ? new Cat() : new Dog();
        }

        // The same rule applies to nullable value types, where neither branch
        // is int? on its own.
        public static int? PickNullable(bool first, int value)
        {
            return first ? value : null;
        }

        // Combined with a target-typed new, neither branch names its type.
        public static Animal PickTargetTypedNew(bool first)
        {
            Animal animal = first ? new Cat() : new Dog();
            return animal;
        }
    }
}
