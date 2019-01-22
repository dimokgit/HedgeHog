using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HedgeHog.Shared;
using IBApi;
using HedgeHog;
using System.Runtime.CompilerServices;
using static EpochTimeExtensions;
using IBApp;
using HedgeHog.Core;
using System.Collections.Specialized;
using System.Configuration;
using MongoDB.Bson;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using ContDetHandler = System.Action<IBSampleApp.messages.ContractDetailsMessage>;
using OptionsChainHandler = System.Action<IBSampleApp.messages.SecurityDefinitionOptionParameterMessage>;
using TickPriceHandler = System.Action<IBApp.TickPriceMessage>;
using OptionPriceHandler = System.Action<IBApp.TickOptionMessage>;
using ReqSecDefOptParams = System.IObservable<IBSampleApp.messages.SecurityDefinitionOptionParameterMessage>;
using ErrorHandler = System.Action<int, int, string, System.Exception>;

using OPTION_CHAIN_OBSERVABLE = System.IObservable<(string exchange, string tradingClass, string multiplier, System.DateTime[] expirations, double[] strikes, string symbol, string currency)>;
using OPTION_CHAIN_DICT = System.Collections.Concurrent.ConcurrentDictionary<string, (string exchange, string tradingClass, string multiplier, System.DateTime[] expirations, double[] strikes, string symbol, string currency)>;

using OPTION_PRICE_OBSERVABLE = System.IObservable<IBApp.TickOptionMessage>;
using PRICE_OBSERVABLE = System.IObservable<(IBApi.Contract contract, double bid, double ask, System.DateTime time)>;
using System.Reactive.Subjects;
using System.Diagnostics;
using static IBApi.IBApiMixins;
using System.Collections.Concurrent;
using System.Reactive;
using IBSampleApp.messages;

namespace IBApp {
  public class IBClientCore :IBClient, ICoreFX {
    #region Configer
    class Configer {
      static NameValueCollection section;
      public static int[] WarningCodes;
      static Configer() {
        section = ConfigurationManager.GetSection("IBSettings") as NameValueCollection;
        var mongoUri = ConfigurationManager.AppSettings["MongoUri"];
        var mongoCollection = ConfigurationManager.AppSettings["MongoCollection"];
        try {
          var codeAnon = new { id = ObjectId.Empty, codes = new int[0] };
          WarningCodes = MongoExtensions.ReadCollectionAnon(codeAnon, mongoUri, "forex", mongoCollection).SelectMany(x => x.codes).ToArray();
        } catch(Exception exc) {
          throw new Exception(new { mongoUri, mongoCollection } + "", exc);
        }
      }
    }

    private const string GENERIC_TICK_LIST = "233,221,236,165";
    #endregion

    #region Fields
    EReaderMonitorSignal _signal;
    private int _port;
    private string _host;
    internal TimeSpan _serverTimeOffset;
    private string _managedAccount;
    public string ManagedAccount { get => _managedAccount; }
    private readonly Action<object> _trace;
    TradingServerSessionStatus _sessionStatus;
    readonly private MarketDataManager _marketDataManager;
    private static int _validOrderId;
    private IObservable<EventPattern<PriceChangedEventArgs>> _priceChangeObservable;
    #endregion

    #region Properties
    public void TraceMe<T>(T v) => _trace(v);
    public Action<object> Trace => _trace;
    public Action<bool, object> TraceIf => (b, o) => { if(b) _trace(o); };
    public Action<object> TraceTemp => o => { };
    private bool _verbose = false;
    public void Verbose(object o) { if(_verbose) _trace(o); }
    public IObservable<EventPattern<PriceChangedEventArgs>> PriceChangeObservable { get => _priceChangeObservable; private set => _priceChangeObservable = value; }
    #endregion

    #region ICoreEX Implementation
    public Contract SetContractSubscription(Contract contract) => contract.SideEffect(c => SetContractSubscription(c, _ => { }));
    void SetContractSubscription(Contract contract, Action<Contract> callback) {
      _marketDataManager.AddRequest(contract, GENERIC_TICK_LIST, callback);
    }
    public void SetSymbolSubscription(string pair) {
      if(pair.IsFuture()) {
        var contract = new Contract { LocalSymbol = pair, SecType = "FUT", Currency = "USD" };
        ReqContractDetailsCached(contract)
          .Take(1)
          .OnEmpty(() => throw new Exception(new { contract, not = "found" } + ""))
          .Subscribe(cd => SetContractSubscription(cd.Contract));
      } else
        SetContractSubscription(ContractSamples.ContractFactory(pair));
    }
    public bool IsInVirtualTrading { get; set; }
    public DateTime ServerTime => DateTime.Now + _serverTimeOffset;
    public event EventHandler<LoggedInEventArgs> LoggedOff;
    public event EventHandler<LoggedInEventArgs> LoggingOff;
    public event PropertyChangedEventHandler PropertyChanged;
    #endregion

    #region Ctor
    public static IBClientCore Create(Action<object> trace) {
      var signal = new EReaderMonitorSignal();
      return new IBClientCore(signal, trace) { _signal = signal };
    }
    public IBClientCore(EReaderSignal signal, Action<object> trace) : base(signal) {
      _trace = trace;
      NextValidId += OnNextValidId;
      Error += OnError;
      ConnectionClosed += OnConnectionClosed;
      ConnectionOpend += OnConnectionOpend;
      CurrentTime += OnCurrentTime;
      _marketDataManager = new MarketDataManager(this);
      _marketDataManager.PriceChanged += OnPriceChanged;

      //SecurityDefinitionOptionParameter += IBClientCore_SecurityDefinitionOptionParameter;
      //SecurityDefinitionOptionParameterEnd += IBClientCore_SecurityDefinitionOptionParameterEnd;
      //void IBClientCore_SecurityDefinitionOptionParameterEnd(int obj) => throw new NotImplementedException();
      //void IBClientCore_SecurityDefinitionOptionParameter(int arg1, string arg2, int arg3, string arg4, string arg5, HashSet<string> arg6, HashSet<double> arg7) => throw new NotImplementedException();

      PriceChangeObservable = Observable.FromEventPattern<PriceChangedEventArgs>(
        h => PriceChanged += h,
        h => PriceChanged -= h
        )
        .ObserveOn(TaskPoolScheduler.Default)
        .Publish()
        .RefCount();

      void Try(Action a, string source) {
        try {
          a();
        } catch(Exception exc) {
          Trace(new Exception(source, exc));
        }
      }
    }

