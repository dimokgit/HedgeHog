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
using ContDetHandler = System.Action<int, IBApi.ContractDetails>;
using OptionsChainHandler = System.Action<int, string, int, string, string, System.Collections.Generic.HashSet<string>, System.Collections.Generic.HashSet<double>>;
using TickPriceHandler = System.Action<int, int, double, int>;
using OptionPriceHandler = System.Action<int, int, double, double, double, double, double, double, double, double>;
using ReqSecDefOptParams = System.IObservable<(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, System.Collections.Generic.HashSet<string> expirations, System.Collections.Generic.HashSet<double> strikes)>;
using ReqSecDefOptParamsList = System.Collections.Generic.IList<(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, System.Collections.Generic.HashSet<string> expirations, System.Collections.Generic.HashSet<double> strikes)>;
using ErrorHandler = System.Action<int, int, string, System.Exception>;
using OPTION_CHAIN = System.IObservable<(string exchange, string tradingClass, string multiplier, System.DateTime expiration, double[] strikes, double price, string symbol, string currency)>;
using OPTION_PRICE_OBSERVABLE = System.IObservable<(int tickerId, int field, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)>;
using System.Reactive.Subjects;
using System.Diagnostics;
using static IBApp.IBApiMixins;

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
    #endregion

    #region Properties
    public Action<object> Trace => _trace;
    public Action<object> TraceTemp => o => { };
    private bool _verbose = true;
    public void Verbouse(object o) { if(_verbose) _trace(o); }

    #endregion

    #region ICoreEX Implementation
    public void SetOfferSubscription(Contract contract, Action<Contract> callback) => _marketDataManager.AddRequest(contract, "233,221,236", callback);
    public void SetOfferSubscription(string pair) => SetOfferSubscription(ContractSamples.ContractFactory(pair), _ => { });
    public void SetOfferSubscription(string pair, Action<Contract> callback) => SetOfferSubscription(ContractSamples.ContractFactory(pair), callback);
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
    IObservable<(int reqId, ContractDetails contractDetails)> ContractDetailsFactory()
      => Observable.FromEvent<ContDetHandler, (int reqId, ContractDetails contractDetails)>(
        onNext => (int a, ContractDetails b) => onNext((a, b)),
        h => ContractDetails += h.SideEffect(_ => TraceTemp($"Subscribed to {nameof(ContractDetails)}")),
        h => ContractDetails -= h.SideEffect(_ => TraceTemp($"UnSubscribed to {nameof(ContractDetails)}"))
        );
    IObservable<(int reqId, ContractDetails contractDetails)> _ContractDetailsObservable;
    IObservable<(int reqId, ContractDetails contractDetails)> ContractDetailsObservable =>
      (_ContractDetailsObservable ?? (_ContractDetailsObservable = ContractDetailsFactory()));

    IObservable<int> ContractDetailsEndFactory() => Observable.FromEvent<Action<int>, int>(
        onNext => (int a) => onNext(a),
        h => ContractDetailsEnd += h.SideEffect(_ => TraceTemp($"Subscribed to {nameof(ContractDetailsEnd)}")),
        h => ContractDetailsEnd -= h.SideEffect(_ => TraceTemp($"UnSubscribed to {nameof(ContractDetailsEnd)}"))
        );
    IObservable<int> _ContractDetailsEndObservable;
    IObservable<int> ContractDetailsEndObservable =>
      (_ContractDetailsEndObservable ?? (_ContractDetailsEndObservable = ContractDetailsEndFactory()));
    #endregion

    #region SecurityDefinitionOptionParameterObservable
    ReqSecDefOptParams SecurityDefinitionOptionParameterFactory() => Observable.FromEvent<OptionsChainHandler, (int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)>(
        onNext => (int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
        => onNext((reqId, exchange, underlyingConId, tradingClass, multiplier, expirations, strikes)),
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
    static IScheduler esReqPrice = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice", Priority = ThreadPriority.Lowest });
    static IScheduler esReqPrice2 = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqPrice2", Priority = ThreadPriority.Lowest });
    IObservable<(int reqId, int field, double price, int canAutoExecute)> TickPriceFactoryFromEvent()
      => Observable.FromEvent<TickPriceHandler, (int reqId, int field, double price, int canAutoExecute)>(
        onNext => (int reqId, int field, double price, int canAutoExecute)
        => onNext((reqId, field, price, canAutoExecute)),
        h => TickPrice += h/*.SideEffect(_ => Trace($"Subscribed to {nameof(TickPrice)}"))*/,
        h => TickPrice -= h/*.SideEffect(_ => Trace($"UnSubscribed to {nameof(TickPrice)}"))*/
        )
      .SubscribeOn(esReqPrice)
      .Publish()
      .RefCount()
      .ObserveOn(esReqPrice2)
      ;
    IObservable<(int reqId, int field, double price, int canAutoExecute)> _TickPriceObservable;
    internal IObservable<(int reqId, int field, double price, int canAutoExecute)> TickPriceObservable =>
      (_TickPriceObservable ?? (_TickPriceObservable = TickPriceFactoryFromEvent()));
    #endregion

    #region OptionPrice
    OPTION_PRICE_OBSERVABLE OptionPriceFactoryFromEvent()
      => Observable.FromEvent<OptionPriceHandler, (int tickerId, int field, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)>(
        onNext => (int tickerId, int field, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
        => onNext((tickerId, field, impliedVolatility, delta, optPrice, pvDividend, gamma, vega, theta, undPrice)),
        h => TickOptionCommunication += h/*.SideEffect(_ => Trace($"Subscribed to {nameof(TickPrice)}"))*/,
        h => TickOptionCommunication -= h/*.SideEffect(_ => Trace($"UnSubscribed to {nameof(TickPrice)}"))*/
        )
      .ObserveOn(esReqPrice)
      .Publish()
      .RefCount();
    OPTION_PRICE_OBSERVABLE _OptionPriceObservable;
    OPTION_PRICE_OBSERVABLE OptionPriceObservable => (_OptionPriceObservable ?? (_OptionPriceObservable = OptionPriceFactoryFromEvent()));
    #endregion

    #region Error
    static IScheduler esError = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Error", Priority = ThreadPriority.Lowest });
    IObservable<(int id, int errorCode, string errorMsg, Exception exc)> ErrorFactory() => Observable.FromEvent<ErrorHandler, (int id, int errorCode, string errorMsg, Exception exc)>(
      onNext => (int id, int errorCode, string errorMsg, Exception exc) => onNext((id, errorCode, errorMsg, exc)),
      h => Error += h/*.SideEffect(_ => Trace($"Subscribed to {nameof(Error)}"))*/,
      h => Error -= h/*.SideEffect(_ => Trace($"UnSubscribed to {nameof(Error)}"))*/
      ).ObserveOn(esError)
      .Publish()
      .RefCount();
    IObservable<(int id, int errorCode, string errorMsg, Exception exc)> _ErrorObservable;
    public IObservable<(int id, int errorCode, string errorMsg, Exception exc)> ErrorObservable =>
      (_ErrorObservable ?? (_ErrorObservable = ErrorFactory()));
    #endregion
    #endregion

    #region Req-* functions
    int NextReqId() => ValidOrderId();

    public IObservable<ContractDetails> ReqContractDetailsCached(Contract contract) {
      return contract.FromDetailsCache().ToObservable().ToArray()
        .Where(c => c.Any())
        .Concat(Observable.Defer(() => ReqContractDetailsAsync(contract).ToArray()))
        .Take(1)
        .SelectMany(b => b);
    }

    IScheduler esReqCont = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqContract" });
    public IObservable<ContractDetails> ReqContractDetailsAsync(Contract contract) {
      var reqId = NextReqId();
      //event Action<(int, ContractDetails)> stopError;// = (a) => (0, (ContractDetails)null);
      var cd = WireToError<(int reqId, ContractDetails cd)>(
        reqId,
        ContractDetailsObservable,
        ContractDetailsEndObservable,
        (int rid) => (rid, (ContractDetails)null),
        (0, (ContractDetails)null),
        t => t.reqId,
        t => t.cd != null,
        error => Trace($"{nameof(ReqContractDetailsAsync)}: {new { c = contract, error }}"),
        () => { }//Trace($"{nameof(ReqContractDetailsAsync)} {reqId} Error done")
        )
        .Do(t => t.cd.AddToCache())
        .Select(t => t.cd)
        .ObserveOn(esReqCont)
      //.Do(t => Trace(new { ReqContractDetailsImpl = t.reqId, contract = t.contractDetails?.Summary, Thread.CurrentThread.ManagedThreadId }))
      ;
      ClientSocket.reqContractDetails(reqId, contract);
      //Trace(new { ReqContractDetailsImpl = reqId, contract });
      return cd;
    }
    IObservable<T> WireToError<T>
      (int reqId, IObservable<T> source, IObservable<int> endSubject, Func<int, T> endFactory, T empty, Func<T, int> getReqId, Func<T, bool> isNotEnd, Action<(int id, int errorCode, string errorMsg, Exception exc)> onError, Action onEnd) {
      var end = new Subject<int>();
      var error = new Subject<(int id, int errorCode, string errorMsg, Exception exc)>();
      var d = error.Merge(ErrorFactory())
        .Where(t => t.id == reqId)
        .TakeWhile(t => t.Item2 != -2)
        .Subscribe(e => {
          end.OnNext(reqId);
          end.Dispose();
          onError(e);
          //;
        }, onEnd);
      Func<int, T> next = _ => {
        //Trace($"Nneed to unsub:{reqId}");
        error.OnNext((reqId, -2, "", (Exception)null));
        error.Dispose();
        end.Dispose();
        d.Dispose();
        return empty;
      };
      var o = source
        .Merge(endSubject.Merge(end).Select(endFactory))
        .Where(t => getReqId(t) == reqId)
        .TakeWhile(isNotEnd)//t => t.Item2 != null)
        .OnErrorResumeNext(Observable.Return(1).Select(next))
        .Where(t => getReqId(t) > 0)
        ;
      return o;
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

    Func<string, string, string, int, ReqSecDefOptParamsList> _ReqSecDefOptParamsList;
    public ReqSecDefOptParamsList ReqSecDefOptParams(string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId) => (_ReqSecDefOptParamsList
      ?? (_ReqSecDefOptParamsList = new Func<string, string, string, int, ReqSecDefOptParamsList>((us, ex, st, comId)
          => ReqSecDefOptParamsImpl(us, ex, st, comId)))
      .Memoize()
      )(underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId);

    public ReqSecDefOptParamsList ReqSecDefOptParamsImpl(string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId) {
      return ReqSecDefOptParamsAsync(underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId).ToEnumerable().ToArray();
    }

    IScheduler esSecDef = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "ReqSecDefOpt" });
    public ReqSecDefOptParams ReqSecDefOptParamsAsync(string underlyingSymbol, string futFopExchange, string underlyingSecType, int underlyingConId) {
      var reqId = NextReqId();
      var cd = SecurityDefinitionOptionParameterObservable
        .Merge(SecurityDefinitionOptionParameterEndObservable.Select(rid => (reqId: rid, exchange: "", underlyingConId: 0, tradingClass: "", multiplier: "", expirations: (HashSet<string>)null, strikes: (HashSet<double>)null)))
        .Where(t => t.reqId == reqId)
        .TakeWhile(t => t.expirations != null)
        .Do(t => TraceTemp(new { ReqSecDefOptParamsImpl = t.reqId, t.tradingClass }))
        .ObserveOn(esSecDef)
        //.Do(x => _verbous(new { ReqSecDefOptParams = new { Started = x.reqId } }))
        //.Timeout(TimeSpan.FromSeconds(_reqTimeout))
        ;
      //.Subscribe(t => callback(t.contractDetails),exc=> { throw exc; }, () => _verbous(new { ContractDetails = new { Completed = reqId } }));
      ClientSocket.reqSecDefOptParams(reqId, underlyingSymbol, futFopExchange, underlyingSecType, underlyingConId);
      return cd;
    }

    public IObservable<Contract> ReqCurrentOptionsAsync(string symbol, bool[] isCalls, int optionsCount = 3) {
      return (
        from t in ReqOptionChainsAsync(symbol)
        from strike in t.strikes.OrderBy(st => st.Abs(t.price)).Take(optionsCount)
        from isCall in isCalls
        let option = MakeOptionSymbol(t.tradingClass, t.expiration, strike, isCall)
        from o in ReqContractDetailsCached(option.ContractFactory())
        select o.Summary.AddToCache()//.SideEffect(c=>Trace(new { optionContract = c }))
       );
    }

    public OPTION_CHAIN ReqOptionChainsAsync(string symbol) {
      var optionChain = (
        from cd in ReqContractDetailsCached(symbol.ContractFactory())
        from price in TryGetPrice(symbol)
        .Where(p => p.Bid > 0 && p.Ask > 0)
        .Select(p => p.Average)
        .ToObservable()
        .Concat(Observable.Defer(() => ReqPriceMarket(cd.Summary)))
        .Take(1)
        from och in ReqSecDefOptParams(cd.Summary.LocalSymbol, "", cd.Summary.SecType, cd.Summary.ConId)
        where och.exchange == "SMART"
        from expiration in och.expirations.Select(e => e.FromTWSDateString())
        select (och.exchange, och.tradingClass, och.multiplier, expiration, strikes: och.strikes.ToArray(), price, symbol = cd.Summary.Symbol, currency: cd.Summary.Currency)
        )
        .Aggregate((p, n) => p.expiration < n.expiration ? p : n);
      //return optionChain
      //  .SelectMany(l => l.OrderBy(t => t.strikes.Length).TakeLast(1))
      //  .Take(1);
      return optionChain;
    }

    public enum TickType { Bid = 1, Ask = 2, MarketPrice = 37 };
    public IObservable<double> ReqMarketPrice_Bad(string symbol) =>
      TryGetPrice(symbol)
      .Where(p => p.Ask > 0 && p.Bid > 0)
      .Select(p => p.Average)
      .ToObservable()
      .ToArray()
      .Where(a => a.Any())
      .Concat(Observable.Defer(() => ReqMarketPrice(symbol)).ToArray())
      .Take(1)
      .SelectMany(b => b);
    public IObservable<double> ReqMarketPrice(string symbol)
      => from cd in ReqContractDetailsCached(symbol.ContractFactory())
         from p in ReqPriceMarket(cd.Summary)
         select p;
    public IObservable<double> ReqMarketPrice(Contract contract) => ReqPrice(contract, TickType.MarketPrice).SelectMany(p => p);
    public IObservable<(double bid, double ask)> ReqBidAsk(Contract contract)
      => ReqPrice(contract, TickType.Bid, TickType.Ask).Select(d => (d.Min(), d.Max()));


    #region ReqMktData Subject
    object _ReqMktDataSubjectLocker = new object();
    ISubject<Action> _ReqMktDataSubject;
    ISubject<Action> ReqMktDataSubject {
      get {
        lock(_ReqMktDataSubjectLocker)
          if(_ReqMktDataSubject == null) {
            _ReqMktDataSubject = new Subject<Action>();
            _ReqMktDataSubject
              .ObserveOn(NewThreadScheduler.Default)
              .Do(_ => Thread.Sleep(100))
              .Subscribe(s => s(), exc => { });
          }
        return _ReqMktDataSubject;
      }
    }
    void OnReqMktData(Action p) {
      ReqMktDataSubject.OnNext(p);
    }
    #endregion

    public IObservable<double[]> ReqPrice(Contract contract, params TickType[] tickType) {
      var tt = tickType.Cast<int>().ToArray();
      var reqId = NextReqId();
      var cd = TickPriceObservable
        //.Do(x => Trace(new { ReqPrice = new { contract, started = x } }))
        .Where(t => t.reqId == reqId)
        .Where(t => tt.Contains(t.field))
        .Distinct(t => t.field)
        //.Timeout(TimeSpan.FromSeconds(15))
        .Take(tickType.Length)
        .Select(t => t.price)
        .ToArray()
        //.Catch<double[], TimeoutException>(exc => {
        //  Trace(new { ReqPrice = exc, contract });
        //  return new double[0][].ToObservable();
        //})
        ;
      ErrorObservable
      .Where(t => t.id == reqId)
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(2)
      .Merge()
      .Subscribe(t => Trace($"{contract}: {t}"), () => Trace($"{nameof(ReqPrice)}: {contract} => {reqId} Error done."));
      ClientSocket.reqMktData(reqId, contract.ContractFactory(), "232", false, null);
      Trace($"{nameof(ReqPrice)}:{reqId} {contract}");
      return cd;
    }
    int[] _bidAsk = new int[] { 1, 2 };
    public IObservable<(Contract contract, double bid, double ask, DateTime time)> ReqPrice(Contract contract) {
      var reqId = NextReqId();
      var cd = TickPriceObservable
        .Where(t => t.reqId == reqId && _bidAsk.Contains(t.field))
        .Scan((contract, bid: 0.0, ask: 0.0, time: DateTime.MinValue), (p, n) => (contract, n.field == 1 ? n.price : p.bid, n.field == 2 ? n.price : p.ask, ServerTime))
        //.Do( x => Trace(new { ReqPrice = new { contract, started = x } }))
        ;
      WatchReqError(contract, reqId, () => Trace($"{nameof(ReqPrice)}: {contract} => {reqId} Error done."));
      ClientSocket.reqMktData(reqId, contract.ContractFactory(), "232", false, null);
      return cd;
    }
    public IObservable<(Contract contract, double bid, double ask)> ReqOptionPrice(Contract contract) {
      var reqId = NextReqId();
      var cd = OptionPriceObservable
        .Where(t => t.tickerId == reqId)
        .Do(t => Trace($"{nameof(ReqOptionPrice)}:t"))
        .Scan((contract, bid: 0.0, ask: 0.0), (p, n) => (contract, n.field == 1 ? n.optPrice : p.bid, n.field == 2 ? n.optPrice : p.ask))
        //.Do( x => Trace(new { ReqPrice = new { contract, started = x } }))
        ;
      ClientSocket.reqMktData(reqId, contract.ContractFactory(), "232", false, null);
      return cd;
    }

    public IObservable<double> ReqPriceMarket(Contract contract) {
      var reqId = NextReqId();
      var cd = TickPriceObservable
        //.Do(x => Trace($"{nameof(ReqPriceMarket)}:{new { contract, started = x }}"))
        .Where(t => t.reqId == reqId && t.field == 37)
        .Take(1)
        .Select(t => t.price)
        //.Do( x => Trace(new { ReqPrice = new { contract, started = x } }))
        ;
      WatchReqError(contract, reqId, () => { });// Trace($"{nameof(ReqPriceMarket)}: {contract} => {reqId} Error done."));
      ClientSocket.reqMktData(reqId, contract.ContractFactory(), "221,232", false, null);
      return cd;
    }

    private void WatchReqError(Contract contract, int reqId, Action error) =>
      ErrorObservable
      .Where(t => t.id == reqId)
      .Window(TimeSpan.FromSeconds(5), TaskPoolScheduler.Default)
      .Take(2)
      .Merge()
      .Subscribe(t => Trace($"{contract}: {t}"), error);
    #endregion

    #region NextValidId
    private static object _validOrderIdLock = new object();
    private void OnNextValidId(int obj) {
      lock(_validOrderIdLock) {
        _validOrderId = obj + 1;
      }
      Verbouse(new { _validOrderId });
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

    public IEnumerable<Price> TryGetPrice(string pair) {
      if(TryGetPrice(pair, out var price))
        yield return price;
    }
    public bool TryGetPrice(string symbol, out Price price) { return _marketDataManager.TryGetPrice(symbol, out price); }
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

    #region OrderAddedEvent
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

    void RaiseOrderAdded(object sender, OrderEventArgs args) => OrderAddedEvent?.Invoke(sender, args);
    #endregion
    #region OrderRemovedEvent
    event OrderRemovedEventHandler OrderRemovedEvent;
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
    System.Collections.Concurrent.ConcurrentBag<int> _handledReqErros = new System.Collections.Concurrent.ConcurrentBag<int>();
    public int SetRequestHandled(int id) {
      _handledReqErros.Add(id); return id;
    }
    private void OnError(int id, int errorCode, string message, Exception exc) {
      if(_handledReqErros.Contains(id)) return;
      if(IsWarning(errorCode)) return;
      if(exc is System.Net.Sockets.SocketException && !ClientSocket.IsConnected())
        RaiseLoginError(exc);
      if(exc != null)
        Trace(exc);
      else
        Trace(new { IBCC = new { id, error = errorCode, message } });
    }

    #region TradeAdded Event
    //public class TradeEventArgs : EventArgs {
    //  public Trade Trade { get; private set; }
    //  public TradeEventArgs(Trade trade) : base() {
    //    Trade = trade;
    //  }
    //}
    event EventHandler<TradeEventArgs> TradeAddedEvent;
    public event EventHandler<TradeEventArgs> TradeAdded {
      add {
        if(TradeAddedEvent == null || !TradeAddedEvent.GetInvocationList().Contains(value))
          TradeAddedEvent += value;
      }
      remove {
        TradeAddedEvent -= value;
      }
    }
    protected void RaiseTradeAdded(Trade trade) {
      if(TradeAddedEvent != null)
        TradeAddedEvent(this, new TradeEventArgs(trade));
    }
    #endregion

    #region TradeRemoved Event
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
    protected void RaiseTradeRemoved(Trade trade) {
      if(TradeRemovedEvent != null)
        TradeRemovedEvent(this, new TradeEventArgs(trade));
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
          while(ClientSocket.IsConnected() && !reader.MessageQueueThread.IsCompleted) {
            _signal.waitForSignal();
            reader.processMsgs();
          }
        }, TaskCreationOptions.LongRunning)
        .ContinueWith(t => {
          Observable.Interval(TimeSpan.FromSeconds(5))
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