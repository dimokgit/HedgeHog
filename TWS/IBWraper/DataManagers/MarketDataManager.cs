/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private Dictionary<int, (Contract contract, Price price)> activeRequests = new Dictionary<int, (Contract contract, Price price)>();

    public MarketDataManager(IBClientCore client) : base(client, TICK_ID_BASE) {
      IbClient.TickPrice += OnTickPrice;
      IbClient.TickPrice += OnTickPrice;
      IbClient.TickString += OnTickString; ;
      IbClient.TickGeneric += OnTickGeneric;
    }

    private void OnTickGeneric(int tickerId, int field, double value) => OnTickPrice(tickerId, field, value, 0);

    public void AddRequest(Contract contract, string genericTickList = "") {
      if(string.IsNullOrEmpty(contract.Exchange)) {
        IbClient.ReqContractDetailsAsync(contract)
        .Subscribe(cd => AddRequestImpl(cd.cd.Summary.ContractFactory(), genericTickList));
      } else AddRequestImpl(contract, genericTickList);
    }
    object _addRequestSync = new object();
    IScheduler es = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = nameof(MarketDataManager) });
    void AddRequestImpl(Contract contract, string genericTickList) {
      lock(_addRequestSync) {
        if(activeRequests.Any(ar => ar.Value.Item1.Instrument.ToUpper() == contract.Instrument.ToUpper()))
          return;
        var reqId = NextReqId();
        Trace($"{nameof(AddRequest)}: {new { reqId, contract }}");
        IbClient.ErrorFactory()
        .Where(t => t.id == reqId)
        .Window(TimeSpan.FromSeconds(5), es)
        .Take(2)
        .Concat()
        .Subscribe(t => Trace($"{contract}: {t}"), () => Trace($"AddRequest: {contract} Error done."));
        IbClient.ClientSocket.reqMktData(reqId, contract, genericTickList, false, new List<TagValue>());
        activeRequests.Add(reqId, (contract, new Price(contract.Instrument)));
      }
    }

    public bool TryGetPrice(string symbol, out Price price) {
      price = null;
      if(!_currentPrices.ContainsKey(symbol))
        return false;
      price = _currentPrices[symbol];
      return true;
    }
    public Price GetPrice(string symbol) {
      if(!_currentPrices.ContainsKey(symbol))
        throw new KeyNotFoundException(new { _currentPrices = new { symbol, not = " found" } } + "");
      return _currentPrices[symbol];
    }
    private void OnTickString(int tickerId, int tickType, string value) {
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
      if(priceMessage.Price == 0)
        return;
      switch(priceMessage.Field) {
        case 1: {
            //BID
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
        case 2: {
            //ASK
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
      }
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
      if(!IBClientCore.Contracts.ContainsKey(price.Pair))
        IbClient.ReqContractDetailsAsync(price.Pair.ContractFactory())
          .Subscribe(cd => {
            IBClientCore.Contracts.TryAdd(price.Pair, cd.cd);
            RaisePriceChangedImpl(price);
          });
      else
        RaisePriceChangedImpl(price);
    }
    protected void RaisePriceChangedImpl(Price price) {
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
