using GalaSoft.MvvmLight;
using System;

namespace Manheim.ViewModel {
  /// <summary>
  /// This class contains properties that the main View can data bind to.
  /// <para>
  /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
  /// </para>
  /// <para>
  /// You can also use Blend to data bind with the tool's support.
  /// </para>
  /// <para>
  /// See http://www.galasoft.ch/mvvm/getstarted
  /// </para>
  /// </summary>
  public class MainViewModel : ViewModelBase {
    public string Welcome {
      get {
        return "Welcome to MVVM Light";
      }
    }
    /// <summary>
    /// The <see cref="Prop" /> property's name.
    /// </summary>
    public const string PropPropertyName = "Prop";

    private bool _prop = false;

    /// <summary>
    /// Gets the Prop property.
    /// TODO Update documentation:
    /// Changes to that property's value raise the PropertyChanged event. 
    /// This property's value is broadcasted by the Messenger's default instance when it changes.
    /// </summary>
    public bool Prop {
      get {
        return _prop;
      }

      set {
        if (_prop == value) {
          return;
        }

        var oldValue = _prop;
        _prop = value;

        // Update bindings, no broadcast
        RaisePropertyChanged(PropPropertyName);

        // Update bindings and broadcast change using GalaSoft.MvvmLight.Messenging
        RaisePropertyChanged(PropPropertyName, oldValue, value, true);
      }
    }
    /// <summary>
    /// Initializes a new instance of the MainViewModel class.
    /// </summary>
    public MainViewModel() {
      if (IsInDesignMode) {
        // Code runs in Blend --> create design time data.
      } else {
        // Code runs "for real"
      }
    }

    ////public override void Cleanup()
    ////{
    ////    // Clean up if needed

    ////    base.Cleanup();
    ////}
  }
}