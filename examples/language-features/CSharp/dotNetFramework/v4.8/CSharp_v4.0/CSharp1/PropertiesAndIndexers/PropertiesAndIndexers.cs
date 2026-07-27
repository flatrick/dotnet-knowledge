namespace Net48_CSharp4_Library.CSharp1.PropertiesAndIndexers
{
    public class Matrix
    {
        private readonly double[,] _cells;
        private string _label;

        public Matrix(int rows, int columns)
        {
            _cells = new double[rows, columns];
            _label = string.Empty;
        }

        // A read/write property backed by an explicit field.
        public string Label
        {
            get { return _label; }
            set { _label = value; }
        }

        // A read-only property computed on each access.
        public int Rows
        {
            get { return _cells.GetLength(0); }
        }

        public int Columns
        {
            get { return _cells.GetLength(1); }
        }

        // A two-argument indexer.
        public double this[int row, int column]
        {
            get { return _cells[row, column]; }
            set { _cells[row, column] = value; }
        }
    }
}
