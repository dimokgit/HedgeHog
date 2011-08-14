//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Manheim.Model
{
    public partial class Seller
    {
        #region Primitive Properties
    
        public virtual int SellerId
        {
            get;
            set;
        }
    
        public virtual string Name
        {
            get;
            set;
        }

        #endregion
        #region Navigation Properties
    
        public virtual ICollection<PreSale> PreSales
        {
            get
            {
                if (_preSales == null)
                {
                    var newCollection = new FixupCollection<PreSale>();
                    newCollection.CollectionChanged += FixupPreSales;
                    _preSales = newCollection;
                }
                return _preSales;
            }
            set
            {
                if (!ReferenceEquals(_preSales, value))
                {
                    var previousValue = _preSales as FixupCollection<PreSale>;
                    if (previousValue != null)
                    {
                        previousValue.CollectionChanged -= FixupPreSales;
                    }
                    _preSales = value;
                    var newValue = value as FixupCollection<PreSale>;
                    if (newValue != null)
                    {
                        newValue.CollectionChanged += FixupPreSales;
                    }
                }
            }
        }
        private ICollection<PreSale> _preSales;

        #endregion
        #region Association Fixup
    
        private void FixupPreSales(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (PreSale item in e.NewItems)
                {
                    item.Seller = this;
                }
            }
    
            if (e.OldItems != null)
            {
                foreach (PreSale item in e.OldItems)
                {
                    if (ReferenceEquals(item.Seller, this))
                    {
                        item.Seller = null;
                    }
                }
            }
        }

        #endregion
    }
}