    #endregion

    #region Req-* Observables
    #region ContractDetails
    IObservable<ContractDetailsMessage> ContractDetailsFactory()
      => Observable.FromEvent<ContDetHandler, ContractDetailsMessage>(
        onNext => (ContractDetailsMessage m) => onNext(m),
        h => ContractDetails += h,
        h => ContractDetails -= h
        )
      //.Spy("ContractDetails")
      ;
    IObservable<ContractDetailsMessage> _ContractDetailsObservable;
    IObservable<ContractDetailsMessage> ContractDetailsObservable =>
      (_ContractDetailsObservable ?? (_ContractDetailsObservable = ContractDetailsFactory()));

    static IScheduler esReqContract = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqContract" });
    IObservable<int> ContractDetailsEndFactory() => Observable.FromEvent<Action<int>, int>(
        onNext => (int a) => onNext(a),
        h => ContractDetailsEnd += h,
        h => ContractDetailsEnd -= h
        )
      .ObserveOn(esReqContract)
      .Publish().RefCount();
    IObservable<int> _ContractDetailsEndObservable;
    IObservable<int> ContractDetailsEndObservable =>
      (_ContractDetailsEndObservable ?? (_ContractDetailsEndObservable = ContractDetailsEndFactory()));
    #endregion

    #region SecurityDefinitionOptionParameterObservable
    ReqSecDefOptParams SecurityDefinitionOptionParameterFactory() => Observable.FromEvent<OptionsChainHandler, SecurityDefinitionOptionParameterMessage>(
        onNext => (SecurityDefinitionOptionParameterMessage m) => onNext(m),
        h => SecurityDefinitionOptionParameter += h,
        h => SecurityDefinitionOptionParameter -= h
        )
      .Publish()
      .RefCount();
    ReqSecDefOptParams _SecurityDefinitionOptionParameterObservable;
    ReqSecDefOptParams SecurityDefinitionOptionParameterObservable =>
      (_SecurityDefinitionOptionParameterObservable ?? (_SecurityDefinitionOptionParameterObservable = SecurityDefinitionOptionParameterFactory()));

    #region SecurityDefinitionOptionParameterEnd
    IObservable<int> SecurityDefinitionOptionParameterEndFactory() => Observable.FromEvent<Action<int>, int>(
      onNext => (int a) => onNext(a),
      h => SecurityDefinitionOptionParameterEnd += h,
      h => SecurityDefinitionOptionParameterEnd -= h
      )
      .Publish()
      .RefCount();
    IObservable<int> _SecurityDefinitionOptionParameterEndObservable;
    IObservable<int> SecurityDefinitionOptionParameterEndObservable =>
      (_SecurityDefinitionOptionParameterEndObservable ?? (_SecurityDefinitionOptionParameterEndObservable = SecurityDefinitionOptionParameterEndFactory()));
    #endregion
    #endregion

    #region TickPrice
    static IScheduler esReqPriceSubscribe = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice", Priority = ThreadPriority.Lowest });
    static IScheduler esReqPriceObserve = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice2", Priority = ThreadPriority.Lowest });
    IObservable<TickPriceMessage> TickPriceFactoryFromEvent()
      => Observable.FromEvent<TickPriceHandler, TickPriceMessage>(
        onNext => (TickPriceMessage m) => onNext(m),
        h => TickPrice += h.SideEffect(_ => Trace($"Subscribed to {nameof(TickPrice)}")),
        h => TickPrice -= h.SideEffect(_ => Trace($"UnSubscribed to {nameof(TickPrice)}"))
        )
      .SubscribeOn(esReqPriceSubscribe)
      .Publish()
      .RefCount()
      .ObserveOn(esReqPriceObserve)
      ;
    IObservable<TickPriceMessage> _TickPriceObservable;
    internal IObservable<TickPriceMessage> TickPriceObservable =>
      (_TickPriceObservable ?? (_TickPriceObservable = TickPriceFactoryFromEvent()));
    #endregion

    #region OptionPrice
    OPTION_PRICE_OBSERVABLE OptionPriceFactoryFromEvent()
      => Observable.FromEvent<OptionPriceHandler, TickOptionMessage>(
        onNext => (TickOptionMessage m) => onNext(m),
        h => TickOptionCommunication += h/*.SideEffect(_ => Trace($"Subscribed to {nameof(TickPrice)}"))*/,
        h => TickOptionCommunication -= h/*.SideEffect(_ => Trace($"UnSubscribed to {nameof(TickPrice)}"))*/
        )
      .ObserveOn(esReqPriceSubscribe)
      .Publish()
      .RefCount();
    OPTION_PRICE_OBSERVABLE _OptionPriceObservable;
    internal OPTION_PRICE_OBSERVABLE OptionPriceObservable => (_OptionPriceObservable ?? (_OptionPriceObservable = OptionPriceFactoryFromEvent()));
    #endregion

