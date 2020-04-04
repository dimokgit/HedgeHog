using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HedgeHog;
using HedgeHog.Bars;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.messages;
using ReactiveUI;
using static HedgeHog.Shared.TradesManagerStatic;
namespace IBApp {
  public class IBWraper :ITradesManager {

    public static (int c, IList<T> a) RunUntilCount<T>(int count, int countMax, Func<IList<T>> func) {
      IList<T> options = default;
      do {
        options = func();
      } while(options.Count < count && (countMax--)/*.SideEffect(c => Debug.WriteLine(new { countMax }))*/ > 0);
      return (countMax, options);
    }

    public readonly IBClientCore _ibClient;
    private AccountManager _accountManager;
    public AccountManager AccountManager { get { return _accountManager; } }
    private void Trace(object o) { _ibClient.Trace(o); }
    private void Verbous(object o) { _ibClient.Trace(o); }
    public void FetchMMRs() => _accountManager.FetchMMRs();

    #region ctor
    static IBWraper() {
      _orderAddedSubject = new Subject<OrderEventArgs>();
      _priceChangedSubject = new Subject<PriceChangedEventArgs>();
    }
    public IBWraper(ICoreFX coreFx, Func<Trade, double> commissionByTrade) {
      CommissionByTrade = commissionByTrade;

      {
        var hot = _orderAddedSubject.Publish();
        hot.Connect();
        OrderAddedObservable = hot.Replay();
        OrderAddedObservable.Connect();
      }
      {
        OrderRemovedObservable = Observable.FromEvent<OrderRemovedEventHandler, HedgeHog.Shared.Order>(
          next => (o) => next.Try(o, e => Trace($"{nameof(OrderRemoved)}: {e.Message}")), h => OrderRemoved += h, h => OrderRemoved -= h);
      }
      {
        var hot = _priceChangedSubject.Publish();
        hot.Connect();
        PriceChangedObservable = hot.Replay();
        PriceChangedObservable.Connect();
      }

      CoreFX = coreFx;
      _ibClient = (IBClientCore)CoreFX;
      _ibClient.PriceChanged += OnPriceChanged;
      _ibClient.CommissionByTrade = commissionByTrade;
      _ibClient.ManagedAccounts += OnManagedAccounts;
    }
    #endregion

    public Trade CreateTrade(string symbol)
      => Trade.Create(this, symbol, GetPipSize(symbol), GetBaseUnitSize(symbol), null);

    object _accountManagerLocket = new object();
    private void OnManagedAccounts(ManagedAccountsMessage m) {
      lock(_accountManagerLocket) {
        if(_accountManager != null) {
          Trace(new { _accountManager, isNotNull = true });
          //_accountManager.Dispose();
          return;
        }
        var ma = m.ManagedAccounts.Where(a => _ibClient.ManagedAccount.IsNullOrWhiteSpace() || a == _ibClient.ManagedAccount).FirstOrDefault();
        if(ma == null)
          throw new Exception(new { _ibClient.ManagedAccount, error = "Not Found" } + "");
        _accountManager = new AccountManager(_ibClient, ma, CreateTrade, CommissionByTrade);
        _accountManager.TradeAdded += (s, e) => RaiseTradeAdded(e.Trade);
        _accountManager.TradeChanged += (s, e) => RaiseTradeChanged(e.Trade);
        _accountManager.TradeRemoved += (s, e) => RaiseTradeRemoved(e.Trade);

        //_accountManager.TradeClosed += (s, e) => RaiseTradeClosed(e.Trade);
        //_accountManager.TradeClosed += _accountManager_TradeClosed; ;
        // TODO: RaiseTradeClosed needs testing
        Observable.FromEventPattern<TradeEventArgs>(h => _accountManager.TradeClosed += h, h => _accountManager.TradeClosed -= h)
          .SubscribeOn(TaskPoolScheduler.Default)
          .Subscribe(eh => RaiseTradeClosed(eh.EventArgs.Trade));
        _accountManager.OrderAdded += RaiseOrderAdded;
        _accountManager.OrderRemoved += RaiseOrderRemoved;
      }
    }


