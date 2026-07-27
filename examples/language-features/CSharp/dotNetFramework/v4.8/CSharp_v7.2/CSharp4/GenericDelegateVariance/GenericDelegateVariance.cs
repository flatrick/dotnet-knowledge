using System.Collections.Generic;

namespace Net48_CSharp7_2_Library.CSharp4.GenericDelegateVariance
{
    public class Animal
    {
    }

    public class Dog : Animal
    {
    }

    // out marks T covariant: it may appear only in output positions.
    public interface IProducer<out T>
    {
        T Produce();
    }

    // in marks T contravariant: it may appear only in input positions.
    public interface IConsumer<in T>
    {
        bool Accepts(T item);
    }

    public delegate T Factory<out T>();

    public delegate void Handler<in T>(T item);

    public class DogProducer : IProducer<Dog>
    {
        public Dog Produce()
        {
            return new Dog();
        }
    }

    public class AnimalConsumer : IConsumer<Animal>
    {
        public bool Accepts(Animal item)
        {
            return item != null;
        }
    }

    public class VarianceSamples
    {
        private static int _handledCount;

        public static int HandledCount
        {
            get { return _handledCount; }
        }

        // Covariance: an IProducer<Dog> is usable where IProducer<Animal> is
        // expected, because T only ever comes out.
        public static IProducer<Animal> Covariant()
        {
            IProducer<Dog> dogs = new DogProducer();
            return dogs;
        }

        // Contravariance: an IConsumer<Animal> is usable where IConsumer<Dog>
        // is expected, because T only ever goes in.
        public static IConsumer<Dog> Contravariant()
        {
            IConsumer<Animal> animals = new AnimalConsumer();
            return animals;
        }

        // The BCL carries the same annotations — IEnumerable<out T> is what
        // makes this assignment legal.
        public static IEnumerable<Animal> BclCovariance()
        {
            List<Dog> dogs = new List<Dog>();
            dogs.Add(new Dog());
            return dogs;
        }

        public static Factory<Animal> CovariantDelegate()
        {
            Factory<Dog> factory = () => new Dog();
            return factory;
        }

        public static Handler<Dog> ContravariantDelegate()
        {
            Handler<Animal> handler = item =>
            {
                if (item != null)
                {
                    _handledCount++;
                }
            };
            return handler;
        }
    }
}
