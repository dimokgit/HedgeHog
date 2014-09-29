using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  public class School : INotifyPropertyChanged
  {
    #region Name

    /// <summary>
    /// The school name.
    /// </summary>
    internal string schoolName = string.Empty;


    /// <summary>
    /// The school name.
    /// </summary>
    public string SchoolName
    {
      get { return schoolName; }
      set
      {
        //ignore if values are equal
        if (value == schoolName) return;

        schoolName = value;
        OnPropertyChanged("SchoolName");
      }
    }

    #endregion

    #region Address

    /// <summary>
    /// The person's address.
    /// </summary>
    internal IAddress address;


    /// <summary>
    /// The person's address.
    /// </summary>
    public IAddress Address
    {
      get { return address; }
      set
      {
        address = value;
        OnPropertyChanged("Address");
      }
    }

    #endregion

    #region SchoolId

    /// <summary>
    /// Some ID.
    /// </summary>
    internal int schoolid;


    /// <summary>
    /// Some ID.
    /// </summary>
    public int SchoolId
    {
      get { return schoolid; }
      set
      {
        schoolid = value;
        OnPropertyChanged("SchoolId");
      }
    }

    #endregion

    #region INotifyPropertyChanged event

    ///<summary>
    ///Occurs when a property value changes.
    ///</summary>
    public event PropertyChangedEventHandler PropertyChanged;


    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for
    /// a given property.
    /// </summary>
    /// <param name="propertyName">The name of the changed property.</param>
    protected void OnPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }

    #endregion

    ~School()
    {
      FinalizeCounter++;
    }

    public static int FinalizeCounter = 0;
  }
}
