namespace Net10_CSharp14_Library.CSharp9.CovariantReturnTypes
{
    public class Food
    {
    }

    public class Kibble : Food
    {
    }

    public abstract class Animal
    {
        public abstract Food GetFood();
    }

    // The override narrows the return type to a derived one. Before C# 9.0 an
    // override had to repeat the base signature exactly, so a caller holding
    // the derived type still had to cast.
    public class Dog : Animal
    {
        public override Kibble GetFood()
        {
            return new Kibble();
        }
    }

    public class Usage
    {
        // No cast is needed here, which is the benefit.
        public static Kibble FeedDog(Dog dog)
        {
            return dog.GetFood();
        }

        // Through the base type the member still has the base return type.
        public static Food FeedAnimal(Animal animal)
        {
            return animal.GetFood();
        }
    }
}
