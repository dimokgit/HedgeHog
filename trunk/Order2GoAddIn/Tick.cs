using System;
using System.Collections.Generic;
using System.Text;

namespace Order2GoAddIn
{
    /// <summary>
    /// The tick data
    /// </summary>
    public class Tick
    {
        /// <summary>
        /// Tick's date and time
        /// </summary>
        public DateTime DateTime
        {
            get
            {
                return mDateTime;
            }
        }
        DateTime mDateTime;

        /// <summary>
        /// Ask price
        /// </summary>
        public double Ask
        {
            get
            {
                return mAsk;
            }
        }
        double mAsk;

        /// <summary>
        /// Bid price
        /// </summary>
        public double Bid
        {
            get
            {
                return mBid;
            }
        }
        double mBid;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Tick(DateTime datetime, double ask, double bid)
        {
            mDateTime = datetime;
            mAsk = ask;
            mBid = bid;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public Tick(FXCore.MarketRateAut rate)
        {
            if (rate == null)
                throw new ArgumentNullException("rate");
            if (rate.Period != "t1")
                throw new ArgumentException("Rate shall be tick rate", "rate");

            mDateTime = rate.StartDate;
            mAsk = rate.AskOpen;
            mBid = rate.BidOpen;
        }


        /// <summary>
        /// Compares the tick with the FX Core's rate.
        /// </summary>
        public bool Equals(FXCore.MarketRateAut rate)
        {
            if (rate == null)
                throw new ArgumentNullException("rate");
            if (rate.Period != "t1")
                throw new ArgumentException("Rate shall be tick rate", "rate");

            int rc = mDateTime.CompareTo(rate.StartDate);
            return rc == 0 && mAsk == rate.AskOpen && mBid == rate.BidOpen;
        }
    }
}
