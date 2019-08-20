/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HedgeHog;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.util;

namespace IBApp {
  public class MarketDataManager :DataManager {
    const int TICK_ID_BASE = 10000000;
    static private readonly MemoryCache _currentPrices = MemoryCache.Default;
    public static IReadOnlyDictionary<string, Price> CurrentPrices => _currentPrices.ToDictionary(kv => kv.Key, kv => (Price)kv.Value);
    private static ConcurrentDictionary<int, (Contract contract, Price price)> activeRequests = new ConcurrentDictionary<int, (Contract contract, Price price)>();
    public static IReadOnlyDictionary<int, (Contract contract, Price price)> ActiveRequests => activeRequests;
    public Action<Contract, string, Action<Contract>> AddRequest;// = (a1, a2, a3) => {    };
    public IObservable<(Contract c, string gl, Action<Contract> cb)> AddRequestObs { get; }
    public MarketDataManager(IBClientCore client) : base(client, TICK_ID_BASE) {
      IbClient.TickPriceObservable.Subscribe(t => OnTickPrice(t.RequestId, t.Field, t.Price, t.attribs));
      IbClient.OptionPriceObservable.Subscribe(t => OnOptionPrice(t));
      IbClient.TickString += OnTickString; ;
      IbClient.TickGeneric += OnTickGeneric;
      //IbClient.TickOptionCommunication += TickOptionCommunication; ;
      AddRequestObs = Observable.FromEvent<Action<Contract, string, Action<Contract>>, (Contract c, string gl, Action<Contract> cb)>(
        next => (c, gl, a) => next((c, gl, a)), h => AddRequest += h, h => AddRequest -= h);
      AddRequestObs
        //.Delay(TimeSpan.FromMilliseconds(1000))
        .ObserveOn(ThreadPoolScheduler.Instance)
        //.SubscribeOn(TaskPoolScheduler.Default)
        //.Distinct(t => t.Item1.Instrument)
        .Subscribe(t => AddRequestSync(t.c, t.cb, t.gl));
    }

    private void OnTickGeneric(int tickerId, int field, double value) => OnTickPrice(tickerId, field, value, new TickAttrib());

    void AddRequestSync(Contract contract, Action<Contract> callback, string genericTickList = "") {
      if(contract.IsCombo) {
        AddRequestImpl(contract.AddToCache(), callback, genericTickList);
      } else {
        IbClient.ReqContractDetailsCached(contract)
          .Take(1)
          .Subscribe(cd => {
            AddRequestImpl(cd.Contract, callback, genericTickList);
          });
      }
    }
    object _addRequestImplLock = new object();
    public void AddRequestImpl(Contract contract, Action<Contract> callback, string genericTickList) {
      lock(_addRequestImplLock) {
        if(activeRequests.Any(ar => ar.Value.contract.Instrument == contract.Instrument)) {
          Verbose0($"AddRequest:{contract} already requested");
          callback(contract);
        } else {
          IbClient.OnReqMktData(() => {
            var reqId = IbClient.ValidOrderId();
            Verbose0($"AddRequest:{reqId}=>{contract}");
            IbClient.WatchReqError(reqId, t => Trace($"{nameof(AddRequestImpl)}:{contract}: {t}"), () => TraceIf(DoShowRequestErrorDone, $"AddRequest: {contract} => {reqId} Error done."));
            IbClient.ClientSocket.reqMktData(reqId, contract.ContractFactory(), genericTickList, false, false, new List<TagValue>());
            activeRequests.TryAdd(reqId, (contract, new Price(contract.Instrument)));
            contract.ReqId = reqId;
            if(reqId == 0)
              Debugger.Break();
            callback(contract);
          });
        }
      }
    }

    public bool TryGetPrice(Contract contract, out Price price, [CallerMemberName] string Caller = "") {
      price = null;
      var symbol = contract.Instrument;
      if(!_currentPrices.Contains(symbol)) {
        IbClient.SetContractSubscription(contract, $"{nameof(TryGetPrice)} <= {Caller}");
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
          RaisePriceChanged(t);
          break;
      }
    }
    private void OnTickPrice(int requestId, int field, double price, TickAttrib attrib) {
      if(!activeRequests.ContainsKey(requestId)) return;
      var ar = activeRequests[requestId];
      var priceMessage = new TickPriceMessage(requestId, field, price, attrib);
      if(false) TraceDebug(new[] { new { Price = ar.price, field, price } }.ToTextOrTable($"{nameof(RaisePriceChanged)}:{ ShowThread()}"));
      var price2 = ar.price;
      //Trace($"{nameof(OnTickPrice)}:{price2.Pair}:{(requestId, field, price).ToString()}");
      if(priceMessage.Price <= 0)
        return;
      const int LOW_52 = 19;
      const int HIGH_52 = 20;
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
          //RaisePriceChanged(ar);
          break;
        case 37:
          if(price2.Bid <= 0 && price2.Ask <= 0) {
            price2.Bid = price2.Ask = price;
            price2.Time2 = IbClient.ServerTime;
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
    event PriceChangedEventHandler PriceChangedEvent;
    public event PriceChangedEventHandler PriceChanged {
      add {
        if(PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
          PriceChangedEvent += value;
      }
      remove {
        PriceChangedEvent -= value;
      }
    }
    protected void RaisePriceChanged((Contract contract, Price price) t, [CallerMemberName] string caller = "") {
      if(!_currentPrices.Contains(t.price.Pair)) {
        {
          var cip = t.contract.HasOptions ? new CacheItemPolicy() {
            RemovedCallback = ce => {
              ActiveRequestCleaner((Price)ce.CacheItem.Value);
            },
            SlidingExpiration = 60.FromSeconds()
          } : new CacheItemPolicy();
          if(!_currentPrices.Add(t.price.Pair, t.price, cip))
            TraceError($"RaisePriceChanged: {t.price.Pair} is already in {nameof(_currentPrices)}");
          //else TraceDebug($"_currentPrices.Add({t.price.Pair}, {t.price}, cip)");
        }
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
          TraceDebug($"{nameof(activeRequests)} - removed {ar.Value.contract}");
        }
      }
    }
    public static Contract[] ActiveRequestCleaner() {
      var contracts = activeRequests.Select(kv => kv.Value.contract).ToArray();
      _currentPrices.ToList().ForEach(cp => _currentPrices.Remove(cp.Key));
      contracts.ForEach(c => IBClientMaster.SetContractSubscription(c));
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
