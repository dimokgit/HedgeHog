using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Reactive.Linq;
using System.Windows;

namespace HedgeHog {
  public static class UIExtensions {
    public static IDisposable SubscribeToPlotterPreviewMouseLeftClick<TElement>(this TElement element
      , Func<System.Reactive.EventPattern<MouseButtonEventArgs>
      , bool> where
      , Action onNext
      , int moveTolerance = 4) where TElement : UIElement {
      var mouseDown = Observable.FromEventPattern<MouseButtonEventArgs>(element, "PreviewMouseLeftButtonDown")
        .Where(where)
        .Select(me => { return 0; });
      var mouseMove = Observable.FromEventPattern<MouseEventArgs>(element, "PreviewMouseMove")
        .Select(me => { return 1; });
      var mouseUp = Observable.FromEventPattern<MouseButtonEventArgs>(element, "PreviewMouseLeftButtonUp")
        .Where(where)
        .Select(me => { return 0; });
      var mo = from md in mouseDown
               from mm in mouseMove.StartWith(0)
               .Timeout(0.5.FromSeconds())
               .OnErrorResumeNext(Observable.Repeat(1, moveTolerance + 1))
               .Buffer(mouseUp)
               select mm;
      return mo
        .Subscribe(ii => {
          if (ii.Count(i => i == 1) <= moveTolerance)
            onNext();
        }, exc => { GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(exc); }, () => { }); 
    }
  }
}