    private void OnPriceChanged(object sender, PriceChangedEventArgs e) {
      var price = e.Price;
      try {
        GetAccount().PipsToMC = PipsToMarginCallCore().ToInt();
      } catch { }
      RaisePriceChanged(price);
    }

    #region ITradesManager - Implemented



    #region Methods
    //public int GetBaseUnitSize(string pair) => TradesManagerStatic.IsCurrenncy(pair) ? 1 : 1;
    //public int GetBaseUnitSize(string pair) => IBApi.Contract.ContractDetails.TryGetValue(pair, out var m) ? int.Parse(m.Summary.Multiplier.IfEmpty("0")) : 0;
    public int GetBaseUnitSize(string pair) => IBApi.Contract.FromCache(pair, m => int.Parse(m.Multiplier.IfEmpty("1"))).DefaultIfEmpty().Single();

    public double Leverage(string pair, bool isBuy) => GetBaseUnitSize(pair) / GetMMR(pair, isBuy);
    public Trade TradeFactory(string pair) => Trade.Create(this, pair, GetPipSize(pair), GetBaseUnitSize(pair), CommissionByTrade);

    public double InPips(string pair, double? price) => price.GetValueOrDefault() / GetPipSize(pair);
    public double RateForPipAmount(Price price) { return price.Ask.Avg(price.Bid); }
    public double RateForPipAmount(double ask, double bid) { return ask.Avg(bid); }
    TBar ToRate<TBar>(DateTime date, double open, double high, double low, double close, long volume, int count) where TBar : Rate, new() {
      return Rate.Create<TBar>(date, high, low, true);
    }
    public void GetBarsBase<TBar>(string pair
      , int period
      , int periodsBack
      , DateTime startDate
      , DateTime endDate
      , List<TBar> ticks
      , Func<List<TBar>, List<TBar>> map
      , Action<RateLoadingCallbackArgs<TBar>> callBack = null
      ) where TBar : Rate, new() {

      Thread.CurrentThread.Name.ThrowIf(threadName => threadName == "MsgProc");
      var contract = Contract.FromCache(pair).Count(1, $"{nameof(GetBarsBase)}: {new { pair }}").Single();
      var cd = contract.FromDetailsCache().Single();
      if(contract.IsFuture)
        contract = new Contract {
          SecType = "CONTFUT"
          ,
          Exchange = cd.ValidExchanges.Split(new[] { ';',',' })[0]// "GLOBEX"/*contract.Exchange*//*, TradingClass = contract.TradingClass*/
          ,
          Symbol = cd.UnderSymbol
        };
      var isDone = false;
      Func<DateTime, DateTime> fxDate = d => d == FX_DATE_NOW ? new DateTime(DateTime.Now.Ticks, DateTimeKind.Local) : d;
      endDate = fxDate(endDate);
      startDate = fxDate(startDate);
      var timeUnit = period == 0 ? TimeUnit.S : period == 1 ? TimeUnit.D : TimeUnit.W;
      var barSize = period == 0 ? BarSize._1_secs : period == 1 ? BarSize._1_min : BarSize._3_mins;
      var duration = (endDate - startDate).Duration();
      var lastTime = DateTime.Now;
      new HistoryLoader_Slow<TBar>(
        _ibClient,
        contract,
        periodsBack,
        endDate.Max(startDate),
        duration,
        timeUnit,
        barSize,
        ToRate<TBar>,
         list => { ticks.AddRange(list); ticks.Sort(); isDone = true; },
         list => {
           //var x = new { ReqId = _reqId, contract.Symbol, EndDate = _endDate, Duration = Duration(_barSize, _timeUnit, _duration) } + ""));

           callBack(new RateLoadingCallbackArgs<TBar>(
             new {
               HistoryLoader = new {
                 StartDate = list.FirstOrDefault()?.StartDate,
                 EndDate = list.LastOrDefault()?.StartDate,
                 timeUnit, barSize,
                 contract.Symbol,
                 duration = HistoryLoader<Rate>.Duration(barSize, timeUnit, duration)
               }
             },
             list));
           lastTime = DateTime.Now;
         },
         exc => {
           isDone = !(exc is SoftException);
           Trace(exc);
           lastTime = DateTime.Now;
         });
      while(!isDone) {
        Thread.Sleep(300);
        if(lastTime.AddMinutes(10) < DateTime.Now) {
          Trace(new { GetBarsBase = new { contract, lastTime, DateTime.Now, error = "Timeout" } });
          break;
        }
      }
      // Test threading
      //lastTime = DateTime.Now;
      //while(true) {
      //  Thread.Sleep(300);
      //  if(lastTime.AddMinutes(10) < DateTime.Now) {
      //    break;
      //  }
      //}

      return;
    }
    public void GetBars(string pair, int periodMinutes, int periodsBack, DateTime startDate, DateTime endDate, List<Rate> ratesList, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      throw new NotImplementedException();
    }

