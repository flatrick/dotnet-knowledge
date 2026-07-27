using System.Collections.Generic;

namespace Net10_CSharpLatest_Library.CSharp3.ObjectCollectionInitializers
{
    public class Point
    {
        private int _x;
        private int _y;

        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }
    }

    public class Rectangle
    {
        private readonly Point _topLeft = new Point();
        private string _label;

        // A nested initializer assigns into the existing instance rather than
        // replacing it, so this property needs no setter.
        public Point TopLeft
        {
            get { return _topLeft; }
        }

        public string Label
        {
            get { return _label; }
            set { _label = value; }
        }
    }

    public class Initializers
    {
        // Object initializer: the members are set after the constructor runs.
        public static Point CreatePoint()
        {
            return new Point { X = 3, Y = 4 };
        }

        // Nested initializer: TopLeft is mutated, not assigned.
        public static Rectangle CreateRectangle()
        {
            return new Rectangle { Label = "unit", TopLeft = { X = 0, Y = 1 } };
        }

        // Collection initializer: each element is handed to Add.
        public static List<int> CreateList()
        {
            return new List<int> { 1, 2, 3 };
        }

        // Dictionary's Add takes two arguments, so each element is a brace pair.
        public static Dictionary<string, int> CreateDictionary()
        {
            return new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };
        }
    }
}
