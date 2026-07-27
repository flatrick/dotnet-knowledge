namespace Net48_CSharp1_Library.CSharp1.ClassesStructsEnums
{
    public enum TrafficLight
    {
        Red = 0,
        Amber = 1,
        Green = 2
    }

    public struct Point
    {
        private readonly int _x;
        private readonly int _y;

        public Point(int x, int y)
        {
            _x = x;
            _y = y;
        }

        public int X
        {
            get { return _x; }
        }

        public int Y
        {
            get { return _y; }
        }
    }

    public class Signal
    {
        private readonly TrafficLight _state;
        private readonly Point _position;

        public Signal(TrafficLight state, Point position)
        {
            _state = state;
            _position = position;
        }

        public TrafficLight State
        {
            get { return _state; }
        }

        public Point Position
        {
            get { return _position; }
        }
    }
}
