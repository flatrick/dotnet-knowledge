using System;

namespace Net5_CSharp10.CSharp1.EventsAndDelegates
{
    public delegate void ThresholdReachedHandler(object sender, ThresholdReachedEventArgs e);

    public class ThresholdReachedEventArgs : EventArgs
    {
        private readonly int _threshold;

        public ThresholdReachedEventArgs(int threshold)
        {
            _threshold = threshold;
        }

        public int Threshold
        {
            get { return _threshold; }
        }
    }

    public class Counter
    {
        private readonly int _threshold;
        private int _total;

        public Counter(int threshold)
        {
            _threshold = threshold;
        }

        public event ThresholdReachedHandler ThresholdReached;

        public int Total
        {
            get { return _total; }
        }

        public void Add(int amount)
        {
            _total += amount;
            if (_total >= _threshold)
            {
                OnThresholdReached(new ThresholdReachedEventArgs(_threshold));
            }
        }

        protected virtual void OnThresholdReached(ThresholdReachedEventArgs e)
        {
            ThresholdReachedHandler handler = ThresholdReached;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}
