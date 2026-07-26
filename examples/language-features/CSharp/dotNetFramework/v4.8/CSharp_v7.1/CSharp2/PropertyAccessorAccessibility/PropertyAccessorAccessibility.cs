namespace CSharpFw48Cs73.CSharp2.PropertyAccessorAccessibility
{
    public class Document
    {
        private string _title;
        private int _revision;

        public Document(string title)
        {
            _title = title;
            _revision = 1;
        }

        // The accessor may be more restrictive than the property itself:
        // readable everywhere, writable only inside this class.
        public string Title
        {
            get { return _title; }
            private set { _title = value; }
        }

        // Writable by this class and its derived classes only.
        public int Revision
        {
            get { return _revision; }
            protected set { _revision = value; }
        }

        public void Rename(string title)
        {
            Title = title;
            Revision++;
        }
    }

    public class VersionedDocument : Document
    {
        public VersionedDocument(string title)
            : base(title)
        {
        }

        // Reaching the protected setter from a derived class.
        public void Republish()
        {
            Revision = Revision + 10;
        }
    }
}
