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
using System.ComponentModel.Composition;
using System.Collections.Specialized;
using ReactiveUI;
using System.Diagnostics;

namespace HedgeHog.UI {
  public partial class NewsCasterControl : UserControl {
    [Export]
    public NewsCasterModel NewsCasterModel { get { return NewsCasterModel.Default; } }
    public NewsCasterControl() {
      InitializeComponent();
    }
  }
  public class NewsCasterModel : Models.ModelBase {
    private static readonly NewsCasterModel defaultInstance = new NewsCasterModel();
    public static NewsCasterModel Default { get { return defaultInstance; } }
    public class FetchNewsException : Exception {
      public FetchNewsException(object message, Exception inner = null) : base(message + "", inner) { }
    }
    #region Methods
    Color GetEventColor(DateTimeOffset time) {
      if (time.Between(DateTimeOffset.Now.AddHours(2), DateTimeOffset.Now.AddMinutes(15))) return Colors.Yellow;
      if (time.Between(DateTimeOffset.Now.AddMinutes(15), DateTimeOffset.Now.AddMinutes(-15))) return Colors.LimeGreen;
      if (DateTimeOffset.Now.Between(time.AddMinutes(15), time.AddMinutes(45))) return Colors.GreenYellow;
      return Colors.Transparent;
    }
    private void UpdateNewsColor() {
      News.ToArray().ForEach(evt => {
        evt.Color = GetEventColor(evt.Event.Time) + "";
        evt.IsToday = evt.Event.Time.Date == DateTimeOffset.Now.Date;
        evt.DidHappen = evt.Event.Time < DateTimeOffset.Now.AddMinutes(-30);
        evt.Countdown = (evt.Event.Time - DateTimeOffset.Now).TotalMinutes.ToInt();
      });
    }
    private void FetchNews(DateTime? date = null) {
      if (newsObserver != null) return;
      try {
        //ProcessNews(NewsHound.MyFxBook.Fetch());
        var dateStart = DateTime.Now.AddDays(-7).Round(MathExtensions.RoundTo.Week).AddDays(1);// DateTime.Parse("1/2/2012");
        var dates = Enumerable.Range(0, 10000).Select(i => dateStart.AddDays(i * 7))
          .TakeWhile(d => d <= DateTime.Now.Date.AddDays(1)).ToArray();
        newsObserver = HedgeHog.NewsCaster.NewsHound.EconoDay.Fetch(dates)
        //.ObserveOn(GalaSoft.MvvmLight.Threading.DispatcherHelper.UIDispatcher)
        .Subscribe(ProcessNews,
        exc => {
          newsObserver = null;
          Log = new FetchNewsException("", exc);
        }, () => {
          newsObserver = null;
          Log = new FetchNewsException("News Done.");
        });
      } catch (Exception exc) {
        newsObserver = null;
        Log = new FetchNewsException("", exc);
      }
    }

    private void ProcessNews(IEnumerable<NewsEvent> events) {
      newsObserver = null;
      var newNews = events.Select(evt => new NewsContainer(evt))
        .Except(News, new LambdaComparer<NewsContainer>((l, r) => l.Event.Level == r.Event.Level && l.Event.Name == r.Event.Name && l.Event.Time == r.Event.Time));
      ForexStorage.UseForexContext(c => {
        var dateLast = c.Event__News.Max(e => e.Time);
        newNews.ToList().Select(evt => evt.Event)
          //.Where(evt => evt.Time > dateLast)
          .ForEach(evt => {
            c.Event__News.Add(new Event__News() {
              Level = (evt.Level + "").Substring(0, 1),
              Country = evt.Country,
              Name = evt.Name,
              Time = evt.Time
            });
            c.SaveConcurrent();
          });
      }, (c, exc) => Log = exc);
      ReactiveUI.RxApp.MainThreadScheduler.Schedule(() => {
        newNews.ForEach(evt => News.Add(evt));
        NewsView.GroupDescriptions.Clear();
        NewsView.GroupDescriptions.Add(new PropertyGroupDescription("Date"));
        NewsView.Refresh();
        UpdateNewsColor();
      });
    }
    #endregion

    #region Properties

    #region ShowNewsEventSnapshot
    ICommand _ShowNewsEventSnapshotCommand;
    public ICommand ShowNewsEventSnapshotCommand {
      get {
        if (_ShowNewsEventSnapshotCommand == null) {
          _ShowNewsEventSnapshotCommand = new GalaSoft.MvvmLight.Command.RelayCommand<NewsContainer>(ShowNewsEventSnapshot, (nc) => true);
        }

        return _ShowNewsEventSnapshotCommand;
      }
    }
    void ShowNewsEventSnapshot(NewsContainer nc) {
      MessageBus.Current.SendMessage(nc.Event, "Snapshot");
    }
    #endregion

    Exception Log {
      set {
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(value);
      }
    }
    IDisposable newsObserver;
    Predicate<object> _hideNewsFilter = evt => ((NewsContainer)evt).Event.Time.Between(DateTimeOffset.Now.AddHours(-8), DateTimeOffset.Now.Date.AddDays(2));

