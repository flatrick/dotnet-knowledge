namespace CSharpFw48Cs73.CSharp3.PartialMethods
{
    // The implementing half — the hand-written file. Supplying a body here is
    // what makes the calls in Report.Generated.cs real.
    public partial class Report
    {
        private int _lastLineNumber;

        public int LastLineNumber
        {
            get { return _lastLineNumber; }
        }

        partial void OnLineAdded(int lineNumber)
        {
            _lastLineNumber = lineNumber;
        }
    }
}