    #region Error
    static IScheduler esError = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Error", Priority = ThreadPriority.Lowest });
    IObservable<(int id, int errorCode, string errorMsg, Exception exc)> ErrorFactory() => Observable.FromEvent<ErrorHandler, (int id, int errorCode, string errorMsg, Exception exc)>(
      onNext => (int id, int errorCode, string errorMsg, Exception exc) => onNext((id, errorCode, errorMsg, exc)),
      h => Error += h/*.SideEffect(_ => Trace($"Subscribed to {nameof(Error)}"))*/,
      h => Error -= h/*.SideEffect(_ => Trace($"UnSubscribed to {nameof(Error)}"))*/
      ).ObserveOn(esError)
      //.Spy("Error")
      .Publish()
      .RefCount();
    IObservable<(int id, int errorCode, string errorMsg, Exception exc)> _ErrorObservable;
    public IObservable<(int id, int errorCode, string errorMsg, Exception exc)> ErrorObservable =>
      (_ErrorObservable ?? (_ErrorObservable = ErrorFactory()));
    #endregion
    #endregion

    #region Req-* functions
    int NextReqId() => ValidOrderId();

    public IObservable<ContractDetails> ReqContractDetailsCached(string symbol) => ReqContractDetailsCached(symbol.ContractFactory());
    public IObservable<ContractDetails> ReqContractDetailsCached(Contract contract) {
      if(contract.Symbol == "VX" && contract.Exchange == "GLOBEX")
        Debugger.Break();
      return contract.FromDetailsCache()
        .ToArray()
        .ToObservable()
        //.Do(cd => Trace(new { contractCache = cd.Contract }))
        //.ToArray()
        .Concat(Observable.Defer(() => {
          return ReqContractDetailsAsync(contract).Do(cd => Debug.WriteLine(new { contractAsync = cd.Contract }));
        }))
        .Take(1)
        //.SelectMany(a => a)
        ;
    }

    static ConcurrentDictionary<string, IObservable<ContractDetails>> _reqContractDetails = new ConcurrentDictionary<string, IObservable<ContractDetails>>();
    public IObservable<ContractDetails> ReqContractDetailsAsync(Contract contract) {
      if(contract.Symbol == "VX" && contract.Exchange == "GLOBEX")
        Debugger.Break();
      var key = $"{contract.Symbol.IfEmpty(contract.LocalSymbol)}:{contract.SecType}:{contract.Exchange}:{contract.Currency}:{contract.LastTradeDateOrContractMonth}:{contract.Right}:{contract.Strike}";
      lock(_reqContractDetails) {
        if(_reqContractDetails.TryGetValue(key, out var o)) return o;
        var reqId = NextReqId();
        Trace($"{nameof(ReqContractDetailsAsync)}:{key} Start");
        var cd = WireToError<ContractDetailsMessage>(
          reqId,
          ContractDetailsObservable,
          ContractDetailsEndObservable,
          (int rid) => new ContractDetailsMessage(rid, (ContractDetails)null),
          t => t.RequestId,
          t => t.ContractDetails != null,
          error => Trace($"{nameof(ReqContractDetailsAsync)}:{contract}[{new { contract.Exchange, LastTradeDate = contract.LastTradeDateOrContractMonth, contract.Strike }}]-{error}"),
          () => TraceIf(DataManager.DoShowRequestErrorDone || _verbose, $"{nameof(ReqContractDetailsAsync)} {reqId} Error done")
          )
          .Distinct(t => t.ContractDetails.Contract.ConId)
          .ToArray()
          .Do(a => a.ForEach(t => t.ContractDetails.AddToCache()))
          .Do(_ => Trace($"{nameof(ReqContractDetailsAsync)}:{key} found:{_.Length} contracts"))
          .SelectMany(a => a.Select(t => t.ContractDetails))
          .Replay().RefCount()
        //.Select(t => t.cd.SideEffect(d => Verbose($"Adding {d.Contract} to cache")).AddToCache())
        //.ObserveOn(esReqCont)
        //.Do(t => Trace(new { ReqContractDetailsImpl = t.reqId, contract = t.contractDetails?.Contract, Thread.CurrentThread.ManagedThreadId }))
        ;
        OnReqMktData(() => ClientSocket.reqContractDetails(reqId, contract));
        //Trace(new { ReqContractDetailsImpl = reqId, contract });
        return _reqContractDetails.TryAdd(key, cd) ? cd : _reqContractDetails[key];
      }
    }
    public IObservable<T> WireToError<T>
      (int reqId, IObservable<T> source, IObservable<int> endSubject, Func<int, T> endFactory, Func<T, int> getReqId, Func<T, bool> isNotEnd, Action<(int id, int errorCode, string errorMsg, Exception exc)> onError, Action onEnd) {
      SetRequestHandled(reqId);
      return source
        .TakeUntil(
          endSubject.Where(rid => rid == reqId)
          .Merge(ErrorObservable.Where(e => e.id == reqId).Do(onError).Select(e => e.id))
          )
        .Where(t => getReqId(t) == reqId)
        ;
    }
    (IObservable<T> source, IObserver<T> stopper) StoppableObservable<T>
      (IObservable<T> source, Func<T, bool> isNotEnd) {
      var end = new Subject<T>();
      var o = source.Select(t => (id: 1, t))
        .Merge(end.Select(t => (id: 0, t)))
        .TakeWhile(t => t.id > 0)//t => t.Item2 != null)
        .Select(t => t.t)
        ;
      return (o, end);
    }

    public ReqSecDefOptParams ReqSecDefOptParamsAsync(string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId) {
      var reqId = NextReqId();
      var cd = SecurityDefinitionOptionParameterObservable
        .TakeUntil(SecurityDefinitionOptionParameterEndObservable.Where(rid => rid == reqId).Do(_ => TraceTemp("ReqSecDefOptParamsAsync: end")))
        .Where(t => t.ReqId == reqId)
        .TakeWhile(t => t.Expirations != null)
        .Do(t => TraceTemp(new { ReqSecDefOptParamsImpl = t.ReqId, t.TradingClass }))
        //.ObserveOn(esReqCont)
        //.Do(x => _verbous(new { ReqSecDefOptParams = new { Started = x.reqId } }))
        //.Timeout(TimeSpan.FromSeconds(_reqTimeout))
        ;
      //.Subscribe(t => callback(t.contractDetails),exc=> { throw exc; }, () => _verbous(new { ContractDetails = new { Completed = reqId } }));
      OnReqMktData(() => ClientSocket.reqSecDefOptParams(reqId, underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId));
      return cd;
    }