    #region AutoTradeOffset
    private int _AutoTradeOffset = -30;
    public int AutoTradeOffset {
      get { return _AutoTradeOffset; }
      set {
        if (_AutoTradeOffset != value) {
          _AutoTradeOffset = value;
          RaisePropertyChanged("AutoTradeOffset");
        }
      }
    }

    #endregion
    #region DoShowAll
    private bool _DoShowAll;
    public bool DoShowAll {
      get { return _DoShowAll; }
      set {
        if (_DoShowAll != value) {
          _DoShowAll = value;
          OnPropertyChanged("DoShowAll");
          NewsView.Filter = DoShowAll ? null : _hideNewsFilter;
        }
      }
    }

    #endregion

    public class NewsContainer : ReactiveObject {
      public NewsEvent Event { get; set; }
      #region Properties
      #region AutoTrade
      private bool _AutoTrade;
      public bool AutoTrade {
        get { return _AutoTrade; }
        set { this.RaiseAndSetIfChanged(ref _AutoTrade, value); }
      }

      #endregion
      #region DidHappen
      private bool _DidHappen = false;
      public bool DidHappen {
        get { return _DidHappen; }
        set { this.RaiseAndSetIfChanged(ref _DidHappen, value); }
      }

      #endregion
      #region Countdown
      private int? _Countdown = null;
      public int? Countdown {
        get { return _Countdown; }
        set { this.RaiseAndSetIfChanged(ref _Countdown, value < -30 ? null : value); }
      }
      #endregion
      #region IsToday
      private bool _IsToday = false;
      public bool IsToday {
        get { return _IsToday; }
        set { this.RaiseAndSetIfChanged(ref _IsToday, value); }
      }

      #endregion
      #region Color
      private string _Color = "White";
      public string Color {
        get { return _Color; }
        set { this.RaiseAndSetIfChanged(ref _Color, value); }
      }
      #endregion
      #region Date
      private DateTime _Date = DateTime.MinValue;
      public DateTime Date {
        get { return _Date; }
        set { this.RaiseAndSetIfChanged(ref _Date, value); }
      }

      #endregion
      #endregion


      public NewsContainer(NewsEvent newsEvent) {
        this.Event = newsEvent;
        this.Date = newsEvent.Time.ToLocalTime().Date;
      }

      public override string ToString() {
        return new { Event, Countdown } + "";
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
    #region

    #endregion
    public NewsCasterModel() {
      _newsView = new ListCollectionView(_news);
      _news.CollectionChanged += _news_CollectionChanged;
      NewsView.Filter = _hideNewsFilter;
      //Observable.Interval(1.FromMinutes(), DispatcherScheduler.Current).StartWith(0).Subscribe(l => UpdateNewsColor());
      System.Reactive.Concurrency.TaskPoolScheduler.Default.Schedule(0.FromMinutes(), a => {
        if (News.Count == 0)
          FetchNews();
        else {
          var maxDate = News.Max(evt => evt.Event.Time).Date;
          if (maxDate.DayOfWeek != DayOfWeek.Friday && maxDate == DateTime.Now.Date)
            FetchNews(DateTime.Now.Date.AddDays(1));
          else UpdateNewsColor();
        }
        a(10.FromMinutes());
      });
      //ProcessNews(NewsHound.MyFxBook.Fetch());
    }
    static NewsCasterModel() {
      SavedNews = ForexStorage.UseForexContext(c => c.Event__News.ToArray()
        .Select(ne => new NewsEvent() {
          Country = ne.Country,
          Level = (NewsEventLevel)Enum.Parse(typeof(NewsEventLevel), ne.Level),
          Name = ne.Name,
          Time = ne.Time
        }).ToArray(),
          (c, e) => {
            Default.Log = e;
            SavedNews = new NewsEvent[0];
          });
    }
    #region Countdown Subject
    object _CountdownSubjectLocker = new object();
    System.Reactive.Subjects.ISubject<NewsContainer> _CountdownSubject;
    public static IList<NewsEvent> SavedNews { get; private set; }
    public System.Reactive.Subjects.ISubject<NewsContainer> CountdownSubject {
      get {
        lock (_CountdownSubjectLocker)
          if (_CountdownSubject == null) {
            _CountdownSubject = new System.Reactive.Subjects.Subject<NewsContainer>();
          }
        return _CountdownSubject;
      }
    }
    #endregion

    void _news_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
      switch (e.Action) {
        case NotifyCollectionChangedAction.Add:
          e.NewItems.Cast<NewsContainer>().ForEach(nc => {
            nc.ObservableForProperty(c => c.Countdown)
              .Where(c => c.Value.HasValue && c.Value.Value.Between(-60, 180))
              .Subscribe(c => CountdownSubject.OnNext(c.Sender), exc => Log = exc);
          });
          break;
      }
    }
  }
}
