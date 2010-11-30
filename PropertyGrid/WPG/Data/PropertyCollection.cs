using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.ComponentModel;

namespace WPG.Data
{
    public class PropertyCollection : CompositeItem
    {
        #region Initialization

        public PropertyCollection() { }

        //public PropertyCollection(object instance)
        //    : this(instance, false)
        //{ }

        public PropertyCollection(object instance, bool noCategory, bool automaticlyExpandObjects, string filter)
        {
            Dictionary<string, PropertyCategory> groups = new Dictionary<string, PropertyCategory>();
            PropertyDescriptorCollection properties ;
            if (instance != null)
                properties = TypeDescriptor.GetProperties(instance.GetType());  //I changed here from instance to instance.GetType, so that only the Direct Properties are shown!
            else
                properties = new PropertyDescriptorCollection(new PropertyDescriptor[] { });

            List<Property> propertyCollection = new List<Property>();

            foreach (PropertyDescriptor propertyDescriptor in properties)
            {
                CollectProperties(instance, propertyDescriptor, propertyCollection, automaticlyExpandObjects, filter);
                if (noCategory)
                    propertyCollection.Sort(Property.CompareByName);
                else
                    propertyCollection.Sort(Property.CompareByCategoryThenByName);
            }
            
            if (noCategory)
            {

                foreach (Property property in propertyCollection)
                {
                    if (filter == "" || property.Name.ToLower().Contains(filter))
                        Items.Add(property);
                }
            }
            else
            {
                foreach (Property property in propertyCollection)
                {
                    if (filter == "" || property.Name.ToLower().Contains(filter))
                    {
                        PropertyCategory propertyCategory;
                        if (groups.ContainsKey(property.Category))
                        {
                            propertyCategory = groups[property.Category];
                        }
                        else
                        {
                            propertyCategory = new PropertyCategory(property.Category);
                            groups[property.Category] = propertyCategory;
                            Items.Add(propertyCategory);
                        }
                        propertyCategory.Items.Add(property);
                    }
                }
            }
        }

        private void CollectProperties(object instance, PropertyDescriptor descriptor, List<Property> propertyCollection, bool automaticlyExpandObjects, string filter)
        {
            
            if (descriptor.Attributes[typeof(FlatAttribute)] == null)
            {
                Property property = new Property(instance, descriptor);
                if (descriptor.IsBrowsable)
                {
                    //Add a property with Name: AutomaticlyExpandObjects
                    Type propertyType = descriptor.PropertyType;
                    if (automaticlyExpandObjects && propertyType.IsClass && !propertyType.IsArray && propertyType != typeof(string))
                    {
                        propertyCollection.Add(new ExpandableProperty(instance, descriptor, automaticlyExpandObjects, filter));
                    }
                    else if (descriptor.Converter.GetType() == typeof(ExpandableObjectConverter))
                    {
                        propertyCollection.Add(new ExpandableProperty(instance, descriptor, automaticlyExpandObjects, filter));
                    }
                    else
                        propertyCollection.Add(property);
                }
            }
            else
            {
                instance = descriptor.GetValue(instance);
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(instance);
                foreach (PropertyDescriptor propertyDescriptor in properties)
                {
                    CollectProperties(instance, propertyDescriptor, propertyCollection, automaticlyExpandObjects, filter);
                }
            }
        }

        #endregion
    }
}
