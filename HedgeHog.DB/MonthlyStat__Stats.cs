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
    
    public partial class MonthlyStat__Stats
    {
        public int Month { get; set; }
        public string Pair { get; set; }
        public int Period { get; set; }
        public double StDevAvg { get; set; }
        public double StDevStDev { get; set; }
        public int Count { get; set; }
        public System.DateTime Date { get; set; }
    }
}
