using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Data;

namespace VMLocatorSample.ViewModels {
  public class ViewModelLocator {
    private static Dictionary<Type, Object> ViewModels;

    static ViewModelLocator() {
      ViewModels = new Dictionary<Type, Object>();
    }

    public object ViewModel {
      get { return GetViewModel(); }
    }

    private object GetViewModel() {
      Type viewType = GetViewType();
      object viewModel = null;

      if (!ViewModels.TryGetValue(viewType, out viewModel)) {
        var viewModelType = Type.GetType(viewType.FullName.Replace("View", "ViewModel"));
        viewModel = Activator.CreateInstance(viewModelType);
        ViewModels[viewType] = viewModel;
      }

      return viewModel;
    }

    private Type GetViewType() {
      var stack = new StackTrace();

      return stack.GetFrames()
          .Select(f => f.GetMethod())
          .Where(m => m.Name == "InitializeComponent")
          .Select(m => m.DeclaringType)
          .FirstOrDefault();
    }
  }
}
