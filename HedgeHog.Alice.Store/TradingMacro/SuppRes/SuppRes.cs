using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Store {
  public partial class SuppRes {
    public class EntryOrderIdEventArgs : EventArgs {
      public string NewId { get; set; }
      public string OldId { get; set; }
      public EntryOrderIdEventArgs(string newId, string oldId) {
        this.NewId = newId;
        this.OldId = oldId;
      }
    }
    public static readonly double TradesCountMinimum = 1;
    public static readonly string RemovedOrderTag = "X";
    public bool IsBuy { get { return !IsSupport; } }
    public bool IsSell { get { return IsSupport; } }
    private bool _IsActive = true;
    public bool IsActive {
      get { return _IsActive; }
      set {
        if (_IsActive != value) {
          _IsActive = value;
          if (!value) EntryOrderId = "";
          OnIsActiveChanged();
        }
      }
    }
    #region IsGhost
    IDisposable _isGhostDisposable;
    public bool IsGhost {
      get {
        if (_isGhostDisposable == null) {
          _isGhostDisposable = this.SubscribeToPropertiesChanged(sr => OnPropertyChanged("IsGhost")
            , x => x.InManual
            , x => x.IsExitOnly
            , x => x.CanTrade
            , x => x.TradesCount
            );
        }
        return InManual
          && IsExitOnly
          && CanTrade
          && TradesCount <= 0;
      }
      set {
        if (!IsExitOnly) throw new Exception("Not an exit Level.");
        if (value) {
          InManual = true;
          CanTrade = true;
          TradesCount = TradesCount.Min(0);
        } else {
          InManual = false;
          CanTrade = false;
          TradesCount = 9;
        }
      }
    }

    #endregion
    #region CanTrade
    private bool _CanTrade;
    public bool CanTrade {
      get { return _CanTrade; }
      set {
        if (_CanTrade != value) {
          //if (value && IsExitOnly)
          //  Scheduler.Default.Schedule(() => CanTrade = false);
          _CanTrade = value;
          OnPropertyChanged("CanTrade");
          OnPropertyChanged("CanTradeEx");
          RaiseCanTradeChanged();
        }
      }
    }
    #endregion
    public bool HasRateCanTradeChanged { get { return RateCanTrade != Rate; } }
    public double RateCanTrade { get; set; }
    public bool CanTradeEx {
      get { return CanTrade; }
      set {
        if (CanTradeEx == value || InManual) return;
        CanTrade = value;
      }
    }
    public double TradesCountEx {
      get { return TradesCount; }
      set {
        if (TradesCount == value || InManual) return;
        TradesCount = value;
      }
    }

    int _rateExErrorCounter = 0;// This is to ammend some wierd bug in IEntityChangeTracker.EntityMemberChanged or something that it calls
    public double RateEx {
      get { return Rate; }
      set {
        if (Rate == value || this.InManual) return;
        var valuePrev = Rate;
        try {
          Rate = value;
          _rateExErrorCounter = 0;
        } catch (Exception exc) {
          if (_rateExErrorCounter > 100) throw;
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(new Exception("Rate: " + new { Prev = valuePrev, Curr = Rate } + ""));
        }
      }
    }

    #region CanTradeChanged Event
    event EventHandler<EventArgs> CanTradeChangedEvent;
    public event EventHandler<EventArgs> CanTradeChanged {
      add {
        if (CanTradeChangedEvent == null || !CanTradeChangedEvent.GetInvocationList().Contains(value))
          CanTradeChangedEvent += value;
      }
      remove {
        CanTradeChangedEvent -= value;
      }
    }
    protected void RaiseCanTradeChanged() {
      if (CanTradeChangedEvent != null) CanTradeChangedEvent(this, new EventArgs());
    }
    #endregion

    public bool IsGroupIdEmpty { get { return GroupId == Guid.Empty; } }
    #region GroupId
    private Guid _GroupId = Guid.Empty;
    public Guid GroupId {
      get { return _GroupId; }
      set {
        if (_GroupId != value) {
          _GroupId = value;
          OnPropertyChanged("GroupId");
        }
      }
    }

    #endregion

    #region CrossesCount
    private int _CrossesCount;
    public int CrossesCount {
      get { return _CrossesCount; }
      set {
        if (_CrossesCount != value) {
          _CrossesCount = value;
          OnPropertyChanged("CrossesCount");
        }
      }
    }
    #endregion

    #region Scan Event
    event EventHandler<EventArgs> ScanEvent;
    public event EventHandler<EventArgs> Scan {
      add {
        if (ScanEvent == null || !ScanEvent.GetInvocationList().Contains(value))
          ScanEvent += value;
      }
      remove {
        ScanEvent -= value;
      }
    }
    public void OnScan() {
      if (ScanEvent != null) ScanEvent(this, new EventArgs());
    }
    #endregion

    #region SetLevelBy Event
    event EventHandler<EventArgs> SetLevelByEvent;
    public event EventHandler<EventArgs> SetLevelBy {
      add {
        if (SetLevelByEvent == null || !SetLevelByEvent.GetInvocationList().Contains(value))
          SetLevelByEvent += value;
      }
      remove {
        SetLevelByEvent -= value;
      }
    }
    public void OnSetLevelBy() {
      if (SetLevelByEvent != null) SetLevelByEvent(this, new EventArgs());
    }
    #endregion


    #region CorridorDate
    private DateTime _CorridorDate;
    public DateTime CorridorDate {
      get { return _CorridorDate; }
      set {
        if (_CorridorDate != value) {
          _CorridorDate = value;
          OnPropertyChanged("CorridorDate");
        }
      }
    }

    #endregion

    private string _EntryOrderId;
    public string EntryOrderId {
      get { return _EntryOrderId; }
      set {
        if (_EntryOrderId != value) {
          var oldId = value != RemovedOrderTag ? "" : _EntryOrderId;
          _EntryOrderId = value == RemovedOrderTag ? "" : value;
          OnEntryOrderIdChanged(_EntryOrderId, oldId);
        }
      }
    }

    #region TradesCountChanging Event
    public class TradesCountChangingEventArgs : EventArgs {
      public double NewValue { get; set; }
      public double OldValue { get; set; }
    }
    event EventHandler<TradesCountChangingEventArgs> TradesCountChangingEvent;
    public event EventHandler<TradesCountChangingEventArgs> TradesCountChanging {
      add {
        if (TradesCountChangingEvent == null || !TradesCountChangingEvent.GetInvocationList().Contains(value))
          TradesCountChangingEvent += value;
      }
      remove {
        TradesCountChangingEvent -= value;
      }
    }
    protected void RaiseTradesCountChanging(double newValue) {
      if (TradesCountChangingEvent != null)
        TradesCountChangingEvent(this, new TradesCountChangingEventArgs { NewValue = newValue, OldValue = TradesCount });
    }
    double _tradesCountPrev = double.NaN;
    partial void OnTradesCountChanging(global::System.Double value) {
      if (_tradesCountPrev == value) return;
      _tradesCountPrev = TradesCount;
      RaiseTradesCountChanging(value);
    }
    #endregion

    #region TradeCountChanged Event
    event EventHandler<EventArgs> TradesCountChangedEvent;
    public event EventHandler<EventArgs> TradesCountChanged {
      add {
        if (TradesCountChangedEvent == null || !TradesCountChangedEvent.GetInvocationList().Contains(value))
          TradesCountChangedEvent += value;
      }
      remove {
        TradesCountChangedEvent -= value;
      }
    }
    protected void RaiseTradesCountChanged() {
      if (_tradesCountPrev == TradesCount) return;
      _tradesCountPrev = TradesCount;
      if (TradesCountChangedEvent != null) TradesCountChangedEvent(this, new EventArgs());
    }
    partial void OnTradesCountChanged() {
      RaiseTradesCountChanged();
    }
    #endregion



    event EventHandler<EntryOrderIdEventArgs> EntryOrderIdChangedEvent;
    public event EventHandler<EntryOrderIdEventArgs> EntryOrderIdChanged {
      add {
        if (EntryOrderIdChangedEvent == null || !EntryOrderIdChangedEvent.GetInvocationList().Contains(value))
          EntryOrderIdChangedEvent += value;
      }
      remove {
        EntryOrderIdChangedEvent -= value;
      }
    }
    void OnEntryOrderIdChanged(string newId, string oldId) {
      if (EntryOrderIdChangedEvent != null) EntryOrderIdChangedEvent(this, new EntryOrderIdEventArgs(newId, oldId));
    }

    EventHandler _IsActiveChanged;
    public event EventHandler IsActiveChanged {
      add {
        if (_IsActiveChanged == null || !_IsActiveChanged.GetInvocationList().Contains(value))
          _IsActiveChanged += value;
      }
      remove {
        _IsActiveChanged -= value;
      }
    }
    protected void OnIsActiveChanged() {
      if (_IsActiveChanged != null)
        _IsActiveChanged(this, EventArgs.Empty);
    }
    EventHandler _rateChangedDelegate;
    public event EventHandler RateChanged {
      add {
        if (_rateChangedDelegate == null || !_rateChangedDelegate.GetInvocationList().Contains(value))
          _rateChangedDelegate += value;
      }
      remove {
        _rateChangedDelegate -= value;
      }
    }
    partial void OnRateChanged() {
      if (_rateChangedDelegate != null)
        _rateChangedDelegate(this, EventArgs.Empty);
    }

    private int _Index;
    public int Index {
      get { return _Index; }
      set {
        if (_Index != value) {
          _Index = value;
          OnPropertyChanged("Index");
        }
      }
    }
    protected override void OnPropertyChanged(string property) {
      base.OnPropertyChanged(property);
    }

    #region IsExitOnly
    private bool _IsExitOnly;
    public bool IsExitOnly {
      get { return _IsExitOnly; }
      set {
        if (_IsExitOnly != value) {
          _IsExitOnly = value;
          OnPropertyChanged("IsExitOnly");
          if (value) CanTrade = false;
        }
      }
    }

    #endregion
    bool _InManual;
    public bool InManual {
      get { return _InManual; }
      set {
        if (_InManual == value) return;
        _InManual = value;
        OnPropertyChanged("InManual");
      }
    }

    public void ResetPricePosition() { _pricePrev = PricePosition = double.NaN; }
    double _pricePosition = double.NaN;
    public double PricePosition {
      get { return _pricePosition; }
      set {
        if (_pricePosition != value) {
          var prev = _pricePosition;
          _pricePosition = value;
          if (value != 0 && !double.IsNaN(value) && !double.IsNaN(prev))
            RaiseCrossed(value);
        }
      }
    }


    #region Crossed Event
    public void ClearCrossedHandlers() {
      if (CrossedEvent != null)
        CrossedEvent.GetInvocationList().ToList().ForEach(h => CrossedEvent -= h as EventHandler<CrossedEvetArgs>);
    }
    public class CrossedEvetArgs : EventArgs {
      public double Direction { get; set; }
      public CrossedEvetArgs(double direction) {
        this.Direction = direction;
      }
    }
    event EventHandler<CrossedEvetArgs> CrossedEvent;
    public event EventHandler<CrossedEvetArgs> Crossed {
      add {
        if (CrossedEvent == null || !CrossedEvent.GetInvocationList().Contains(value))
          CrossedEvent += value;
      }
      remove {
        if (value == null) {
          if (CrossedEvent != null)
            CrossedEvent.GetInvocationList().Cast<EventHandler<CrossedEvetArgs>>().ForEach(d => CrossedEvent -= d);
        } else
          CrossedEvent -= value;
      }
    }
    protected void RaiseCrossed(double pricePosition) {
      if (CrossedEvent != null) CrossedEvent(this, new CrossedEvetArgs(pricePosition));
    }
    #endregion

    double _pricePrev = double.NaN;
    public void SetPrice(double price) {
      if (double.IsNaN(price))
        GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<Exception>(new Exception(new { type = GetType().Name, PricePosition = "is NaN." } + ""));
      else {
        if (Rate.Between(price, _pricePrev)) {
          _pricePosition = _pricePrev - Rate;
        }
        _pricePrev = price;
        PricePosition = (price - Rate).IfNaN(0).Sign();
      }
    }

    public DateTime? TradeDate { get; set; }
  }
}
