using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog;
using static HedgeHog.ReflectionCore;

namespace HedgeHog {
  public static class RxPropertyChange {
    public static IDisposable SubscribeToPropertiesChanged<T>(this T me, Action<T> fire,IScheduler scheduler, params Expression<Func<T, object>>[] props) where T : class, INotifyPropertyChanged {
      return Observable.Merge<T>(props.Select(p => me.ObservePropertyChanged(p)).ToArray())
        .Throttle(TimeSpan.FromSeconds(0.1))
              .ObserveOn(scheduler)
              .Subscribe(sr => fire(sr));
    }
    public static IDisposable SubscribeToPropertyChanged<TPropertySource>(
      this TPropertySource source
      , Expression<Func<TPropertySource, object>> property
      , Action<TPropertySource> onNext) where TPropertySource : class, INotifyPropertyChanged {
      var propertyName = GetLambda(property);
      var propertyDelegate = new Func<TPropertySource, object>(property.Compile());
      return (from e in Observable.FromEventPattern<PropertyChangedEventArgs>(source, "PropertyChanged")
              where e.EventArgs.PropertyName == propertyName
              select e.Sender as TPropertySource
              ).DistinctUntilChanged(propertyDelegate).Subscribe(onNext);
    }

    public static IObservable<TPropertySource> ObservePropertyChanged<TPropertySource>(
      this TPropertySource source, Expression<Func<TPropertySource, object>> property
      ) where TPropertySource : class, INotifyPropertyChanged {
      var propertyName = GetLambda(property);
      var propertyDelegate = new Func<TPropertySource, object>(property.Compile());
      return (from e in Observable.FromEventPattern<PropertyChangedEventArgs>(source, "PropertyChanged")
              where e.EventArgs.PropertyName == propertyName
              select e.Sender as TPropertySource
              ).DistinctUntilChanged(propertyDelegate);
    }
  }
}
