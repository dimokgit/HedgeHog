/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.util;

namespace IBApp {
  public class MarketDataManager :DataManager {
    const int TICK_ID_BASE = 10000000;
    private readonly ConcurrentDictionary<string, Price> _currentPrices = new ConcurrentDictionary<string, Price>(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<int, (Contract contract, Price price)> activeRequests = new ConcurrentDictionary<int, (Contract contract, Price price)>();
    public Action<Contract, string, Action<Contract>> AddRequest;// = (a1, a2, a3) => {    };
    public IObservable<(Contract c, string gl, Action<Contract>)> AddRequestObs { get; }
    public MarketDataManager(IBClientCore client) : base(client, TICK_ID_BASE) {
      IbClient.TickPriceObservable.Subscribe(t => OnTickPrice(t.reqId, t.field, t.price, t.canAutoExecute));
      IbClient.TickString += OnTickString; ;
      IbClient.TickGeneric += OnTickGeneric;
      //IbClient.TickOptionCommunication += TickOptionCommunication; ;
      AddRequestObs = Observable.FromEvent<Action<Contract, string, Action<Contract>>, (Contract c, string gl, Action<Contract>)>(
        next => (c, gl, a) => next((c, gl, a)), h => AddRequest += h, h => AddRequest -= h);
      AddRequestObs
        //.Delay(TimeSpan.FromMilliseconds(1000))
        .ObserveOn(TaskPoolScheduler.Default)
        .SubscribeOn(TaskPoolScheduler.Default)
        .Distinct(t => t.Item1.Instrument)
        .Subscribe(t => AddRequestSync(t.Item1, t.Item3, t.Item2));
    }

    private void OnTickGeneric(int tickerId, int field, double value) => OnTickPrice(tickerId, field, value, 0);

    void AddRequestSync(Contract contract, Action<Contract> callback, string genericTickList = "") {
      if(contract.IsCombo) {
        AddRequestImpl(contract.AddToCache(), genericTickList);
      } else {
        IbClient.ReqContractDetailsCached(contract.Instrument)
          .Take(1)
          .Subscribe(cd => {
            AddRequestImpl(cd.Summary, genericTickList);
          });
      }
      callback(contract);
    }
    void AddRequestImpl(Contract contract, string genericTickList) {
      if(activeRequests.Any(ar => ar.Value.contract.Instrument == contract.Instrument))
        Trace($"AddRequest:{contract} already requested");
      else {
        var reqId = NextReqId();
        Verbose0($"AddRequest:{reqId}=>{contract}");
        IbClient.WatchReqError(reqId, t => Trace($"{nameof(AddRequestImpl)}:{contract}: {t}"), () => TraceIf(DoShowRequestErrorDone, $"AddRequest: {contract} => {reqId} Error done."));
        IbClient.OnReqMktData(() => IbClient.ClientSocket.reqMktData(reqId, contract.ContractFactory(), genericTickList, false, new List<TagValue>()));
        if(reqId == 0)
          Debugger.Break();
        activeRequests.TryAdd(reqId, (contract, new Price(contract.Instrument)));
        contract.ReqId = reqId;
      }
    }

    public bool TryGetPrice(Contract contract, out Price price) {
      price = null;
      var symbol = contract.Instrument;
      if(!_currentPrices.ContainsKey(symbol)) {
        IbClient.SetContractSubscription(contract);
        return false;
      }
      price = _currentPrices[symbol];
      return true;
    }
    public Price GetPrice(string symbol) {
      if(!_currentPrices.ContainsKey(symbol))
        throw new KeyNotFoundException(new { _currentPrices = new { symbol, not = " found" } } + "");
      return _currentPrices[symbol];
    }
    private void OnTickString(int tickerId, int tickType, string value) {
      //Trace($"{nameof(OnTickGeneric)}{(tickerId, tickType, value)}");
      if(!activeRequests.TryGetValue(tickerId, out var t)) return;
      var price = t.Item2;
      switch(tickType) {
        // RT Volume
        case 48:
          RaisePriceChanged(price);
          break;
      }
    }
    private void OnTickPrice(int requestId, int field, double price, int canAutoExecute) {
      if(!activeRequests.ContainsKey(requestId)) return;
      var priceMessage = new TickPriceMessage(requestId, field, price, canAutoExecute);
      var price2 = activeRequests[requestId].price;
      //Trace($"{nameof(OnTickPrice)}:{price2.Pair}:{(requestId, field, price).ToString()}");
      if(priceMessage.Price == 0)
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
              RaisePriceChanged(price2);
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
              RaisePriceChanged(price2);
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
            RaisePriceChanged(price2);
            break;
          }
        case 0:
        case 3:
        case 5:
          RaisePriceChanged(price2);
          break;
        case 37:
          if(price2.Bid <= 0 && price2.Ask <= 0) {
            price2.Bid = price2.Ask = price;
            RaisePriceChanged(price2);
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
    protected void RaisePriceChanged(Price price) {
      _currentPrices.AddOrUpdate(price.Pair, price, (s, p) => price);
      PriceChangedEvent?.Invoke(price);
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