    public static ConcurrentDictionary<(string symbol, DateTime expDate, double strike), bool> OptionChainOldCacheInRun = new ConcurrentDictionary<(string symbol, DateTime expDate, double strike), bool>();

    public static void ClearCache() {
      Contract.Contracts.Clear();
      IBApi.ContractDetails.ClearCache();
      OptionChainCache.Clear();
      OptionChainCache.Clear();
    }

    static ConcurrentDictionary<string, IObservable<(DateTime[] expDate, double[] strike)>> _allStrikesAndExpirations = new ConcurrentDictionary<string, IObservable<(DateTime[] expDate, double[] strike)>>();
    public IObservable<(DateTime[] expirations, double[] strikes)> ReqStrikesAndExpirations(string underSymbol) {
      lock(_allStrikesAndExpirations) {
        if(_allStrikesAndExpirations.TryGetValue(underSymbol, out var cache)) return cache;
        var increments = new[] { 50.0, 10, 5, 2.5, 1 };
        IEnumerable<double> CurrentIncrement(double price) => increments.OrderBy(i => (price * 0.002).Abs(i)).Take(1).Select(i => (price / i).ToInt() * i);
        var newCache = (from under in ReqContractDetailsCached(underSymbol)
                        from price in ReqPriceSafe(under.Contract, 2000, true)
                        from strike in CurrentIncrement(price.ask.Avg(price.bid))
                        let symbol = under.Contract.LocalSymbol
                        from byStrike in ReqOptionChainOldCache(under.Contract.LocalSymbol, DateTime.MinValue
                        , strike.SideEffect(_ => Trace(new { ReqStrikesAndExpirations = $"{underSymbol}:{strike} Start" })), false)
                        let exps = byStrike
                        .SideEffect(_ => Trace(new { ReqStrikesAndExpirations = $"{underSymbol}:{strike} End" }))
                        .Select(o => o.Expiration).Distinct().OrderBy(ex => ex).ToArray()
                        from exp in exps.Take(1)
                        from byExp in ReqOptionChainOldCache(under.Contract.LocalSymbol, exp, 0, false)
                        let strikes = byExp.Select(o => o.Strike).Distinct().OrderBy(st => st).ToArray()
                        select (exps, strikes)).Replay().RefCount();
        return _allStrikesAndExpirations.TryAdd(underSymbol, newCache) ? newCache : _allStrikesAndExpirations[underSymbol];
      }
    }
    static ConcurrentDictionary<(string symbol, DateTime expDate, double strike), IObservable<Contract[]>> OptionChainOldCache = new ConcurrentDictionary<(string symbol, DateTime expDate, double strike), IObservable<Contract[]>>();
    public IObservable<Contract[]> ReqOptionChainOldCache(string symbol, DateTime expDate, double strike = 0, bool waitForAllStrikes = true) {
      var key = (symbol, expDate, strike);
      lock(OptionChainOldCache) {
        if(OptionChainOldCache.TryGetValue(key, out var o)) return o;
        var newCache = ReqOptionChainOldAsync(symbol, expDate, strike, waitForAllStrikes).ToArray().Replay().RefCount();
        //Trace($"{nameof(ReqOptionChainOldCache)}:{new { key }}");
        return newCache
          .Do(nc => {
            if(nc.Any()) OptionChainOldCache.TryAdd(key, newCache);
          });
      }
    }
    public IObservable<Contract> ReqOptionChainOldAsync(string symbol, DateTime expirationDate, double strike, bool waitForAllStrikes) {
      Passager.ThrowIf(() => expirationDate.IsMin() && strike == 0, new { expirationDate, strike } + "");
      var fopDate = expirationDate;
      Trace(new { fopDate = fopDate.ToShortDateString(), symbol, strike });
      var isVIX = true;
      Contract MakeFutureContract(ContractDetails cd, string twsDate) => new Contract { Symbol = cd.MarketName, SecType = "FOP", Exchange = cd.Contract.Exchange, Currency = "USD", LastTradeDateOrContractMonth = twsDate, Strike = strike };
      Contract MakeIndexContract(string s, string twsDate) => new Contract { Symbol = s, SecType = "OPT", Currency = "USD", LastTradeDateOrContractMonth = twsDate, Strike = strike };
      Contract MakeStockContract(string twsDate) => new Contract { Symbol = symbol, SecType = "OPT", Exchange = "SMART", Currency = "USD", LastTradeDateOrContractMonth = twsDate, Strike = strike };
      Contract MakeContract(ContractDetails cd, string twsDate) =>
        (isVIX = cd.UnderSymbol == "VIX") || cd.Contract.IsIndex ? MakeIndexContract(cd.UnderSymbol, twsDate)
        : cd.Contract.IsFuture ? MakeFutureContract(cd, twsDate)
        : MakeStockContract(twsDate);

      return (from cds in ReqCDs(fopDate).Take(fopDate.IsMin() ? int.MaxValue : 1)
              from cd in cds
              from vix in (isVIX ? ReqContractDetailsCached("VIX") : new ContractDetails[0].ToObservable()).ToArray()
              select cd.AddToCache().Contract);

      IObservable<ContractDetails[]> ReqCDs(DateTime fd) =>
        (from stkExp in waitForAllStrikes ? ReqStrikesAndExpirations(symbol).Select(t => t.expirations) : Observable.Return(new DateTime[0])
         from under in ReqContractDetailsCached(symbol)
         from exp in stkExp.DefaultIfEmpty(fd).Where(ex => ex >= fd.Date).OrderBy(ex => ex).Take(1)
         let tws = !fd.IsMin() ? exp.ToTWSDateString() : ""
         let c = MakeContract(under, tws)
         from cd in ReqContractDetailsAsync(c)
         select cd)
         .ToArray()
         .Do(__ => {
           //Debugger.Break();
         });
    }
    public IObservable<Contract> ReqCurrentOptionsAsync
      (string symbol, double price, bool[] isCalls, int expirationDaysSkip, int expirationsCount, int strikesCount) {
      var expStartDate = ServerTime.Date.AddBusinessDays(expirationDaysSkip);
      return (
        //from exps in ReqStrikesAndExpirations(symbol)
        //from exp in exps.expirations.OrderBy(d => d).Where(d => d >= expStartDate).Take(1)
        from och in ReqOptionChainOldCache(symbol, expStartDate)
        select och
        ).Select(a => a.OrderBy(c => c.Strike.Abs(price)).ThenBy(c => c.Right).Select((o, i) => (i, o))
          .ToArray())
          .SelectMany(a => a.OrderBy(t => t.i).ThenBy(t => t.o.Strike).Select(t => t.o))
          .Take(strikesCount * expirationsCount);
    }

