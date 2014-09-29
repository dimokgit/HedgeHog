using System;
using System.ComponentModel;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  public interface IAddress : INotifyPropertyChanged
  {
    /// <summary>
    /// The street.
    /// </summary>
    string Street { get; set; }

    /// <summary>
    /// The city.
    /// </summary>
    string City { get; set; }
  }

  public class Address : IAddress
  {
    #region Street

    /// <summary>
    /// The street.
    /// </summary>
    internal string street = "";


    /// <summary>
    /// The street.
    /// </summary>
    public string Street
    {
      get { return street; }
      set
      {
        //ignore if values are equal
        if (value == street) return;

        street = value;
        OnPropertyChanged("Street");
      }
    }

    #endregion

    #region City

    /// <summary>
    /// The city.
    /// </summary>
    internal string city = "";


    /// <summary>
    /// The city.
    /// </summary>
    public string City
    {
      get { return city; }
      set
      {
        //ignore if values are equal
        if (value == city) return;

        city = value;
        OnPropertyChanged("City");
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

    ~Address()
    {
      FinalizeCounter++;
      //Console.Out.WriteLine("Finalizing address " + city);
    }

    public static int FinalizeCounter = 0;
  }
}