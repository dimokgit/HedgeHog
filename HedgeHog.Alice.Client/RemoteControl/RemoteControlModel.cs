using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using Gala = GalaSoft.MvvmLight.Command;
using FXW = Order2GoAddIn.FXCoreWrapper;
using System.Windows.Data;
using System.Data.Objects;
using System.Windows.Input;
using System.Windows;
using HedgeHog.Shared;
using HedgeHog.Bars;
using System.IO;
using System.Xml.Linq;

namespace HedgeHog.Alice.Client {
  public class RemoteControlModel : HedgeHog.Models.ModelBase {
    #region Properties
    public bool IsInDesigh { get { return GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic; } }
    FXW fw;
    public bool IsLoggedIn { get { return MasterModel.CoreFX.IsLoggedIn; } }
    public IMainModel MasterModel { get; set; }
    public ObservableCollection<string> Instruments { get; set; }
    public double[] TradingRatios { get { return new double[] {0, 0.1,0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; } }
    public double[] StopsAndLimits { get { return new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100, 120, 135, 150 }; } }

    double CurrentLoss { get { return TradingMacrosCopy.Sum(tm => tm.CurrentLoss); } }
    object _tradingMacrosLocker = new object();
    Models.TradingMacro[] _tradingMacrosCiopy;
    public Models.TradingMacro[] TradingMacrosCopy {
      get {
        lock (_tradingMacrosLocker) {
          if (_tradingMacrosCiopy == null) _tradingMacrosCiopy = TradingMacros.ToArray();
          return _tradingMacrosCiopy;
        } 
      }
    }
    Models.TradingMacro GetTradingMacro(string pair) {
      var tms = TradingMacrosCopy.Where(tm => tm.Pair == pair).ToArray();
      if (tms.Length == 0)
        new NullReferenceException("TradingMacro is null");
      return tms.OrderBy(tm => tm.TradeAmount).ThenBy(tm => tm.Limit).ThenBy(tm => tm.Stop).FirstOrDefault();
    }

    public IQueryable<Models.TradingMacro> TradingMacros {
      get {
        try {
          return !IsInDesigh && MasterModel.CoreFX.IsLoggedIn ? GlobalStorage.Context.TradingMacroes : new[] { new Models.TradingMacro() }.AsQueryable();
        } catch (Exception exc) {
          Debug.Fail("TradingMacros is null.");
          return null;
        }
      }
    }
    private Exception Log { set { MasterModel.Log = value; } }
    List<Trade> tradesWithLoss = new List<Trade>();
    Dictionary<string, List<Rate>> ratesByPair = new Dictionary<string, List<Rate>>();
    Dictionary<string, Tick[]> ticksByPair = new Dictionary<string, Tick[]>();
    Dictionary<string, double> anglesByPair = new Dictionary<string, double>();

    Dictionary<string, ThreadScheduler> _loadTicks = new Dictionary<string, ThreadScheduler>();
    ThreadScheduler GetTickLoader(string pair) {
      if (!_loadTicks.ContainsKey(pair)) 
        _loadTicks.Add(pair, new ThreadScheduler(() => LoadTicks(pair), (s, e) => Log = e.Exception));
      return _loadTicks[pair];
    }
    ThreadScheduler _CleanEntryOrderScheduler;

    public ThreadScheduler CleanEntryOrderScheduler {
      get { 
        if(_CleanEntryOrderScheduler == null)
          _CleanEntryOrderScheduler = new ThreadScheduler(CleanEntryOrders,(s,e)=>Log = e.Exception);
        return _CleanEntryOrderScheduler; 
      }
    }