    public IObservable<Contract> ReqCurrentOptionsFastAsync
      (string symbol, double price, bool[] isCalls, int expirationDaysSkip, int expirationsCount, int strikesCount) {
      var expStartDate = ServerTime.Date.AddWorkingDays(expirationDaysSkip);
      var x = (
                from t in ReqOptionChainCache(symbol)
                from strike in t.strikes.OrderBy(st => st.Abs(price)).Take(strikesCount * 2).Select((s, i) => (s, i)).ToArray()
                from isCall in isCalls
                from expiration in t.expirations.SkipWhile(exd => exd < expStartDate).Take(expirationsCount).ToArray()
                select (t.tradingClass, expiration, strike, isCall)
                )
                .ToArray();

      return (
        from ts in x
        from t in ts.OrderBy(t => t.strike.i).ThenBy(t => t.expiration).ToArray()
        let option = MakeOptionSymbol(t.tradingClass, t.expiration, t.strike.s, t.isCall)
        from o in ReqContractDetailsCached(option).Synchronize()
        select (t.strike.i, o: o.AddToCache().Contract)
       )
       .ToArray()
       .TakeUntil(DateTimeOffset.Now.AddSeconds(1))
       .Concat(Observable.Defer(() => ReqOptionChainOldCache(symbol, expStartDate)
          .Select(a => a.OrderBy(c => c.Strike.Abs(price)).ThenBy(c => c.Right).Select((o, i) => (i, o))
          .ToArray())
       ))
       .SelectMany(a => a.OrderBy(t => t.i).ThenBy(t => t.o.Strike).Select(t => t.o))
       .Take(strikesCount * expirationsCount);
    }

    public static OPTION_CHAIN_DICT OptionChainCache = new OPTION_CHAIN_DICT(StringComparer.OrdinalIgnoreCase);
    public OPTION_CHAIN_OBSERVABLE ReqOptionChainCache(string symbol) =>
      OptionChainCache.TryGetValue(symbol)
      .ToArray()
      .Do(x => {
      })
      .ToObservable()
      .Concat(Observable.Defer(() => ReqOptionChainAsync(symbol)))
      .Take(1);

    OPTION_CHAIN_OBSERVABLE ReqOptionChainAsync(string symbol) =>
      ReqOptionChainsAsync(symbol)
      .ToArray()
      .SelectMany(a => a.OrderBy(x => x.expirations.Min()).ThenByDescending(x => x.strikes.Length))
      .Where(a => a.exchange == "SMART")
      .Take(1)
      .Do(t => OptionChainCache.TryAdd(symbol, t));

    public OPTION_CHAIN_OBSERVABLE ReqOptionChainsAsync(string symbol) =>
      from cd in ReqContractDetailsCached(symbol.ContractFactory())
        //from price in TryGetPrice(symbol)
        //  .Where(p => p.Bid > 0 && p.Ask > 0)
        //  .Select(p => p.Average)
        //  .ToObservable()
        //  .Concat(Observable.Defer(() => ReqPriceMarket(cd.Contract)))
        //  .Take(1)
      from och in ReqSecDefOptParamsAsync(cd.Contract.LocalSymbol, cd.Contract.IsFuture ? cd.Contract.Exchange : "", cd.Contract.SecType, cd.Contract.ConId)
      select (och.Exchange, och.TradingClass, och.Multiplier, expirations: och.Expirations.Select(e => e.FromTWSDateString(DateTime.MaxValue)).ToArray(), strikes: och.Strikes.ToArray(), symbol = cd.Contract.Symbol, currency: cd.Contract.Currency);

