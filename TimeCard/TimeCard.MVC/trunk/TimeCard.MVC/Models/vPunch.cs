//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace TimeCard.MVC.Models
{
    public partial class vPunch
    {
        public int Id { get; set; }
        public System.DateTimeOffset Time { get; set; }
        public string Direction { get; set; }
        public int DirectionId { get; set; }
        public string Type { get; set; }
        public int TypeId { get; set; }
        public bool IsOutOfSequence { get; set; }
        public Nullable<System.DateTimeOffset> PairStart { get; set; }
    }
    
}
