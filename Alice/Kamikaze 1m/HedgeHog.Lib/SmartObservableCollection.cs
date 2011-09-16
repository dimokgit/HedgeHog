using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace HedgeHog.Collections {
  public class SmartObservableCollection<T> : ObservableCollection<T> {
    public SmartObservableCollection()
      : base() {
      _suspendCollectionChangeNotification = false;
    }


    bool _suspendCollectionChangeNotification;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) {
      if (!_suspendCollectionChangeNotification) {
        base.OnCollectionChanged(e);
      }
    }

    public void SuspendCollectionChangeNotification() {
      _suspendCollectionChangeNotification = true;
    }

    public void ResumeCollectionChangeNotification() {
      _suspendCollectionChangeNotification = false;
    }


    public void AddRange(IEnumerable<T> items) {
      this.SuspendCollectionChangeNotification();
      int index = base.Count;
      try {
        foreach (var i in items) {
          base.InsertItem(base.Count, i);
        }
      } finally {
        this.ResumeCollectionChangeNotification();
        var arg = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
        this.OnCollectionChanged(arg);
      }
    }

  }

}
