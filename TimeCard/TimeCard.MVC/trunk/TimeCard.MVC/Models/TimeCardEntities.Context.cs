﻿//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;

namespace TimeCard.MVC.Models
{
    public partial class TimeCardEntitiesContainer : DbContext
    {
        public TimeCardEntitiesContainer()
            : base("name=TimeCardEntitiesContainer")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public DbSet<Punch> Punches { get; set; }
        public DbSet<PunchDirection> PunchDirections { get; set; }
        public DbSet<PunchType> PunchTypes { get; set; }
    }
}
