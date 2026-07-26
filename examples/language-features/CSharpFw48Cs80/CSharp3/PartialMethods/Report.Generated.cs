namespace CSharpFw48Cs80.CSharp3.PartialMethods
{
    // The declaring half — the shape a code generator would emit. A C# 3.0
    // partial method is implicitly private, must return void, and cannot have
    // out parameters.
    public partial class Report
    {
        private int _lineCount;

        public int LineCount
        {
            get { return _lineCount; }
        }

        public void AddLine()
        {
            _lineCount++;
            OnLineAdded(_lineCount);
        }

        partial void OnLineAdded(int lineNumber);

        // Nothing implements this one, so the compiler erases the declaration
        // and every call to it — including the call in Close below.
        partial void OnReportClosed();

        public void Close()
        {
            OnReportClosed();
        }
    }
}
