/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HedgeHog;
using HedgeHog.Core;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.util;
using Order = IBApi.Order;

namespace IBApp {
  public partial class MarketDataManager :DataManager {
    private const string GENERIC_TICK_LIST = "233,221,236,165,106,104";
    static private readonly MemoryCache __currentPrices = MemoryCache.Default;
    static private MemoryCache _currentPrices => __currentPrices;
    public static IReadOnlyDictionary<string, Price> CurrentPrices => _currentPrices.ToDictionary(kv => kv.Key, kv => (Price)kv.Value);
    private static ConcurrentDictionary<int, (Contract contract, Price price)> activeRequests = new ConcurrentDictionary<int, (Contract contract, Price price)>();
    public static IReadOnlyDictionary<int, (Contract contract, Price price)> ActiveRequests => activeRequests;
    public IObservable<(Contract c, string gl, Action<int, Contract> cb, string Caller)> AddRequestObs { get; }
    static IScheduler esTickPrice = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = nameof(esTickPrice), Priority = ThreadPriority.Normal });
    class OnTickPriceAsyncBufferClass :ActionAsyncBuffer {
      public OnTickPriceAsyncBufferClass(int boundCapacity, [CallerMemberName] string Caller = null) : base(boundCapacity, Caller) {
      }
    }
    OnTickPriceAsyncBufferClass OnTickPriceAsyncBuffer = new OnTickPriceAsyncBufferClass(100);
    public MarketDataManager(IBClientCore client) : base(client) {
      IbClient.TickPriceObservable.SubscribeOn(esTickPrice).ObserveOn(esTickPrice)
        .Subscribe(t => OnTickPrice(t.RequestId, t.Field, t.Price, t.attribs));
      IbClient.TickStringObservable.SubscribeOn(esTickPrice).ObserveOn(esTickPrice)
        .Subscribe(t => OnTickString(t.tickerId, t.tickType, t.value));
      IbClient.TickGenericObservable.SubscribeOn(esTickPrice).ObserveOn(esTickPrice).Subscribe(OnTickGeneric);
      IbClient.OptionPriceObservable.Subscribe(OnOptionPrice);
      //IbClient.TickOptionCommunication += TickOptionCommunication; ;
    }

    private void OnTickGeneric((int tickerId, int field, double value) t) => OnTickPrice(t.tickerId, t.field, t.value, new TickAttrib());

    public void AddRequestSync(Contract contract, Action<int, Contract> callback = null, string genericTickList = null, [CallerMemberName] string Caller = "") {
      var cache = contract.FromCache().SingleOrDefault();
      if(cache == null) {
        if(Debugger.IsAttached)
          Debugger.Break();
        TraceError($"{nameof(AddRequestSync)}: ReqContractDetailsCached({contract})");
        contract.ReqContractDetailsCached().ObserveOn(TaskPoolScheduler.Default).Subscribe(cd => AddRequestSync(cd.Contract, callback, genericTickList, Caller));
        return;
      }
      if((true || cache.IsOptionsCombo || cache.IsHedgeCombo)) {
        AddRequestImpl(cache, callback, genericTickList, Common.CallerChain(Caller));
      } else {
        Task.Run(() =>
        IbClient.ReqContractDetailsCached(cache)
          .Take(1)
          .SubscribeOn(TaskPoolScheduler.Default)
          .ObserveOn(TaskPoolScheduler.Default)
          .Subscribe(cd => {
            AddRequestImpl(cd.Contract, callback, genericTickList, Common.CallerChain(Caller));
          }));
      }
    }
    static object _addRequestImplLock = new object();
    private void AddRequestImpl(Contract contract, Action<int, Contract> callback, string genericTickList = GENERIC_TICK_LIST, [CallerMemberName] string Caller = "") {
      genericTickList = genericTickList ?? GENERIC_TICK_LIST;
      var title = Common.CallerChain(Caller);
      lock(_addRequestImplLock) {
        var ar = activeRequests.Where(kv => kv.Value.contract == contract).ToArray();
        var ar0 = activeRequests.Where(kv => {
          if((kv.Value.contract == null || contract == null)) {
            if(Debugger.IsAttached)
              Debugger.Break();
            else
              TraceError(new { AddRequestImpl = new { kv = new { Value = new { kv.Value.contract } } }, contract });
          }
          return kv.Value.contract.Instrument == contract.Instrument;
        }).ToArray();
        if(ar0.Length != ar.Length)
          Debugger.Break();
        if(ar.Any()) {
          ar.ForEach(a => {
            if(a.Value.contract.ReqMktDataId == 0)
              Debugger.Break();
          });
          //TraceDebug($"{title}: {new { contract, key = contract._key() }} already requested");
          ar.ForEach(a => callback?.Invoke(a.Key, contract));
        } else {
          var reqId = contract.ReqMktDataId = IbClient.ValidOrderId();
          if(!activeRequests.TryAdd(reqId, (contract, new Price(contract.Instrument))))
            TraceError($"{title}: {nameof(activeRequests)}: {new { reqId, contract, contract.Instrument }} already exists");
          else
            TraceDebug0($"{title}: {nameof(activeRequests)} <- {new { reqId, contract, contract.Instrument }}");
          IbClient.OnReqMktData(() => {
            Verbose0($"AddRequest:{reqId}=>{contract}");
            IbClient.WatchReqError(1.FromSeconds()
              , reqId
              , t => {
                TraceError($"{title}:{contract} will be removed: {t}");
                activeRequests.TryRemove(t.id, out var c);
              }
              , () => {
                if(!activeRequests.ContainsKey(reqId))
                  TraceError($"{title} => request:{reqId} wes removed. See previous error");
                else {
                  AddCurrentPrice(activeRequests[reqId].price);
                  TraceIf(DoShowRequestErrorDone, $"AddRequest: {contract} => {reqId} Error done.");
                }
              });

            IbClient.OnReqMktData(() => IbClient.ClientSocket.reqMktData(reqId, contract.IsHedgeCombo ? contract : contract.ContractFactory(), genericTickList, false, false, new List<TagValue>()));
            contract.ReqMktDataId = reqId;
            if(reqId == 0)
              Debugger.Break();
            callback?.Invoke(reqId, contract);
          });
        }
      }
    }
    private void AddCurrentPrice(Price price) {
      if(!_currentPrices.Contains(price.Pair)) {
        {
          var cip = new CacheItemPolicy() {
            RemovedCallback = ce => {
              ActiveRequestCleaner((Price)ce.CacheItem.Value);
            },
            SlidingExpiration = 60.FromSeconds()
          };
          if(!_currentPrices.Add(price.Pair, price, cip))
            TraceError($"RaisePriceChanged: {price.Pair} is already in {nameof(_currentPrices)}");
          //else TraceDebug($"_currentPrices.Add({t.price.Pair}, {t.price}, cip)");
        }
      }

    }
    public IEnumerable<Price> GetPrice(Contract contract, [CallerMemberName] string Caller = "") {
      if(TryGetPrice(contract, out var price, null, Caller))
        yield return price;
    }
    public bool TryGetPrice(Contract contract, out Price price, [CallerMemberName] string Caller = "") => TryGetPrice(contract, out price, null, Caller);
    public bool TryGetPrice(Contract contract, out Price price, Action<int, Contract> callback, [CallerMemberName] string Caller = "") {
      price = null;
      var symbol = contract.Instrument;
      if(!_currentPrices.Contains(symbol)) {
        AddRequestSync(contract, callback, null, Common.CallerChain(Caller));
        //IbClient.SetContractSubscription(contract, $"{nameof(TryGetPrice)} <= {Caller}");
        return false;
      }
      price = (Price)_currentPrices[symbol];
      return true;
    }
    public Price GetPrice(string symbol) {
      if(!_currentPrices.Contains(symbol))
        throw new KeyNotFoundException(new { _currentPrices = new { symbol, not = " found" } } + "");
      return (Price)_currentPrices[symbol];
    }
    private void OnTickString(int tickerId, int tickType, string value) {
      //Trace($"{nameof(OnTickGeneric)}{(tickerId, tickType, value)}");
      if(!activeRequests.TryGetValue(tickerId, out var t)) return;
      var price = t.Item2;
      switch(tickType) {
        // RT Volume
        case 48:
          //RaisePriceChanged(t);
          break;
      }
    }
    #region OnTickPrice Subject
    object _OnTickPriceSubjectLocker = new object();
    ISubject<(string key, string message)> _OnTickPriceTraceSubject;
    public ISubject<(string key, string message)> OnTickPriceTraceSubject {
      get {
        lock(_OnTickPriceSubjectLocker)
          if(_OnTickPriceTraceSubject == null) {
            _OnTickPriceTraceSubject = new Subject<(string key, string message)>();

            _OnTickPriceTraceSubject
              .Distinct(t => t.key)
              .Subscribe(s => TraceDebug(s.message), exc => { });
          }
        return _OnTickPriceTraceSubject;
      }
    }
    void OnTickPriceTrace(string key, string message) {
      OnTickPriceTraceSubject.OnNext((key, message));
    }
    #endregion

    enum TickType {
      BidSize = 0,
      BidPrice = 1,
      AskPrice = 2,
      AskSize = 3,
      LastPrice = 4,
      LastSize = 5,
      High = 6,
      Low = 7,
      Volume = 8,
      ClosePrice = 9,
      BidOptionComputation = 10,
      AskOptionComputation = 11,
      LastOptionComputation = 12,
      ModelOptionComputation = 13,
      OpenTick = 14,
      Low13Weeks = 15,
      High13Weeks = 16,
      Low26Weeks = 17,
      High26Weeks = 18,
      Low52Weeks = 19,
      High52Weeks = 20,
      AverageVolume = 21,
      OpenInterest = 22,
      OptionHistoricalVolatility = 23,
      OptionImpliedVolatility = 24,
      OptionBidExchange = 25,
      OptionAskExchange = 26,
      OptionCallOpenInterest = 27,
      OptionPutOpenInterest = 28,
      OptionCallVolume = 29,
      OptionPutVolume = 30,
      IndexFuturePremium = 31,
      BidExchange = 32,
      AskExchange = 33,
      AuctionVolume = 34,
      AuctionPrice = 35,
      AuctionImbalance = 36,
      MarkPrice = 37,
      BidEFPComputation = 38,
      AskEFPComputation = 39,
      LastEFPComputation = 40,
      OpenEFPComputation = 41,
      HighEFPComputation = 42,
      LowEFPComputation = 43,
      CloseEFPComputation = 44,
      LastTimestamp = 45,
      Shortable = 46,
      RTVolumeTimeSales = 48,
Halted = 49,
BidYield = 50,
AskYield = 51,
LastYield = 52,
CustomOptionComputation = 53,
TradeCount = 54,
TradeRate = 55,
VolumeRate = 56,
LastRTHTrade = 57,
RTHistoricalVolatility = 58,
IBDividends = 59,
BondFactorMultiplier = 60,
RegulatoryImbalance = 61,
News = 62,
ShortTermVolume3Minutes = 63,
ShortTermVolume5Minutes = 64,
ShortTermVolume10Minutes = 65,
DelayedBid = 66,
DelayedAsk = 67,
DelayedLast = 68,
DelayedBidSize = 69,
DelayedAskSize = 70,
DelayedLastSize = 71,
DelayedHighPrice = 72,
DelayedLowPrice = 73,
DelayedVolume = 74,
DelayedClose = 75,
DelayedOpen = 76,
RTTradeVolume = 77,
Creditmanmarkprice = 78,
Creditmanslowmarkprice = 79,
DelayedBidOption = 80,
DelayedAskOption = 81,
DelayedLastOption = 82,
DelayedModelOption = 83,
LastExchange = 84,
LastRegulatoryTime = 85,
FuturesOpenInterest = 86,
AverageOptionVolume = 87,
DelayedLastTimestamp = 88,
ShortableShares = 89,
ETFNavClose = 92,
ETFNavPriorClose = 93,
ETFNavBid = 94,
ETFNavAsk = 95,
ETFNavLast = 96,
ETFNavFrozenLast = 97,
ETFNavHigh = 98,
ETFNavLow = 99,
};

    private void OnTickPrice(int requestId, int field, double price, TickAttrib attrib) {
      if(!activeRequests.ContainsKey(requestId))
        return;
      var ar = activeRequests[requestId];
      var trace = false && ar.contract.UnderContract.Any(c => c.LocalSymbol == "SESN");
      if(false) TraceDebug(new[] { new { Price = ar.price, field, price } }.ToTextOrTable($"{nameof(RaisePriceChanged)}:{ ShowThread()}"));
      if(false) OnTickPriceTrace(new { requestId, field, price } + "", $"{nameof(OnTickPrice)}[{requestId}]: {ar.contract}:{new { field, price }}");
      var price2 = ar.price;
      //Trace($"{nameof(OnTickPrice)}:{price2.Pair}:{(requestId, field, price).ToString()}");
      const int CLOSE_PRICE = 9;
      const int LOW_52 = 19;
      const int HIGH_52 = 20;
      var doLastPrice = new[] { "NQ", "ES", "MNQ", "MES", "TN", "VIX","RTY", "YM" }.Contains(ar.contract.Symbol);
      var st = IbClient.ServerTime;
      var pt = price2.TimeSet;
      var doMarkPrice = doLastPrice && (st - pt).TotalSeconds > 30;
      if(price == -1) return;
      switch(field) {
        case 1: { // Bid
            if(doMarkPrice || doLastPrice) Debug.WriteLine(new { doLastPrice, doMarkPrice, field, price });
            if(!price2.IsBidSet || price > 0) {
              price2.Bid = price;
              price2.Time2 = price2.TimeSet = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
            break;
          }
        case 2: { //ASK
            if(doMarkPrice || doLastPrice) Debug.WriteLine(new { doLastPrice, doMarkPrice, field, price });
            if(!price2.IsAskSet || price > 0) {
              price2.Ask = price;
              price2.Time2 = price2.TimeSet = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
            break;
          }
        case CLOSE_PRICE: {
            if(!ar.contract.IsTradingHours(IbClient.ServerTime)) {
              if(!price2.IsAskSet) {
                price2.Ask = price;
                price2.IsAskSet = true;
              }
              if(!price2.IsBidSet) {
                price2.Bid = price;
                price2.IsBidSet = true;
              }
              price2.Time2 = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
          }
          break;
        case 37: {
            if(doMarkPrice) {
              var p = Order.OrderPrice(price, ar.contract);
              price2.Ask = price2.Bid = p;
              price2.Time2 = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
          }
          break;
        case (int)TickType.LastPrice: {
            if(ar.contract.IsIndex || doMarkPrice || !ar.contract.IsTradingHours(IbClient.ServerTime)) {
              if(ar.contract.IsIndex || !price2.IsAskSet && price2.Ask <= 0)
                price2.Ask = price;
              if(ar.contract.IsIndex || !price2.IsBidSet && price2.Bid <= 0)
                price2.Bid = price;
              price2.Time2 = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
          }
          break;
        case 46:
          price2.IsShortable = price > 2.5;
          Trace(new { price2.Pair, price2.IsShortable, price });
          break;
        case LOW_52:
          price2.Low52 = price;
          break;
        case HIGH_52:
          price2.High52 = price;
          break;
        case (int)TickType.OptionImpliedVolatility:
          price2.OptionImpliedVolatility = price;
          TraceDebug($"{nameof(OnTickPrice)}[{requestId}]: {ar.contract}:{new { field, price }}");
          break;
      }
    }

    private void OnOptionPrice(TickOptionMessage t) {
      if(!activeRequests.ContainsKey(t.RequestId)) return;
      var priceMessage = new TickPriceMessage(t.RequestId, t.Field, t.OptPrice, null);
      if(!activeRequests.TryGetValue(t.RequestId, out var ar)) {
        TraceError($"{nameof(OnOptionPrice)}: {new { t.RequestId }} not found in {nameof(activeRequests)}");
        return;
      };
      var price2 = ar.price;
      //Trace($"{nameof(OnTickPrice)}:{price2.Pair}:{(requestId, field, price).ToString()}");
      if(priceMessage.Price == 0)
        return;
      const int LOW_52 = 19;
      const int HIGH_52 = 20;
      price2.GreekDelta = (t.Delta.Abs().Between(0, 1) ? t.Delta : 1).Round(1);
      if(t.Theta.IsSetAndNotZero())
        price2.GreekTheta = price2.GreekTheta.Min(0).Cma(10, t.Theta);
      var price = t.OptPrice;
      switch(priceMessage.Field) {
        case 1: { // Bid
            if(price2.Bid == priceMessage.Price)
              break;
            if(price2.Ask <= 0)
              price2.Ask = priceMessage.Price;
            if(priceMessage.Price > 0) {
              price2.Bid = priceMessage.Price;
              price2.Time2 = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
            break;
          }
        case 2: { //ASK
            if(price2.Ask == priceMessage.Price)
              break;
            if(price2.Bid <= 0)
              price2.Bid = priceMessage.Price;
            if(priceMessage.Price > 0) {
              price2.Ask = priceMessage.Price;
              price2.Time2 = IbClient.ServerTime;
              RaisePriceChanged(ar);
            }
            break;
          }
        case 4: {
            if(priceMessage.Price > 0)
              if(price2.Ask <= 0)
                price2.Ask = priceMessage.Price;
            if(price2.Bid <= 0)
              price2.Bid = priceMessage.Price;
            price2.Time2 = IbClient.ServerTime;
            RaisePriceChanged(ar);
            break;
          }
        case 0:
        case 3:
        case 5:
          RaisePriceChanged(ar);
          break;
        case 37:
          if(price2.Bid <= 0 && price2.Ask <= 0) {
            price2.Bid = price2.Ask = price;
            RaisePriceChanged(ar);
          }
          break;
        case 46:
          price2.IsShortable = price > 2.5;
          Trace(new { price2.Pair, price2.IsShortable, price });
          break;
        case LOW_52:
          price2.Low52 = price;
          break;
        case HIGH_52:
          price2.High52 = price;
          break;
      }
    }

    private void TickOptionCommunication(int tickerId, int field, double impliedVolatility
      , double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice) {
      Trace($"TickOption:{new { tickerId, field, impliedVolatility, delta, optPrice, pvDividend, gamma, vega, theta, undPrice }}");
    }


    #region PriceChanged Event
    public event PriceChangedEventHandler PriceChangedEvent;
    public event PriceChangedEventHandler PriceChanged {
      add {
        if(PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
          PriceChangedEvent += value;
      }
      remove {
        PriceChangedEvent -= value;
      }
    }
    static object _raisePriceChangedLocket = new object();
    protected void RaisePriceChanged((Contract contract, Price price) t, [CallerMemberName] string caller = "") {
      if(t.contract.IsHedgeCombo) {
        TraceDebug0($"{nameof(OnTickPrice)}: {t.contract}:{new { t.price.Ask, t.price.Bid }}");
      }
      PriceChangedEvent?.Invoke(t.price);
      /// Locals
    }
    public void ActiveRequestCleaner(Price price = null, Contract contract = null) {
      activeRequests.Where(kv => price != null ? kv.Value.price == price : kv.Value.contract == contract).ToList().ForEach(CancelPriceRequest);
      void CancelPriceRequest(KeyValuePair<int, (Contract contract, Price price)> ar) {
        _currentPrices.Where(cp => cp.Key == ar.Value.price.Pair).ToList().ForEach(cp => _currentPrices.Remove(cp.Key));
        if(activeRequests.TryRemove(ar.Key, out var rem)) {
          IBClientMaster.CancelPrice(ar.Key);
          TraceDebug0($"{nameof(activeRequests)} - removed {ar.Value.contract}");
        }
      }
    }
    public static Contract[] ActiveRequestCleaner() {
      var contracts = activeRequests.Select(kv => kv.Value.contract).ToArray();
      _currentPrices.ToList().ForEach(cp => _currentPrices.Remove(cp.Key));
      while(activeRequests.Any())
        activeRequests.TryRemove(activeRequests.First().Key, out var ar);
      contracts.ForEach(c => IBClientMaster.MarketDataManager.AddRequestSync(c));
      return contracts;
    }
    #endregion


    /* UpdateUI
    public override void UpdateUI(IBMessage message) {
      MarketDataMessage dataMessage = (MarketDataMessage)message;
      //checkToAddRow(dataMessage.RequestId);
      DataGridView grid = (DataGridView)uiControl;
      if(grid.Rows.Count >= dataMessage.RequestId - TICK_ID_BASE) {
        if(message is TickPriceMessage) {
          TickPriceMessage priceMessage = (TickPriceMessage)message;
          switch(dataMessage.Field) {
            case 1: {
                //BID
                grid[BID_PRICE_INDEX, GetIndex(dataMessage.RequestId)].Value = priceMessage.Price;
                break;
              }
            case 2: {
                //ASK
                grid[ASK_PRICE_INDEX, GetIndex(dataMessage.RequestId)].Value = priceMessage.Price;
                break;
              }
            case 9: {
                //CLOSE
                grid[CLOSE_PRICE_INDEX, GetIndex(dataMessage.RequestId)].Value = priceMessage.Price;
                break;
              }
          }
        } else if(dataMessage is TickSizeMessage) {
          TickSizeMessage sizeMessage = (TickSizeMessage)message;
          switch(dataMessage.Field) {
            case 0: {
                //BID SIZE
                grid[BID_SIZE_INDEX, GetIndex(dataMessage.RequestId)].Value = sizeMessage.Size;
                break;
              }
            case 3: {
                //ASK SIZE
                grid[ASK_SIZE_INDEX, GetIndex(dataMessage.RequestId)].Value = sizeMessage.Size;
                break;
              }
            case 5: {
                //LAST SIZE
                grid[LAST_SIZE_INDEX, GetIndex(dataMessage.RequestId)].Value = sizeMessage.Size;
                break;
              }
            case 8: {
                //VOLUME
                grid[VOLUME_SIZE_INDEX, GetIndex(dataMessage.RequestId)].Value = sizeMessage.Size;
                break;
              }
          }
        }
      }
    }
    */
  }

}
