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
    public partial class vPunchPair
    {
        public string TypeIn { get; set; }
        public System.DateTimeOffset Start { get; set; }
        public string TypeOut { get; set; }
        public System.DateTimeOffset Stop { get; set; }
        public Nullable<int> TotalMinutes { get; set; }
    }
    
}