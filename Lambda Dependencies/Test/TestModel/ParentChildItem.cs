using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Hardcodet.Util.Dependencies.Testing.TestModel
{
  /// <summary>
  /// An item that has circular dependencies to itself.
  /// </summary>
  public class ParentChildItem : INotifyPropertyChanged
  {
    private DependencyNode parentToSelfDependency;
    private DependencyNode childToSelfDependency;

    #region Name

    private string name = "";


    /// <summary>
    /// The item's name.
    /// </summary>
    public string Name
    {
      get { return name; }
      set
      {
        //ignore if values are equal
        if (value == name) return;

        name = value;
        OnPropertyChanged("Name");
      }
    }

    #endregion

    #region Parent

    /// <summary>
    /// The parent, if any.
    /// </summary>
    private ParentChildItem parent;


    /// <summary>
    /// The parent, if any.
    /// </summary>
    public ParentChildItem Parent
    {
      get { return parent; }
      set
      {
        parent = value;
        OnPropertyChanged("Parent");
      }
    }

    #endregion

    #region Child

    /// <summary>
    /// The child item, if any.
    /// </summary>
    private ParentChildItem child;


    /// <summary>
    /// The child item, if any.
    /// </summary>
    public ParentChildItem Child
    {
      get { return child; }
      set
      {
        child = value;
        OnPropertyChanged("Child");
      }
    }

    #endregion

    public ParentChildItem(string name)
    {
      Name = name;
      var pts = DependencyNode.Create(() => Parent.Child.Name);
      pts.DependencyChanged += OnParentDependencyChanged;
      parentToSelfDependency = pts;

      var cts = DependencyNode.Create(() => Child.Parent.Name);
      cts.DependencyChanged += OnChildDependencyChanged;
      childToSelfDependency = cts;
    }

    private void OnChildDependencyChanged(object sender, DependencyChangeEventArgs<string> e)
    {
      Console.Out.WriteLine("{0}: Changed child dependency with value {1}", name, e.TryGetLeafValue());
    }

    private void OnParentDependencyChanged(object sender, DependencyChangeEventArgs<string> e)
    {
      Console.Out.WriteLine("{0}: Changed parent dependency with value {1}", name, e.TryGetLeafValue());
    }

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
      //validate the property name in debug builds
      VerifyProperty(propertyName);

      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }


    /// <summary>
    /// Verifies whether the current class provides a property with a given
    /// name. This method is only invoked in debug builds, and results in
    /// a runtime exception if the <see cref="OnPropertyChanged"/> method
    /// is being invoked with an invalid property name. This may happen if
    /// a property's name was changed but not the parameter of the property's
    /// invocation of <see cref="OnPropertyChanged"/>.
    /// </summary>
    /// <param name="propertyName">The name of the changed property.</param>
    [Conditional("DEBUG")]
    private void VerifyProperty(string propertyName)
    {
      Type type = GetType();

      //look for a *public* property with the specified name
      PropertyInfo pi = type.GetProperty(propertyName);
      if (pi == null)
      {
        //there is no matching property - notify the developer
        string msg = "OnPropertyChanged was invoked with invalid property name {0}: ";
        msg += "{0} is not a public property of {1}.";
        msg = String.Format(msg, propertyName, type.FullName);
        Debug.Fail(msg);
      }
    }

    #endregion


    ~ParentChildItem()
    {
      FinalizeCounter++;
      Console.Out.WriteLine("Finalizing ParentChildItem " + name);
    }

    public static int FinalizeCounter = 0;


    public override string ToString()
    {
      return "ITEM:" + name;
    }
  }
}