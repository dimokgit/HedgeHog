/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HedgeHog.Shared;
using IBApi;
using IBSampleApp.util;

namespace IBApp {
  public class MarketDataManager : DataManager {
    const int TICK_ID_BASE = 10000000;

    private Dictionary<int, Tuple<Contract,Price>> activeRequests = new Dictionary<int, Tuple<Contract,Price>>();

    public MarketDataManager(IBClientCore client ) : base(client, TICK_ID_BASE) { }

    public void AddRequest(Contract contract, string genericTickList="") {
      if(activeRequests.Any(ar => ar.Value.Item1.Instrument.ToUpper() == contract.Instrument.ToUpper())) {
        return;
      }
      var reqId = NextReqId();
      IbClient.TickPrice += OnTickPrice;
      //IbClient.TickSize += OnTickSize;
      IbClient.ClientSocket.reqMktData(reqId, contract, genericTickList, false, new List<TagValue>());
      activeRequests.Add(reqId, Tuple.Create(contract,new Price(contract.Instrument)));
    }

    private void OnTickPrice(int requestId, int field, double price, int canAutoExecute) {
      var priceMessage = new TickPriceMessage(requestId, field, price, canAutoExecute);
      var price2 = activeRequests[requestId].Item2;
      switch(priceMessage.Field) {
        case 1: {
            //BID
            if(price2.Bid == priceMessage.Price)
              break;
            price2.Bid = priceMessage.Price;
            RaisePriceChanged(price2);
            break;
          }
        case 2: {
            //ASK
            if(price2.Ask == priceMessage.Price)
              break;
            price2.Ask = priceMessage.Price;
            RaisePriceChanged(price2);
            break;
          }
        case 9: {
            //CLOSE
            //grid[CLOSE_PRICE_INDEX, GetIndex(dataMessage.RequestId)].Value = priceMessage.Price;
            break;
          }
      }
    }


    #region PriceChanged Event
    event PriceChangedEventHandler PriceChangedEvent;
    public event PriceChangedEventHandler PriceChanged {
      add {
        if (PriceChangedEvent == null || !PriceChangedEvent.GetInvocationList().Contains(value))
          PriceChangedEvent += value;
      }
      remove {
        PriceChangedEvent -= value;
      }
    }
    protected void RaisePriceChanged(Price price) {
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
