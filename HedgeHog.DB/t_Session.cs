//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace HedgeHog.DB
{
    using System;
    using System.Collections.Generic;
    
    public partial class t_Session
    {
        public System.Guid Uid { get; set; }
        public double MinimumGross { get; set; }
        public int MaximumLot { get; set; }
        public Nullable<double> Profitability { get; set; }
        public System.DateTime Timestamp { get; set; }
        public Nullable<System.Guid> SuperUid { get; set; }
        public Nullable<System.DateTime> DateMin { get; set; }
        public Nullable<System.DateTime> DateMax { get; set; }
        public Nullable<double> BallanceMax { get; set; }
    }
}