    #region PendingTrade
    class PendingTrade {
      public string Pair { get; set; }
      public bool IsBuy { get; set; }
      public Func<bool> Condition { get; set; }
      public Action OpenTradeCommand;
      public PendingTrade(string pair, bool isBuy, Func<bool> condition,Action openTradeCommand) {
        this.Pair = pair;
        this.IsBuy = isBuy;
        this.Condition = condition;
        this.OpenTradeCommand = openTradeCommand;
      }
    }
    List<PendingTrade> _pendingTrades= new List<PendingTrade>();
    PendingTrade GetPendingTrade(string pair, bool isBuy) {
       var pt = _pendingTrades.SingleOrDefault(po => po.Pair == pair && po.IsBuy == isBuy);
       return pt == null ? null : pt;
    }
    bool HasPendingOrder(string pair, bool isBuy) { return GetPendingTrade(pair, isBuy) != null; }
    void RemovePendingOrder(string pair, bool isBuy) {
      var pt = GetPendingTrade(pair, isBuy);
      if (pt != null) _pendingTrades.Remove(pt);
    }
    void AddPendingOrder(bool isBuy, string pair, Func<bool> condition, Action openTradeCommand) {
      _pendingTrades.Add(new PendingTrade(pair, isBuy, condition, openTradeCommand));
    }
    #endregion

    #endregion

    #region Commands

    ICommand _DeleteTradingMacroCommand;
    public ICommand DeleteTradingMacroCommand {
      get {
        if (_DeleteTradingMacroCommand == null) {
          _DeleteTradingMacroCommand = new Gala.RelayCommand<object>(DeleteTradingMacro, (tm) => tm is Models.TradingMacro);
        }

        return _DeleteTradingMacroCommand;
      }
    }
    void DeleteTradingMacro(object tradingMacro) {
      var tm = tradingMacro as Models.TradingMacro;
      if (tm == null || tm.EntityState == System.Data.EntityState.Detached) return;
      GlobalStorage.Context.TradingMacroes.DeleteObject(tm);
      GlobalStorage.Context.SaveChanges();
    }


    ICommand _ClosePairCommand;
    public ICommand ClosePairCommand {
      get {
        if (_ClosePairCommand == null) {
          _ClosePairCommand = new Gala.RelayCommand<object>(ClosePair, (tm) => true);
        }

        return _ClosePairCommand;
      }
    }

