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
    public partial class vRateCodeByRange
    {
        public int Id { get; set; }
        public int HourStart { get; set; }
        public int HourStop { get; set; }
        public string RateCode { get; set; }
        public int RateCodeId { get; set; }
        public string RateCodeType { get; set; }
        public int RateCodeTypeId { get; set; }
        public string RateCodeLayer { get; set; }
        public int RateCodeLayerId { get; set; }
        public int RateCodeTypePriority { get; set; }
        public int RateCodeLayerPriority { get; set; }
    }
    
}
