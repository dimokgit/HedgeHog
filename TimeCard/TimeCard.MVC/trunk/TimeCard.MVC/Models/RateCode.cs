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
    public partial class RateCode
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Rate { get; set; }
        public int TypeId { get; set; }
        public int LayerId { get; set; }
    
        public virtual RateCodeLayer RateCodeLayer { get; set; }
        public virtual RateCodeType RateCodeType { get; set; }
    }
    
}