    void ClosePair(object tradingMacro) {
      try {
        var pair = (tradingMacro as Models.TradingMacro).Pair;
        var tradeIds = fw.GetTrades(pair).Select(t => t.Id).ToArray();
        if (tradeIds.Length > 0) AddTradeToReverse(tradeIds.Last());
        fw.FixOrdersClose(tradeIds);
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }



    ICommand _ReversePairCommand;
    public ICommand ReversePairCommand {
      get {
        if (_ReversePairCommand == null) {
          _ReversePairCommand = new Gala.RelayCommand<object>(ReversePair, (tm) => true);
        }

        return _ReversePairCommand;
      }
    }

    List<string> tradesToReverse = new List<string>();
    void AddTradeToReverse(string tradeId) {
      if (!tradesToReverse.Any(s => s == tradeId)) tradesToReverse.Add(tradeId);
    }
    bool HasTradeToReverse(string tradeId) {
      return tradesToReverse.Any(s => s == tradeId);
    }

    void ReversePair(object tradingMacro) {
      try {
        var pair = (tradingMacro as Models.TradingMacro).Pair;
        var tradeIds = fw.GetTrades(pair).Select(t => t.Id).ToArray();
        if (tradeIds.Length > 0) AddTradeToReverse(tradeIds.Last());
        fw.FixOrdersClose(tradeIds);
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _BuyCommand;
    public ICommand BuyCommand {
      get {
        if (_BuyCommand == null) {
          _BuyCommand = new Gala.RelayCommand<object>(Buy, (tm) => true);
        }

        return _BuyCommand;
      }
    }
    void Buy(object tradingMacro) {
      try {
        OpenChainTrade(tradingMacro as Models.TradingMacro, true);
        //AddPendingOrder(true, tm.Pair, () => openTradeCondition( tm,true), () => OpenTrade(tm, true));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }


    ICommand _SellCommand;
    public ICommand SellCommand {
      get {
        if (_SellCommand == null) {
          _SellCommand = new Gala.RelayCommand<object>(Sell, (tm) => true);
        }

        return _SellCommand;
      }
    }
    void Sell(object tradingMacro) {
      try {
        OpenChainTrade(tradingMacro as Models.TradingMacro,false);
        //AddPendingOrder(false, tm.Pair, () => openTradeCondition(tm, false), () => OpenTrade(tm, false));
      } catch (Exception exc) {
        MessageBox.Show(exc + "");
      }
    }

    void OpenChainTrade(Models.TradingMacro tm, bool isBuy) {
      var lastTrade = tradesWithLoss.OrderBy(t => t.Id).Where(t => t.Pair == tm.Pair).LastOrDefault();
      var lot = tm.CurrentLoss < 0 ? CalculateLot(tm) : tm.TradeAmount;
      var remark = tm.CurrentLoss < 0 && lastTrade != null ? lastTrade.Id : "";
      OpenTrade(isBuy, tm.Pair, lot, tm.Limit, 0, GetStopByFractal(tm.Pair, isBuy), remark);
    }

    #endregion

    #region Ctor
    void CleanEntryOrders() {
      var trades = fw.GetTrades();
      foreach (var order in fw.GetOrders(""))
        if (!trades.Any(t => t.Id == order.QTXT)) fw.DeleteOrder(order.OrderID);
    }
    public RemoteControlModel(IMainModel tradesModel) {
      Instruments = new ObservableCollection<string>(new[] { "EUR/USD", "USD/JPY" });
      this.MasterModel = tradesModel;
      if (!IsInDesigh) {
        fw = new FXW(MasterModel.CoreFX);
        fw.TradeRemoved += fw_TradeRemoved;
        fw.TradeAdded += fw_TradeAdded;
        fw.OrderAdded += fw_OrderAdded;
        fw.Error += fw_Error;
        MasterModel.CoreFX.LoggedInEvent += CoreFX_LoggedInEvent;
        MasterModel.CoreFX.LoggedOffEvent += CoreFX_LoggedOffEvent;

        GlobalStorage.Context.ObjectMaterialized += new ObjectMaterializedEventHandler(Context_ObjectMaterialized);
        GlobalStorage.Context.ObjectStateManager.ObjectStateManagerChanged += new System.ComponentModel.CollectionChangeEventHandler(ObjectStateManager_ObjectStateManagerChanged);

        var xmlString = File.ReadAllText(@"ClosedTrades.txt");
        var x = XElement.Parse("<x>" + xmlString + "</x>");
        var nodes = x.Nodes().ToArray();
        foreach (XElement node in nodes.Reverse().Take(150)) {
          var trade = new Trade();
          trade.FromString(node);
          if (trade.GrossPL < 0 && !tradesWithLoss.Any(t => t.Id == trade.Id))
            tradesWithLoss.Add(trade);
        }

      }
    }
    ~RemoteControlModel() {
      if (fw != null) {
        fw.TradeRemoved -= fw_TradeRemoved;
        fw.TradeAdded -= fw_TradeAdded;
        fw.OrderAdded += fw_OrderAdded;
        fw.Error -= fw_Error;
      }
      MasterModel.CoreFX.LoggedInEvent -= CoreFX_LoggedInEvent;
      MasterModel.CoreFX.LoggedOffEvent -= CoreFX_LoggedOffEvent;
    }
    #endregion

    #region Event Handlers
    void ObjectStateManager_ObjectStateManagerChanged(object sender, System.ComponentModel.CollectionChangeEventArgs e) {
      var tm = e.Element as Models.TradingMacro;
      if (tm != null) {
        if (tm.EntityState == System.Data.EntityState.Detached)
          tm.PropertyChanged -= TradingMacro_PropertyChanged;
        else if (tm.EntityState == System.Data.EntityState.Added)
          InitTradingMacro(tm);
      }
    }

    void Context_ObjectMaterialized(object sender, ObjectMaterializedEventArgs e) {
      var tm = e.Entity as Models.TradingMacro;
      if (tm == null) return;
      InitTradingMacro(tm);
    }

    void TradingMacro_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
      try {
        var tm = sender as Models.TradingMacro;
        var propsToHandle = Lib.GetLambdas(() => tm.Pair, () => tm.TradingRatio, () => tm.Lots, () => tm.Stop, () => tm.Limit);
        if (propsToHandle.Contains(e.PropertyName)) SetLotSize(tm, fw.GetAccount());
        if (e.PropertyName == Lib.GetLambda(() => tm.LimitBar)) SetLimitByBar(tm);
        if (e.PropertyName == Lib.GetLambda(() => tm.OverlapToStop)) LoadRates(tm.Pair);
        if (e.PropertyName == Lib.GetLambda(() => tm.CurrentLoss) ) GlobalStorage.Context.SaveChanges();
      } catch (Exception exc) { Log = exc; }
    }

    void CoreFX_LoggedInEvent(object sender, EventArgs e) {
      try {
        InitInstruments();
        fw.PriceChanged += fw_PriceChanged;
        //foreach (var trade in fw.GetTrades()) {
        //  var prevLoss = getChainGross(trade) - trade.GrossPL;
        //  if (prevLoss != 0) GetTradingMacro(trade.Pair).CurrentLoss = prevLoss;
        //}
        MasterModel.CurrentLoss = CurrentLoss;
        foreach (var tm in TradingMacrosCopy)
          tm.CurrentLot = fw.GetTrades().Where(t => t.Pair == tm.Pair).Sum(t => t.Lots);
      } catch (Exception exc) { MessageBox.Show(exc + ""); }
    }

    void CoreFX_LoggedOffEvent(object sender, EventArgs e) {
      fw.PriceChanged -= fw_PriceChanged;
    }

    Dictionary<string, ThreadScheduler> _PriceChangedChedulers = new Dictionary<string, ThreadScheduler>();
    ThreadScheduler GetPriceChanceScheduler(string pair) {
      if (!_PriceChangedChedulers.ContainsKey(pair)) 
        _PriceChangedChedulers.Add(pair, new ThreadScheduler(() => RunPrice(pair), (s, e) => Log = e.Exception));
      return _PriceChangedChedulers[pair];
    }

    void fw_PriceChanged(Bars.Price Price) {
      var pc = GetPriceChanceScheduler(Price.Pair);
      if (!pc.IsRunning) pc.Run();
    }

    private void RunPrice(string pair) {
      try {
        var tm = GetTradingMacro(pair);
        if (tm == null) return;
        var tl = GetTickLoader(pair);
        if (!tl.IsRunning) tl.Run();
        var summary = fw.GetSummary(pair);
        var account = fw.GetAccount();
        tm.Net = summary != null ? summary.NetPL : (double?)null;
        tm.BalanceOnStop = account.Balance + tm.StopAmount.GetValueOrDefault();
        tm.BalanceOnLimit = account.Balance + tm.LimitAmount.GetValueOrDefault();
        SetLotSize(tm, account);
        CheckTrades(account.Trades);
        ProcessPendingOrders(pair);
      } catch (Exception exc) { Log = exc; }
    }


    void fw_TradeAdded(Trade trade) {
      GetTradingMacro(trade.Pair).CurrentLot = fw.GetTrades().Where(t => t.Pair == trade.Pair).Sum(t => t.Lots);
    }


    List<string> pendingFxOrders = new List<string>();
    bool HasPendingFxOrder(string tradeId) {
      return GetPendingFxOrder(tradeId) != null;
    }
    string GetPendingFxOrder(string tradeId) {
      return pendingFxOrders.SingleOrDefault(s => s == tradeId);
    }
    void fw_OrderAdded(Order order) {
      var po = GetPendingFxOrder(order.QTXT);
      if (po != null) 
        new ThreadScheduler(TimeSpan.FromSeconds(15), ThreadScheduler.infinity, () => pendingFxOrders.Remove(po));
    }

    void fw_Error(object sender, Order2GoAddIn.ErrorEventArgs e) {
      Log = e.Error;
    }




    void fw_TradeRemoved(Trade trade) {
      try {
        RemoveEntryOrder(trade.Id);
        #region Locals
        var pair = trade.Pair;
        var tm = GetTradingMacro(pair);
        var totalGross = tm.CurrentLoss + trade.GrossPL;
        tm.CurrentLoss = Math.Min(0, totalGross);
        if (tm.Lots == 0) return;
        tm.FreezLimit = tm.FreezStop = false;
        var limitBar = tm.LimitBar;
        bool isBuy = !trade.IsBuy;
        #endregion
        if (totalGross < 0) {
          tradesWithLoss.Add(trade);
          var ratesForStop = ratesByPair[pair].Union(new[] { new Rate(fw.GetPrice(pair), false) }).ToArray();
          var stop = GetStopByFractal(pair, ratesByPair[pair], isBuy);
          var limit = tm.Limit;
          var lot = CalculateLot(tm, totalGross);
          if (HasTradeToReverse(trade.Id))
            OpenTrade(isBuy, pair, lot, limit, 0, stop, trade.Id);
        } else {
          if( tm.IsReverseOnProfit )
            OpenTrade(tm, isBuy);
        }
        MasterModel.CurrentLoss = CurrentLoss;
      } catch (Exception exc) {
        Log = exc;
      }
      //fw.FixOrderOpen(trade.Pair, !trade.IsBuy, lot, limit, stop, trade.GrossPL < 0 ? trade.Id : "");
    }

    private int CalculateLot(Models.TradingMacro tm) {
      return CalculateLot(tm, tm.CurrentLoss);
    }
    private int CalculateLot(Models.TradingMacro tm, double totalGross) {
      return fw.MoneyAndPipsToLot(totalGross.Abs(), tm.Limit, tm.Pair) + tm.LotSize;
    }
    #endregion

    #region Rate Loading
    void LoadTicks(string pair) {
      return;
      if (!IsLoggedIn) return;
      try {
        var ticks = fw.GetTicks(pair, 300);
        ticksByPair[pair] = ticks;
        var regress = Lib.Regress(ticks.Select(t => t.PriceAvg).ToArray(), 1);
        GetTradingMacro(pair).Angle = regress[1];
      } catch (Exception exc) {
        Log = exc;
      }
    }
    void LoadRates(string pair) {
      if (IsLoggedIn) {
        var sw = Stopwatch.StartNew();
        var dateEnd = fw.ServerTime.Round();
        var dateStart = dateEnd.AddHours(-1);
        var rates = ratesByPair[pair];
        fw.GetBars(pair, 1, dateStart, dateEnd,ref rates);
        Debug.WriteLine("GetRates:{0:n} ms", sw.ElapsedMilliseconds);
        rates = rates.Where(r => r.StartDate >= dateStart).ToList();
        ratesByPair[pair] = rates;
        FillOverlaps(pair, rates);
        var tm = GetTradingMacro(pair);
        tm.LastRateTime = rates.Max(r => r.StartDate);
        int corridorMinutes;
        tm.Corridornes = ScanCorridors(pair, rates.ToArray().Reverse().Take(60 * 3).ToArray(),out corridorMinutes);
        tm.CorridorMinutes = corridorMinutes;
        SetLimitByBar(GetTradingMacro(pair));
        Debug.WriteLine("LoadRates:{0:n} ms", sw.ElapsedMilliseconds);
      }
      new ThreadScheduler(TimeSpan.FromMinutes(1), ThreadScheduler.infinity, () => LoadRates(pair), (s, e) => { });
    }
    int MinutesBack(Models.TradingMacro tm) { return tm.CorridorMinutes; }
    double ScanCorridors(string pair, IEnumerable<Rate> rates, out int minutes) {
      var tm = GetTradingMacro(pair);
      Dictionary<int, double> corridornesses = new Dictionary<int, double>();
      for (minutes = tm.Overlap.ToInt() + tm.Overlap5; minutes < rates.Count(); minutes++) {
        corridornesses.Add(minutes, ScanCorridor(rates.Take(minutes)));
      }
      var corrAverage = corridornesses.Values.Average();
      var corrAfterAverage = corridornesses.Where(c => c.Value > corrAverage).ToArray();
      corrAverage = corrAfterAverage.Average(c=>c.Value);
      corrAfterAverage = corrAfterAverage.Where(c => c.Value > corrAverage).ToArray();
      corrAverage = corrAfterAverage.Average(c => c.Value);
      corrAfterAverage = corrAfterAverage.Where(c => c.Value > corrAverage).ToArray();
      var corr = corrAfterAverage.OrderBy(c => c.Key).Last();
      minutes = corr.Key;
      return corr.Value;
    }
    double ScanCorridor(IEnumerable<Rate> rates) {
      var averageHigh = rates.Average(r => r.PriceHigh);
      var averageLow = rates.Average(r => r.PriceLow);
      var count = 0.0;
      foreach (var rate in rates)
        if (rate.PriceLow <= averageHigh && rate.PriceHigh >= averageLow) count++;
      return count / rates.Count();
    }

    private void FillOverlaps(string pair, IEnumerable<Rate> rates) {
      var ratesOverlap = rates.ToArray().Reverse().ToArray();
      ratesOverlap.FillOverlaps();
      var overlapAverage = ratesOverlap.Select(r => r.Overlap).Average();
      var tm = GetTradingMacro(pair);
      var highOverlapPeriod = 5;
      var mb = MinutesBack(tm);
      tm.Overlap = Math.Ceiling(overlapAverage.TotalMinutes).ToInt();
      tm.Overlap5 = Math.Ceiling(rates.ToArray().GetMinuteTicks(highOverlapPeriod).OrderBarsDescending().ToArray().FillOverlaps().Where(r => r.Overlap != TimeSpan.Zero).Select(r => r.Overlap).Average().TotalMinutes / highOverlapPeriod).ToInt();
      var trade = fw.GetTrade(pair);
      if (trade != null && mb < MinutesBack(tm))
          fw.FixOrderSetStop(trade.Id, 0, "");
    }

    void SetLimitByBar(Models.TradingMacro tm) {
      tm.Limit = fw.InPips(tm.Pair, 
        ratesByPair[tm.Pair].GetMinuteTicks(tm.LimitBar).OrderBarsDescending().Take(MinutesBack(tm)).Average(r => r.Spread)).Round(1);
      tm.Stop = -tm.Limit;
    }
    #endregion

    #region Helpers

    #region OpenTrade
    private void OpenTrade(Models.TradingMacro tm, bool buy) {
      var lot = tm.TradeAmount;
      if (lot == 0) return;
      var pair = tm.Pair;
      var stop = GetStopByFractal(tm.Pair, buy);
      OpenTrade(buy, pair, lot, tm.Limit, 0, stop, "");
    }

    private void OpenTrade(bool buy, string pair, int lot, double limitInPips, double stopInPips, double stop, string remark) {
      var price = fw.GetPrice(pair);
      var limit = limitInPips == 0 ? 0 : buy ? price.Ask + fw.InPoints(pair, limitInPips) : price.Bid - fw.InPoints(pair, limitInPips);
      if (stop == 0 && stopInPips != 0)
        stop = buy ? price.Bid + fw.InPoints(pair, stopInPips) : price.Ask - fw.InPoints(pair, stopInPips);
      fw.FixOrderOpen(pair, buy, lot, limit, stop, remark);
    }
    #endregion

    void ProcessPendingOrders(string pair) {
      var tm = GetTradingMacro(pair);
      tm.PendingSell = HasPendingOrder(pair,false);
      if (tm.PendingSell) {
        var pt = GetPendingTrade(pair, false);
        if (pt.Condition()) {
          RemovePendingOrder(pair, false);
          pt.OpenTradeCommand();
        }
      }
      tm.PendingBuy = HasPendingOrder(pair, true);
      if (tm.PendingBuy) {
        var pt = GetPendingTrade(pair, true);
        if (pt.Condition()) {
          RemovePendingOrder(pair, true);
          pt.OpenTradeCommand();
        }
      }
    }

    #region Get (stop/limit)
    private double GetStopByFractal(string pair, bool isBuy) {
      return GetStopByFractal(pair, ratesByPair[pair], isBuy);
    }
    private double GetStopByFractal(string pair, IEnumerable<Rate> rates, bool isBuy) {
      return GetStopByFractal(0,pair, rates, isBuy);
    }
    private double GetStopByFractal(double stopCurrent, string pair, IEnumerable<Rate> rates, bool isBuy) {
      if (rates.Count() == 0) return stopCurrent;
      var tm = GetTradingMacro(pair);
      var stopSlack = rates.Average(r => r.Spread);
      var ratesForStop = rates.Where(r=>r.Spread <= stopSlack*2) .OrderBarsDescending().Take(MinutesBack(tm)).ToArray();
      var stop = isBuy ? ratesForStop.Min(r => r.BidLow) - stopSlack : ratesForStop.Max(r => r.AskHigh) + stopSlack;
      var price = fw.GetPrice(pair);
      if (isBuy && stop >= price.Bid) stop = price.Bid - stopSlack;
      if (!isBuy && stop <= price.Ask) stop = price.Ask + stopSlack;
      return stop;
    }
    private double GetLimitByFractal(Trade trade, IEnumerable<Rate> rates) {
      string pair = trade.Pair;
      bool isBuy = trade.IsBuy;
      var tm = GetTradingMacro(pair);
      var slack = fw.InPoints(pair, tm.Limit);// rates.Reverse().Take((tm.Overlap + tm.Overlap5).ToInt()).Average(r => r.Spread);
      var ratesForLimit = rates.OrderBarsDescending().Take(MinutesBack(tm)).ToArray();
      var price = fw.GetPrice(pair);
      var limit = isBuy ? Math.Max(trade.Open,ratesForLimit.Max(r => r.BidHigh)) + slack 
        : Math.Min(trade.Open, ratesForLimit.Min(r => r.AskLow)) - slack;
      if (isBuy && limit <= price.Bid) return 0;
      if (!isBuy && limit >= price.Ask) return 0;
      return limit;
    }
    double GetLimit(Trade trade) {
      var limitInPoints = fw.InPoints(trade.Pair, GetTradingMacro(trade.Pair).Limit);
      return Math.Round(trade.IsBuy ? trade.Open + limitInPoints : trade.Open - limitInPoints, fw.GetDigits(trade.Pair));
    }
    #endregion

    private void CheckTrades(Trade[] trades) {
      foreach (var trade in trades) {
        var tm = GetTradingMacro(trade.Pair);
        var round = fw.GetDigits(trade.Pair);
        var stopNew = Math.Round(GetStopByFractal(trade.Pair, ratesByPair[trade.Pair], trade.IsBuy), round);
        var stopOld = Math.Round(trade.Stop, round);
        if (!tm.FreezStop && stopNew != stopOld)
          if (trade.Stop == 0 || trade.IsBuy && stopNew > stopOld || !trade.IsBuy && stopNew < stopOld)
            fw.FixCreateStop(trade.Id, stopNew, "");
        if (!tm.FreezLimit) {
          var limitNew = Math.Round(GetLimitByFractal(trade, ratesByPair[trade.Pair]), round);
          if (limitNew != 0) {
            var limitOld = Math.Round(trade.Limit, round);
            if (limitNew != limitOld)
              fw.FixCreateLimit(trade.Id, limitNew, "");
          }
        }
        if (trade.StopAmount != 0 && !HasPendingFxOrder(trade.Id)) CreateEnterOrder(trade);
        if (trade.Lots >= tm.LotSize*10 && tm.CurrentLoss < 0 && trade.LimitAmount >= tm.CurrentLoss.Abs() * tm.LimitBar) tm.FreezLimit = true;
      }
      if (!CleanEntryOrderScheduler.IsRunning) CleanEntryOrderScheduler.Run();
    }



    #region Entry Orders
    Order GetEntryOrder(string tradeId) { return fw.GetOrders("").SingleOrDefault(o => o.QTXT == tradeId); }
    void RemoveEntryOrder(string tradeId) {
      var order = GetEntryOrder(tradeId);
      if (order != null)
        new ThreadScheduler(TimeSpan.FromSeconds(2), ThreadScheduler.infinity, () => fw.DeleteOrder(order.OrderID), (s, e) => Log = e.Exception);
    }
    void CreateEnterOrder(Trade trade) {
      try {
        var order = GetEntryOrder(trade.Id);
        var period = fw.GetDigits(trade.Pair);
          string pair = trade.Pair;
          bool isBuy = !trade.IsBuy;
          var tm = GetTradingMacro(pair);
          var totalLoss = tm.CurrentLoss + trade.StopAmount;
          int lot = totalLoss < 0 ? -fw.MoneyAndPipsToLot(totalLoss, tm.Limit, pair) : tm.TradeAmount;
          var round = fw.GetDigits(pair);
          if (order == null) {
            if (!HasPendingFxOrder(trade.Id))
              pendingFxOrders.Add(trade.Id);
            fw.FixOrderOpenEntry(pair, isBuy, lot, trade.Stop, 0, 0, trade.Id);
          } else {
            if (order.Rate.Round(period) != trade.Stop.Round(period))
              fw.ChangeOrderRate(order.OrderID, trade.Stop.Round(period));
            if (order.Lot != lot)
              fw.ChangeOrderAmount(order.OrderID, lot);
            if (order.Limit.Round(period) != tm.Limit.Round(period))
              fw.ChangeEntryOrderPeggedLimit(order.OrderID, tm.Limit.Round(period));
          }
      } catch (Exception exc) { Log = exc; }
    }
    #endregion



    #region Child trade helpers
    double getChainPL(Trade trade, ref int count) {
      if (trade == null) return 0;
      count++;
      return trade.PL + getChainPL(tradesWithLoss.SingleOrDefault(t => t.Id == trade.Remark.Remark), ref count);
    }
    double getChainGross(Trade trade) {
      if (trade == null) return 0;
      return trade.GrossPL + getChainGross(tradesWithLoss.SingleOrDefault(t => t.Id == trade.Remark.Remark));
    }

    void SetLotSize(Models.TradingMacro tm, Account account) {
      if (IsLoggedIn) {
        tm.LotSize = FXW.GetLotstoTrade(account.Balance, fw.Leverage(tm.Pair), tm.TradingRatio, fw.MinimumQuantity);
        var stopAmount = 0.0;
        var limitAmount = 0.0;
        foreach (var trade in fw.GetTrades().Where(t => t.Pair == tm.Pair)) {
          stopAmount += trade.StopAmount;
          limitAmount += trade.LimitAmount;
        }
        tm.StopAmount = stopAmount;
        tm.LimitAmount = limitAmount;
      }
    }
    #endregion

    #region Init ...
    private void InitInstruments() {
      GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(new Action(() => {
        while (Instruments.Count > 0) Instruments.RemoveAt(0);
        fw.GetOffers().Select(o => o.Pair).ToList().ForEach(i => Instruments.Add(i));
        RaisePropertyChangedCore("TradingMacros");
      }));
    }
    private void InitTradingMacro(Models.TradingMacro tm) {
      tm.PropertyChanged += TradingMacro_PropertyChanged;
      if (!ratesByPair.ContainsKey(tm.Pair)) {
        ratesByPair.Add(tm.Pair, new List<Rate>());
      }
      TradingMacro_PropertyChanged(tm, new System.ComponentModel.PropertyChangedEventArgs("Pair"));
    }
    #endregion

    #endregion

  }
}