    public void GetBars(string pair, int Period, int periodsBack, DateTime StartDate, DateTime EndDate, List<Rate> Bars, Action<RateLoadingCallbackArgs<Rate>> callBack, bool doTrim, Func<List<Rate>, List<Rate>> map) {
      if(Contract.FromCache(pair).IsEmpty()) {
        Trace($"Contract.FromCache({pair}).IsEmpty()");
        _ibClient.ReqContractDetailsCached(pair)
          .ObserveOn(TaskPoolScheduler.Default)
          .ForEachAsync(_ => GetBarsBase(pair, Period, periodsBack, StartDate, EndDate, Bars, map, callBack))
          .GetAwaiter().GetResult();
      } else
        GetBarsBase(pair, Period, periodsBack, StartDate, EndDate, Bars, map, callBack);
    }
    public Account GetAccount() {
      try {
        var account = AccountManager?.Account;
        return account;
      } catch(Exception exc) {
        RaiseError(exc);
        return null;
      }
    }
    double PipsToMarginCallCore() {
      Account account = GetAccount();
      var trades = GetTrades();
      if(!trades.Any())
        return int.MaxValue;
      var pair = trades[0].Pair;
      return TryGetPrice(pair, out var price)
        ? trades.Sum(trade =>
           MoneyAndLotToPips(pair, account.ExcessLiquidity, trade.Lots, price.Average, GetPipSize(pair)) * trade.Lots) / trades.Lots()
        : 0;
    }


    public double PipsToMarginCall {
      get {
        return PipsToMarginCallCore();
      }
    }

    public IList<Trade> GetTrades() => AccountManager?.GetTrades() ?? new Trade[0];
    public Trade[] GetTrades(string pair) => GetTrades().Where(t => t.Pair.WrapPair() == pair.WrapPair()).ToArray();
    public Trade[] GetTradesInternal(string Pair) => GetTrades(Pair);
    public IList<Trade> GetClosedTrades(string pair) => AccountManager?.GetClosedTrades() ?? new Trade[0];
    public void SetClosedTrades(IEnumerable<Trade> trades) => AccountManager.SetClosedTrades(trades);

    #endregion

    #region Error Event
    event EventHandler<ErrorEventArgs> ErrorEvent;
    public event EventHandler<ErrorEventArgs> Error {
      add {
        if(ErrorEvent == null || !ErrorEvent.GetInvocationList().Contains(value))
          ErrorEvent += value;
      }
      remove {
        ErrorEvent -= value;
      }
    }
    protected void RaiseError(Exception exc) {
      ErrorEvent?.Invoke(this, new ErrorEventArgs(exc));
    }
    #endregion

    #region PriceChangedEvent

