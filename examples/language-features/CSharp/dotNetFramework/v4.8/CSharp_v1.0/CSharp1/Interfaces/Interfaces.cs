using System;

namespace Net48_CSharp1_Library.CSharp1.Interfaces
{
    public interface IShape
    {
        double Area();
    }

    public interface INamed
    {
        string Name { get; }
    }

    public class Circle : IShape, INamed
    {
        private readonly double _radius;

        public Circle(double radius)
        {
            _radius = radius;
        }

        public double Area()
        {
            return Math.PI * _radius * _radius;
        }

        public string Name
        {
            get { return "Circle"; }
        }
    }

    public class Square : IShape
    {
        private readonly double _side;

        public Square(double side)
        {
            _side = side;
        }

        // Explicit interface implementation: reachable only through an IShape reference.
        double IShape.Area()
        {
            return _side * _side;
        }
    }
}
