using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HedgeHog;
using HedgeHog.DB;
using HedgeHog.Models;
using HedgeHog.NewsCaster;

namespace HedgeHog.UI {
  /// <summary>
  /// Interaction logic for UserControl1.xaml
  /// </summary>
  public partial class NewsCasterControl : Models.UserControlModel {
    public class FetchNewsException : Exception {
      public FetchNewsException(object message, Exception inner = null) : base(message + "", inner) { }
    }
    #region Methods
    Color GetEventColor(DateTimeOffset time) {
      if (time.Between(DateTimeOffset.Now.AddHours(3), DateTimeOffset.Now.AddMinutes(15))) return Colors.Yellow;
      if (time.Between(DateTimeOffset.Now.AddMinutes(15), DateTimeOffset.Now.AddMinutes(-15))) return Colors.LimeGreen;
      if (time.Between(DateTimeOffset.Now.AddMinutes(-15), DateTimeOffset.Now.AddMinutes(-2))) return Colors.GreenYellow;
      return Colors.Wheat;
    }
    private void UpdateNewsColor() {
      News.ForEach(evt => {
        evt.Color = GetEventColor(evt.Event.Time) + "";
        evt.IsToday = evt.Event.Time.Date == DateTimeOffset.Now.Date;
        evt.DidHappen = evt.Event.Time < DateTimeOffset.Now.AddMinutes(-30);
      });
      if (News.Count == 0)
        FetchNews();
      else if (News.Max(evt => evt.Event.Time).Date == DateTime.Now.Date)
        FetchNews(DateTime.Now.Date.AddDays(1));
    }
    private void FetchNews(DateTime? date = null) {
      if (newsObserver != null) return;
      try {
        var dateStart = DateTime.Now.AddDays(-7).Round(MathExtensions.RoundTo.Week).AddDays(1);// DateTime.Parse("1/2/2012");
        var dates = (from d in
                       (from i in Enumerable.Range(0, 10000)
                        select dateStart.AddDays(i * 7)
                         )
                     where d <= DateTime.Now.Date
                     select d).ToArray();

        newsObserver = HedgeHog.NewsCaster.NewsHound.EconoDay.Fetch(dates)
        .ObserveOnDispatcher()
        .Subscribe(ProcessNews,
        exc => {
          newsObserver = null;
          Log = new FetchNewsException("", exc);
        }, () => {
          newsObserver = null;
          Log = new FetchNewsException("Done.");
        });
      } catch (Exception exc) {
        newsObserver = null;
        Log = new FetchNewsException("", exc);
      }
    }

    private void ProcessNews(IEnumerable<NewsEvent> events) {
      newsObserver = null;
      var newNews = events.Select(evt => new NewsContainer(evt))
        .Except(News, new LambdaComparer<NewsContainer>((l, r) => l.Event.Name == r.Event.Name && l.Event.Time == r.Event.Time));
      ForexStorage.UseForexContext(c => {
        var dateLast = c.Event__News.Max(e => e.Time);
        newNews.Select(evt => evt.Event)
          .Where(evt => evt.Time > dateLast)
          .ForEach(evt => c.Event__News.AddObject(new Event__News() {
            Level = (evt.Level + "").Substring(0, 1),
            Country = evt.Country,
            Name = evt.Name,
            Time = evt.Time
          }));
      }, c => c.SaveChanges()
      );
      newNews.ForEach(evt => News.Add(evt));
      NewsView.GroupDescriptions.Clear();
      NewsView.GroupDescriptions.Add(new PropertyGroupDescription("Date"));
      UpdateNewsColor();
    }
    #endregion

    #region Properties
    Exception Log {
      set {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(value);
      }
    }
    IDisposable newsObserver;
    Predicate<object> _hideNews = evt => ((NewsContainer)evt).Event.Time.Between(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));

    #region DoShowAll
    private bool _DoShowAll = true;
    public bool DoShowAll {
      get { return _DoShowAll; }
      set {
        if (_DoShowAll != value) {
          _DoShowAll = value;
          OnPropertyChanged("DoShowAll");
          NewsView.Filter = DoShowAll ? null : _hideNews;
        }
      }
    }

    #endregion

    public class NewsContainer : ModelBase {
      public NewsEvent Event { get; set; }
      #region DidHappen
      private bool _DidHappen;
      public bool DidHappen {
        get { return _DidHappen; }
        set {
          if (_DidHappen != value) {
            _DidHappen = value;
            RaisePropertyChanged("DidHappen");
          }
        }
      }

      #endregion
      #region IsToday
      private bool _IsToday;
      public bool IsToday {
        get { return _IsToday; }
        set {
          if (_IsToday != value) {
            _IsToday = value;
            RaisePropertyChanged("IsToday");
          }
        }
      }

      #endregion
      #region Color
      private string _Color = "White";
      public string Color {
        get { return _Color; }
        set {
          if (_Color == value) return;
          var oldValue = _Color;
          _Color = value;
          RaisePropertyChanged(() => Color);
        }
      }
      #endregion
      #region Date
      private DateTime _Date;
      public DateTime Date {
        get { return _Date; }
        set {
          if (_Date != value) {
            _Date = value;
            RaisePropertyChanged("Date");
          }
        }
      }

      #endregion
      public NewsContainer(NewsEvent newsEvent) {
        this.Event = newsEvent;
        this.Date = newsEvent.Time.Date;
      }
    }

    ObservableCollection<NewsContainer> _news = new ObservableCollection<NewsContainer>();
    public ObservableCollection<NewsContainer> News {
      get { return _news; }
      private set { _news = value; }
    }
    ListCollectionView _newsView;
    public ICollectionView NewsView { get { return CollectionViewSource.GetDefaultView(_news); } }
    #endregion

    public NewsCasterControl() {
      InitializeComponent();
      _newsView = new ListCollectionView(_news);
      Loaded += new RoutedEventHandler(NewsCasterControl_Loaded);
    }

    void NewsCasterControl_Loaded(object sender, RoutedEventArgs e) {
      DispatcherScheduler.Current.Schedule(0.FromMinutes(), a => {
        UpdateNewsColor();
        a(1.FromMinutes());
      });
    }
  }
}
