﻿//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Data.EntityClient;
using System.Data.Objects;
using System.Data.Objects.DataClasses;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;

[assembly: EdmSchemaAttribute()]
namespace HedgeHog.Alice.Store.Model
{
    #region Contexts
    
    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    public partial class AliceLDBEntities : ObjectContext
    {
        #region Constructors
    
        /// <summary>
        /// Initializes a new AliceLDBEntities object using the connection string found in the 'AliceLDBEntities' section of the application configuration file.
        /// </summary>
        public AliceLDBEntities() : base("name=AliceLDBEntities", "AliceLDBEntities")
        {
            this.ContextOptions.LazyLoadingEnabled = true;
            OnContextCreated();
        }
    
        /// <summary>
        /// Initialize a new AliceLDBEntities object.
        /// </summary>
        public AliceLDBEntities(string connectionString) : base(connectionString, "AliceLDBEntities")
        {
            this.ContextOptions.LazyLoadingEnabled = true;
            OnContextCreated();
        }
    
        /// <summary>
        /// Initialize a new AliceLDBEntities object.
        /// </summary>
        public AliceLDBEntities(EntityConnection connection) : base(connection, "AliceLDBEntities")
        {
            this.ContextOptions.LazyLoadingEnabled = true;
            OnContextCreated();
        }
    
        #endregion
    
        #region Partial Methods
    
        partial void OnContextCreated();
    
        #endregion
    
    }

    #endregion

    
}