    enum TickType { Bid = 1, Ask = 2, MarketPrice = 37 };

    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceSafe(Contract contract, double timeoutInSeconds, bool useErrorHandler, double defaultPrice) =>
      ReqPriceSafe(contract, timeoutInSeconds, useErrorHandler).DefaultIfEmpty((defaultPrice, defaultPrice, DateTime.MinValue, 0));

    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceEmpty() => Observable.Return((0.0, 0.0, DateTime.MinValue, 0.0));
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceSafe(Contract contract, double timeoutInSeconds, bool useErrorHandler, [CallerMemberName] string Caller = "") {
      var c = TryGetPrice(contract).Where(p => p.Ask > 0 && p.Bid > 0).ToArray();
      if(c.Any()) return c
      //.Do(p => Trace($"ReqPriceSafe.Cache:{contract}:{p}"))
      .Select(p => (p.Bid, p.Ask, p.Time, p.GreekDelta)).ToObservable();

      if(contract.IsCombo)
        return ReqPriceComboSafe(contract, timeoutInSeconds, useErrorHandler);

      SetContractSubscription(contract);
      return TickPriceObservable
      .TakeUntil(Observable.Timer(TimeSpan.FromSeconds(timeoutInSeconds)))
      .Select(_ => TryGetPrice(contract).Select(p => (p.Bid, p.Ask, p.Time, p.GreekDelta)).ToArray())
      .Where(t => t.Any())
      .SelectMany(p => p)
      .Where(p => p.Bid > 0 && p.Ask > 0 && p.Time > ServerTime.AddSeconds(-60))
      .Do(p => Verbose($"{nameof(TickPriceObservable)}:{contract}:{p}  <= {Caller}"))
      //.Select(p => (p.Bid, p.Ask, p.Time))
      //.Concat(Observable.Defer(() => ReqPriceComboSafe(contract, timeoutInSeconds, useErrorHandler)))
      .Take(1)
      ;
    }

