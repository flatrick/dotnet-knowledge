using System;

namespace Net5_CSharp10.CSharp2.Variance
{
    public class Shape
    {
    }

    public class Circle : Shape
    {
    }

    public delegate Shape ShapeFactory();

    public delegate void CircleHandler(Circle circle);

    public class DelegateVariance
    {
        // Covariance: a method returning the more derived Circle binds to a
        // delegate declared to return Shape.
        public static ShapeFactory Covariant()
        {
            return CreateCircle;
        }

        // Contravariance: a method accepting the less derived Shape binds to a
        // delegate declared to accept Circle.
        public static CircleHandler Contravariant()
        {
            return HandleShape;
        }

        // Array covariance predates generics: a Circle[] is usable as a Shape[].
        public static Shape[] ArrayCovariance()
        {
            Circle[] circles = new Circle[] { new Circle(), new Circle() };
            Shape[] shapes = circles;
            return shapes;
        }

        private static Circle CreateCircle()
        {
            return new Circle();
        }

        private static void HandleShape(Shape shape)
        {
            if (shape == null)
            {
                throw new ArgumentNullException("shape");
            }
        }
    }
}