    event EventHandler<PriceChangedEventArgs> PriceChangedEvent;
    public event EventHandler<PriceChangedEventArgs> PriceChanged {
      add {
        if(PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
          PriceChangedEvent += value;
      }
      remove {
        PriceChangedEvent -= value;
      }
    }
    void RaisePriceChanged(Price price, Account account, IList<Trade> trades) {
      var e = new PriceChangedEventArgs(price, account, trades);
      PriceChangedEvent?.Invoke(this, e);
      //_priceChangedSubject.OnNext(e);
    }

    public void RaisePriceChanged(Price price) {
      RaisePriceChanged(price, GetAccount(), GetTrades());
    }
    #endregion

    #region OrderAddedEvent
    public static Subject<OrderEventArgs> _orderAddedSubject { get; set; }

    private static readonly Subject<PriceChangedEventArgs> _priceChangedSubject;

    public IConnectableObservable<OrderEventArgs> OrderAddedObservable { get; private set; }

    event EventHandler<OrderEventArgs> OrderAddedEvent;
    public event EventHandler<OrderEventArgs> OrderAdded {
      add {
        if(OrderAddedEvent == null || !OrderAddedEvent.GetInvocationList().Contains(value))
          OrderAddedEvent += value;
      }
      remove {
        OrderAddedEvent -= value;
      }
    }

    void RaiseOrderAdded(object sender, OrderEventArgs args) {
      OrderAddedEvent?.Invoke(this, args);
      _orderAddedSubject.OnNext(args);
    }
    #endregion
    #region OrderRemovedEvent
    public IObservable<HedgeHog.Shared.Order> OrderRemovedObservable { get; private set; }
    public event OrderRemovedEventHandler OrderRemovedEvent;
    public event OrderRemovedEventHandler OrderRemoved {
      add {
        if(OrderRemovedEvent == null || !OrderRemovedEvent.GetInvocationList().Contains(value))
          OrderRemovedEvent += value;
      }
      remove {
        OrderRemovedEvent -= value;
      }
    }

    void RaiseOrderRemoved(HedgeHog.Shared.Order args) => OrderRemovedEvent?.Invoke(args);
    #endregion


    #region TradeAddedEvent
    event EventHandler<TradeEventArgs> TradeAddedEvent;
    public event EventHandler<TradeEventArgs> TradeAdded {
      add {
        if(TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
          TradeAddedEvent += value;
      }
      remove {
        if(TradeAddedEvent != null)
          TradeAddedEvent -= value;
      }
    }
    void RaiseTradeAdded(Trade trade) {
      TradeAddedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion


    #region TradeChanged Event
    event EventHandler<TradeEventArgs> TradeChangedEvent;
    public event EventHandler<TradeEventArgs> TradeChanged {
      add {
        if(TradeChangedEvent == null || !TradeChangedEvent.GetInvocationList().Contains(value))
          TradeChangedEvent += value;
      }
      remove {
        TradeChangedEvent -= value;
      }
    }
    protected void RaiseTradeChanged(Trade trade) {
      if(TradeChangedEvent != null) TradeChangedEvent(this, new TradeEventArgs(trade));
    }
    #endregion


    #region TradeRemovedEvent
    event EventHandler<TradeEventArgs> TradeRemovedEvent;
    public event EventHandler<TradeEventArgs> TradeRemoved {
      add {
        if(TradeRemovedEvent == null || !TradeRemovedEvent.GetInvocationList().Contains(value))
          TradeRemovedEvent += value;
      }
      remove {
        TradeRemovedEvent -= value;
      }
    }
    void RaiseTradeRemoved(Trade trade) {
      TradeRemovedEvent?.Invoke(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeClosedEvent
    event EventHandler<TradeEventArgs> TradeClosedEvent;
    public event EventHandler<TradeEventArgs> TradeClosed {
      add {
        if(TradeClosedEvent == null || !TradeClosedEvent.GetInvocationList().Contains(value))
          TradeClosedEvent += value;
      }
      remove {
        if(TradeClosedEvent != null)
          TradeClosedEvent -= value;
      }
    }
    void RaiseTradeClosed(Trade trade) {
      try {
        if(TradeClosedEvent != null) {
          var tradeArg = new TradeEventArgs(trade);
          TradeClosedEvent(this, tradeArg);
        }
      } catch(Exception exc) {
        Trace($"{nameof(RaiseTradeClosed)}:\n{exc.Inners().Select(e => e.Message).ToJson()}");
      }
    }
    #endregion


    #region Properties
    public bool HasTicks => false;
    public bool IsLoggedIn => _ibClient.IsLoggedIn;
    public DateTime ServerTime {
      get {
        return DateTime.Now + _ibClient._serverTimeOffset;
      }
    }
    #endregion
    #endregion

    #region ITradesManager
    public Func<Trade, double> CommissionByTrade { get; private set; }

    public ICoreFX CoreFX { get; set; }

    public bool IsHedged {
      get {
        throw new NotImplementedException();
      }
    }

    public bool IsInTest { get; set; }
    public IConnectableObservable<PriceChangedEventArgs> PriceChangedObservable { get; }

    public event EventHandler<OrderEventArgs> OrderChanged;
    public event EventHandler<RequestEventArgs> RequestFailed;

    public void ChangeEntryOrderLot(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedLimit(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderPeggedStop(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeEntryOrderRate(string orderId, double rate) {
      throw new NotImplementedException();
    }

    public void ChangeOrderAmount(string orderId, int lot) {
      throw new NotImplementedException();
    }

    public void ChangeOrderRate(HedgeHog.Shared.Order order, double rate) {
      throw new NotImplementedException();
    }

    public void CloseAllTrades() {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair, Price price) {
      var lotBuy = GetTradesInternal(pair).IsBuy(true).Lots();
      if(lotBuy > 0)
        ClosePair(pair, true, lotBuy, price);
      var lotSell = GetTradesInternal(pair).IsBuy(false).Lots();
      if(lotSell > 0)
        ClosePair(pair, false, lotSell, price);
      return lotBuy > 0 || lotSell > 0;
    }

    public bool ClosePair(string pair, bool isBuy, Price price) {
      throw new NotImplementedException();
    }

    public bool ClosePair(string pair, bool buy, int lot, Price price) {
      try {
        var lotToDelete = Math.Min(lot, GetTradesInternal(pair).IsBuy(buy).Lots());
        if(lotToDelete > 0) {
          OpenTrade(pair, !buy, lotToDelete, 0, 0, "", price/* ?? GetPrice(pair)*/);
        } else {
          RaiseError(new Exception("Pair [" + pair + "] does not have positions to close."));
          return false;
        }
        return true;
      } catch(Exception exc) {
        RaiseError(exc);
        return false;
      }
    }

    public void CloseTrade(Trade trade) {
      throw new NotImplementedException();
    }

    public bool CloseTrade(Trade trade, int lot, Price price) {
      throw new NotImplementedException();
    }

    public void CloseTradeAsync(Trade trade) {
      throw new NotImplementedException();
    }

    public void CloseTradesAsync(Trade[] trades) {
      throw new NotImplementedException();
    }

    public string CreateEntryOrder(string pair, bool isBuy, int amount, double rate, double stop, double limit) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderLimit(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteEntryOrderStop(string orderId) {
      throw new NotImplementedException();
    }

    public bool DeleteOrder(string orderId) {
      throw new NotImplementedException();
    }

    public void DeleteOrders(string pair) {
      RaiseError(new NotImplementedException(nameof(DeleteOrders)));
    }

    public PendingOrder FixCreateLimit(string tradeId, double limit, string remark) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId) {
      throw new NotImplementedException();
    }

    public object FixOrderClose(string tradeId, int mode, Price price, int lot) {
      throw new NotImplementedException();
    }

    public object[] FixOrdersClose(params string[] tradeIds) {
      throw new NotImplementedException();
    }

    public void FixOrderSetLimit(string tradeId, double takeProfit, string remark) {
      throw new NotImplementedException();
    }

    public void FixOrderSetStop(string tradeId, double stopLoss, string remark) {
      throw new NotImplementedException();
    }

    public IList<Rate> GetBarsFromHistory(string pair, int periodMinutes, DateTime dateTime, DateTime endDate) {
      throw new NotImplementedException();
    }


    void RaiseNotImplemented(string NotImplementedException) {
      Trace(new NotImplementedException(new { NotImplementedException } + ""));
    }
    void RaiseShouldBeImplemented(string NotImplementedException) {
      //Trace(new NotImplementedException(new { NotImplementedException } + ""));
    }

    public int GetDigits(string pair) => TradesManagerStatic.GetDigits(pair);

    public Trade GetLastTrade(string pair) {
      //RaiseNotImplemented(nameof(GetLastTrade));
      return null;
    }

    public HedgeHog.Shared.Order GetNetLimitOrder(Trade trade, bool getFromInternal = false) {
      RaiseShouldBeImplemented(nameof(GetNetLimitOrder));
      return null;
    }

    public HedgeHog.Shared.Order[] GetOrders(string pair) {
      RaiseShouldBeImplemented(nameof(GetOrders));
      return new HedgeHog.Shared.Order[0];
    }
    public IList<(string status, double filled, double remaining, bool isDone)> GetOrderStatuses(string pair = "")
      => _accountManager?.UseOrderContracts(orderContracts => orderContracts
      .Where(os => pair.IsNullOrWhiteSpace() || os.contract.Instrument == pair.ToLower())
      .Select(os => pair.IsNullOrWhiteSpace()
      ? ($"{os.contract}:{os.status.status}:[{os.order.OrderId}]", os.status.filled, os.status.remaining, os.isDone)
      : ($"{os.status.status}:[{os.order.OrderId}]", os.status.filled, os.status.remaining, os.isDone))
      .ToArray()).Concat().ToList()
      ?? new (string status, double filled, double remaining, bool isDone)[0].ToList();

    public double GetNetOrderRate(string pair, bool isStop, bool getFromInternal = false) {
      throw new NotImplementedException();
    }

    public Offer GetOffer(string pair) {
      return TradesManagerStatic.GetOffer(pair);
    }

    public Offer[] GetOffers() {
      throw new NotImplementedException();
    }


    //public double GetPipCost(string pair) {
    //  throw new NotImplementedException();
    //}

    public double GetPipSize(string pair) => ContractDetails.FromCache(pair, cd => cd.PriceMagnifier).Count(1,_=>new Exception($"new{pair} not found in cache."),null).Single();
    //cd => Math.Pow(10, Math.Log10(cd.MinTick.Floor()))).DefaultIfEmpty().Single();

    public IEnumerable<Price> TryGetPrice(string pair) {
      if(TryGetPrice(pair, out var price))
        yield return price;
      else yield break;
    }
    public bool TryGetPrice(string pair, out Price price) => _ibClient.TryGetPrice(pair, out price);
    public Price GetPrice(string pair) => _ibClient.GetPrice(pair);

    public Tick[] GetTicks(string pair, int periodsBack, Func<List<Tick>, List<Tick>> map) {
      throw new NotImplementedException();
    }

    public double InPoints(string pair, double? price) { return InPoins(this, pair, price); }

    public void RefreshOrders() {
      throw new NotImplementedException();
    }

    public void ResetClosedTrades(string pair) {
      throw new NotImplementedException();
    }

    public double Round(string pair, double value, int digitOffset = 0) { return Math.Round(value, GetDigits(pair) + digitOffset); }

    public void SetServerTime(DateTime serverTime) {
      throw new NotImplementedException();
    }

    public PendingOrder OpenTrade(string pair, bool buy, int lots, double takeProfit, double stopLoss, string remark, Price price) {
      if(!IBApi.Contract.Contracts.TryGetValue(pair, out var contract))
        throw new Exception($"Pair:{pair} is not fround in Contracts");
      AccountManager.OpenTrade(contract, lots * (buy ? 1 : -1), (buy ? price?.Ask : price?.Bid).GetValueOrDefault(), takeProfit).Subscribe();
      return null;
    }

    public PendingOrder OpenTrade(string Pair, bool isBuy, int lot, double takeProfit, double stopLoss, double rate, string comment) {
      if(comment == "OPT") {
        var x = (
          from under in _ibClient.ReqContractDetailsCached(Pair).Select(cd => cd.Contract)
          from up in under.ReqPriceSafe().Select(_ => _.ask.Avg(_.bid))
          from os in _ibClient.ReqCurrentOptionsAsync(Pair, up, new[] { isBuy }, 0, 1, 1, c => true).ToArray()
          from o in os
          select (o, under, lot: lot * (isBuy ? 1 : -1))
          )
          .Take(1)
          .Subscribe(t => AccountManager.OpenTradeWithConditions(t.o.LocalSymbol, t.lot, takeProfit, rate, true));
      }
      return null;
    }

    public double GetMinTick(string pair) => GetMinTickImpl(pair);
    public int GetContractSize(string pair) => GetContractSizeImpl(pair);

    static Func<string, double> GetMinTickImpl = new Func<string, double>((string pair) => Contract.FromCache(pair).Select(c => c.MinTick()).Single()).Memoize();
    static Func<string, int> GetContractSizeImpl = new Func<string, int>((string pair) => Contract.FromCache(pair).Select(c => c.ComboMultiplier).Single()).Memoize();

    #endregion
  }
}