    public IObservable<(double bid, double ask, DateTime time)> ReqPriceComboSafe_New(Contract combo, double timeoutInSeconds, bool useErrorHandler) {
      double ask((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double delta) price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.ask;
      double bidCalc((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double delta) price) => (cl.leg.Action == "BUY" ? 1 : -1) * price.bid;
      var x = combo.LegsEx()
        .ToObservable()
        .SelectMany(cl => ReqPriceSafe(cl.contract, timeoutInSeconds, useErrorHandler).Select(price => (cl, price)).Take(1))
        .ToArray()
        .Where(a => a.Any())
        .Select(t => {
          var bids = t.Select(t2 => bidCalc(t2.cl, t2.price) * t2.cl.leg.Ratio).ToArray();
          var bid = bids.Sum();
          return (
           bid,
           t.Sum(t2 => ask(t2.cl, t2.price) * t2.cl.leg.Ratio),
           t.Min(t2 => t2.price.time)
           );
        });
      return x;
    }
    public IObservable<(double bid, double ask, DateTime time, double delta)> ReqPriceComboSafe(Contract combo, double timeoutInSeconds, bool useErrorHandler, [CallerMemberName] string Caller = "") {
      //Trace($"{nameof(ReqPriceComboSafe)}:{combo} <= {Caller}");
      double ask((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double) price) => cl.leg.Action == "BUY" ? price.ask : -price.bid;
      double bid((Contract option, ComboLeg leg) cl, (double bid, double ask, DateTime time, double) price) => cl.leg.Action == "BUY" ? price.bid : -price.ask;
      var x = combo.LegsEx()
        .ToObservable()
        .SelectMany(cl => ReqPriceSafe(cl.contract, timeoutInSeconds, useErrorHandler).Select(price => (cl, price)).Take(1))
        .ToArray()
        .Where(a => a.Any())
        .Select(t => (
        t.Sum(t2 => bid(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Sum(t2 => ask(t2.cl, t2.price) * t2.cl.leg.Ratio),
        t.Max(t2 => t2.price.time),
        t.Max(t2 => t2.price.delta)
        ));
      return x;
    }



    public IObservable<double> ReqPriceMarket(string symbol)
      => from cd in ReqContractDetailsCached(symbol.ContractFactory())
         from p in ReqPriceMarket(cd.Contract)
         select p;

    #region ReqMktData Subject
    object _ReqMktDataSubjectLocker = new object();
    ISubject<Action> _ReqMktDataSubject;
    ISubject<Action> ReqMktDataSubject {
      get {
        lock(_ReqMktDataSubjectLocker)
          if(_ReqMktDataSubject == null) {
            _ReqMktDataSubject = new Subject<Action>();
            _ReqMktDataSubject
              .ObserveOn(TaskPoolScheduler.Default)
              .RateLimit(40)
              //.Do(_ => Thread.Sleep(100))
              .Subscribe(s => s(), exc => { });
          }
        return _ReqMktDataSubject;
      }
    }
    public void OnReqMktData(Action p) {
      ReqMktDataSubject.OnNext(p);
    }
    #endregion

    int[] _bidAsk = new int[] { 1, 2, 37 };
    //      .Concat(Observable.Defer(() => ReqContractDetailsCached(symbol.ContractFactory()).SelectMany(cd => ReqPrice(cd.Contract, 1, false)).Select(p => (p.bid, p.ask))).ToArray())

    public PRICE_OBSERVABLE ReqPricesImpl(Contract contract, double timeoutInSeconds, bool useErrorHandler) {
      Trace($"ReqPricesImpl:{contract}");
      var reqId = NextReqId();
      var cd = TickPriceObservable
        .Where(t => t.RequestId == reqId)
        //.Do(x => Trace(new { ReqPrice = new { contract, started = x } }))
        .Where(t => _bidAsk.Contains(t.Field))
        .Scan((contract, bid: 0.0, ask: 0.0, time: DateTime.MinValue)
        , (p, n) => (contract
        , n.Field == 1 || n.Field == 37 && p.bid < 0 ? n.Price : p.bid
        , n.Field == 2 || n.Field == 37 && p.ask < 0 ? n.Price : p.ask
        , ServerTime))
        .Where(p => p.bid > 0 && p.ask > 0)
        .TakeUntil(Observable.Timer(TimeSpan.FromSeconds(timeoutInSeconds)))
        ;
      if(useErrorHandler)
        WatchReqError("ReqPricesImpl", contract, reqId, () => TraceIf(DataManager.DoShowRequestErrorDone, $"ReqPricesImpl: {contract} => {reqId} Error done."));
      OnReqMktData(() => ClientSocket.reqMktData(reqId, contract.ContractFactory(), "232", false, false, null));
      return cd;
    }
    public PRICE_OBSERVABLE ReqPriceCombo(Contract combo, IEnumerable<Contract> oprions, double timeoutInSeconds, bool useErrorHandler) {
      var x = oprions
        .ToObservable()
        .SelectMany(contract => ReqPriceSafe(contract, timeoutInSeconds, useErrorHandler).Take(1))
        .ToArray()
        .SelectMany(prices => prices.Take(1).Select(price => (combo, prices.Sum(p => p.bid), prices.Sum(p => p.ask), ServerTime)));
      return x;
    }

    public IObservable<(Contract contract, double bid, double ask)> ReqOptionPrice(Contract contract) {
      var reqId = NextReqId();
      var cd = OptionPriceObservable
        .Where(t => t.RequestId == reqId)
        .Do(t => Trace($"{nameof(ReqOptionPrice)}:t"))
        .Scan((contract, bid: 0.0, ask: 0.0), (p, n) => (contract, n.Field == 1 ? n.OptPrice : p.bid, n.Field == 2 ? n.OptPrice : p.ask))
        //.Do( x => Trace(new { ReqPrice = new { contract, started = x } }))
        ;
      OnReqMktData(() => ClientSocket.reqMktData(reqId, contract.ContractFactory(), "232", false, false, null));
      return cd;
    }

    public IObservable<double> ReqPriceMarket(Contract contract) {
      var reqId = NextReqId();
      var cd = TickPriceObservable
        //.Do(x => Trace($"{nameof(ReqPriceMarket)}:{new { contract, started = x }}"))
        .Where(t => t.RequestId == reqId && t.Field == 37)
        .Take(1)
        .Select(t => t.Price)
        //.Do( x => Trace(new { ReqPrice = new { contract, started = x } }))
        ;
      WatchReqError(nameof(ReqPriceMarket), contract, reqId, () => { });// Trace($"{nameof(ReqPriceMarket)}: {contract} => {reqId} Error done."));
      OnReqMktData(() => ClientSocket.reqMktData(reqId, contract.ContractFactory(), "221,232", false, false, null));
      return cd;
    }

    private void WatchReqError<T>(T context, Contract contract, int reqId, Action complete) {
      SetRequestHandled(reqId);
      ErrorObservable
      .Where(t => t.id == reqId)
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(2)
      .Merge()
      .Subscribe(t => Trace($"{context}:{contract}:{t}"), complete);
    }
    public void WatchReqError(int reqId, Action<(int id, int errorCode, string errorMsg, Exception exc)> error, Action complete) {
      SetRequestHandled(reqId);
      ErrorObservable
      .Where(t => t.id == reqId)
      .DistinctUntilChanged(e => e.errorCode)
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(1)
      .Merge()
      .Subscribe(error, complete);
    }
    public void WatchReqError(Func<int> reqId, Action<(int id, int errorCode, string errorMsg, Exception exc)> error, Action complete) {
      SetRequestHandled(reqId());
      ErrorObservable
      .Where(t => t.id == reqId())
      .DistinctUntilChanged(e => (reqId(), e.errorCode))
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(1)
      .Merge()
      .Subscribe(error, complete);
    }
    public IObservable<(int id, int errorCode, string errorMsg, Exception exc)> ReqError(Func<int> reqId, Action<(int id, int errorCode, string errorMsg, Exception exc)> error) {
      SetRequestHandled(reqId());
      return ErrorObservable
      .Where(t => t.id == reqId())
      .DistinctUntilChanged(e => (reqId(), e.errorCode))
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(1)
      .Merge()
      .Do(error);
    }
    public void CancelPrice(int reqId) => ClientSocket.cancelMktData(reqId);

    #endregion

    #region NextValidId
    private static object _validOrderIdLock = new object();
    private void OnNextValidId(int obj) {
      lock(_validOrderIdLock) {
        _validOrderId = obj + 1;
      }
      Verbose(new { _validOrderId });
    }
    /// <summary>
    /// https://docs.microsoft.com/en-us/dotnet/api/system.threading.interlocked.increment?f1url=https%3A%2F%2Fmsdn.microsoft.com%2Fquery%2Fdev15.query%3FappId%3DDev15IDEF1%26l%3DEN-US%26k%3Dk(System.Threading.Interlocked.Increment);k(SolutionItemsProject);k(TargetFrameworkMoniker-.NETFramework,Version%3Dv4.6.2);k(DevLang-csharp)%26rd%3Dtrue&view=netframework-4.7.1
    /// </summary>
    /// <returns></returns>
    public int ValidOrderId() {
      //ClientSocket.reqIds(1);
      lock(_validOrderIdLock) {
        try {
          return Interlocked.Increment(ref _validOrderId);
        } finally {
          //Trace(new { _validOrderId });
        }
      }
    }
    #endregion

    public IEnumerable<Price> TryGetPrice(Contract contract) {
      if(_marketDataManager.TryGetPrice(contract, out var price))
        yield return price;
    }
    public IEnumerable<Price> TryGetPrice(string pair) {
      if(TryGetPrice(pair, out var price))
        yield return price;
    }
    public bool TryGetPrice(string symbol, out Price price) { return _marketDataManager.TryGetPrice(symbol.ContractFactory(), out price); }
    public Price GetPrice(string symbol) { return _marketDataManager.GetPrice(symbol); }
    #region Price Changed
    private void OnPriceChanged(Price price) {
      RaisePriceChanged(price);
    }


    #region PriceChanged Event
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
    protected void RaisePriceChanged(Price price) {
      price.Time2 = ServerTime;
      PriceChangedEvent?.Invoke(this, new PriceChangedEventArgs(price, null, null));
    }
    #endregion

    #endregion

    private void OnCurrentTime(long obj) {
      var ct = obj.ToDateTimeFromEpoch(DateTimeKind.Utc).ToLocalTime();
      _serverTimeOffset = ct - DateTime.Now;
    }
    private void OnConnectionOpend() {
      ClientSocket.reqCurrentTime();
      SessionStatus = TradingServerSessionStatus.Connected;
    }

    private void OnConnectionClosed() {
      SessionStatus = TradingServerSessionStatus.Disconnected;
    }

    #region Events
    public void RaiseError(Exception exc) {
      ((EWrapper)this).error(exc);
    }

    static int[] _warningCodes = new[] { 2104, 2106, 2108 };
    static bool IsWarning(int code) => Configer.WarningCodes.Contains(code);
    static System.Collections.Concurrent.ConcurrentBag<int> _handledReqErros = new System.Collections.Concurrent.ConcurrentBag<int>();
    public static int SetRequestHandled(int id) {
      _handledReqErros.Add(id); return id;
    }
    private void OnError(int id, int errorCode, string message, Exception exc) {
      if(_handledReqErros.Contains(id)) return;
      if(IsWarning(errorCode)) return;
      if(exc is System.Net.Sockets.SocketException && !ClientSocket.IsConnected())
        RaiseLoginError(exc);
      if(exc != null)
        Trace(exc);
      else {
        Trace(new { IBCC = new { id, errorCode, message } });
        if(errorCode == 1102) {
          Trace("Cleaning price requests");
          MarketDataManager.ActiveRequestCleaner();
          Trace("Re-submitted price requests");
        }
      }
    }

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
      if(TradeClosedEvent != null)
        TradeClosedEvent(this, new TradeEventArgs(trade));
    }
    #endregion

    #endregion

    #region Connect/Disconnect
    public void Disconnect() {
      if(!IsInVirtualTrading && ClientSocket.IsConnected())
        ClientSocket.eDisconnect();
    }
    public void Connect(int port, string host, int clientId) {
      _port = port;
      _host = host;
      if(host.IsNullOrWhiteSpace())
        host = "127.0.0.1";
      try {
        ClientId = clientId;

        if(ClientSocket.IsConnected())
          throw new Exception(nameof(ClientSocket) + " is already connected");
        ClientSocket.eConnect(host, port, ClientId);
        if(!ClientSocket.IsConnected()) return;

        var reader = new EReader(ClientSocket, _signal);

        reader.Start();

        Task.Factory.StartNew(() => {
          while(ClientSocket.IsConnected() /*&& !reader.MessageQueueThread.IsCompleted*/) {
            _signal.waitForSignal();
            reader.processMsgs();
          }
        }, TaskCreationOptions.LongRunning)
        .ContinueWith(t => {
          Observable.Interval(TimeSpan.FromMinutes(10))
          .Select(_ => {
            try {
              RaiseError(new Exception(new { ClientSocket = new { IsConnected = ClientSocket.IsConnected() } } + ""));
              ReLogin();
              if(ClientSocket.IsConnected()) {
                RaiseLoggedIn();
                return true;
              };
            } catch {
            }
            return false;
          })
          .TakeWhile(b => b == false)
          .Subscribe();
        });
      } catch(Exception exc) {
        //HandleMessage(new ErrorMessage(-1, -1, "Please check your connection attributes.") + "");
        RaiseLoginError(exc);
      }
    }
    #endregion


    #region ICoreFx Implemented

    #region Events
    private EventHandler<LoggedInEventArgs> LoggedInEvent;
    public event EventHandler<LoggedInEventArgs> LoggedIn {
      add {
        if(LoggedInEvent == null || !LoggedInEvent.GetInvocationList().Contains(value))
          LoggedInEvent += value;
      }
      remove {
        LoggedInEvent -= value;
      }
    }
    private void RaiseLoggedIn() {
      LoggedInEvent?.Invoke(this, new LoggedInEventArgs(IsInVirtualTrading));
    }

    event LoginErrorHandler LoginErrorEvent;
    public event LoginErrorHandler LoginError {
      add {
        if(LoginErrorEvent == null || !LoginErrorEvent.GetInvocationList().Contains(value))
          LoginErrorEvent += value;
      }
      remove {
        LoginErrorEvent -= value;
      }
    }
    private void RaiseLoginError(Exception exc) {
      LoginErrorEvent?.Invoke(exc);
    }
    #endregion

    public override string ToString() {
      return new { Host = _host, Port = _port, ClientId } + "";
    }

    #region Log(In/Out)
    public bool LogOn(string host, string port, string clientId, bool isDemo) {
      try {
        var hosts = host.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        _managedAccount = hosts.Skip(1).LastOrDefault();
        if(!IsInVirtualTrading) {
          int iPort;
          if(!int.TryParse(port, out iPort))
            throw new ArgumentException("Value is not integer", nameof(port));
          int iClientId;
          if(!int.TryParse(clientId, out iClientId))
            throw new ArgumentException("Value is not integer", nameof(port));
          Connect(iPort, hosts.FirstOrDefault(), iClientId);
        }
        if(IsLoggedIn) {
          RaiseLoggedIn();
          return IsLoggedIn;
        } else
          throw new Exception("Not logged in.");
      } catch(Exception exc) {
        RaiseLoginError(exc);
        return false;
      }
    }

    public void Logout() {
      Disconnect();
    }

    public bool ReLogin() {
      _signal.issueSignal();
      Disconnect();
      Connect(_port, _host, ClientId);
      return true;
    }
    public bool IsLoggedIn => IsInVirtualTrading || ClientSocket.IsConnected();

    public TradingServerSessionStatus SessionStatus {
      get {
        return IsInVirtualTrading ? TradingServerSessionStatus.Connected : _sessionStatus;
      }

      set {
        if(_sessionStatus == value)
          return;
        _sessionStatus = value;
        NotifyPropertyChanged();
      }
    }

    public Func<Trade, double> CommissionByTrade { get; set; }

    #endregion

    #endregion
    private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
  }
}