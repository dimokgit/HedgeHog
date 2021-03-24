﻿/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IBApi;
using HedgeHog.Shared;
using HedgeHog.Core;
using HedgeHog;

namespace IBApp {
  /*
   * Contracts can be defined in multiple ways. The TWS/IB Gateway will always perform a query on the available contracts
   * and find which one is the best candidate:
   *  - More than a single candidate will yield an ambiguity error message.
   *  - No suitable candidates will produce a "contract not found" message.
   *  How do I find my contract though?
   *  - Often the quickest way is by looking for it in the TWS and looking at its description there (double click).
   *  - The TWS' symbol corresponds to the API's localSymbol. Keep this in mind when defining Futures or Options.
   *  - The TWS' underlying's symbol can usually be mapped to the API's symbol.
   *
   * Any stock or option symbols displayed are for illustrative purposes only and are not intended to portray a recommendation.
   */
  public static class ContractSamples {
    public static Contract ContractFactory(this Contract contract, bool isInTest = false) =>
      (contract.IsFuturesCombo
      ? contract.CloneJson().SideEffect(c => {
        c.TradingClass = null;
      })
      : contract.ComboLegs?.Any() == true
      ? contract
      : new Contract {
        LocalSymbol = contract.LocalSymbol,
        Symbol = contract.Symbol,
        Exchange = contract.Exchange,
        SecType = contract.SecType,
        Currency = contract.Currency,
        ComboLegs = contract.ComboLegs
      }).SetTestConId(isInTest, 0);

    public static Contract ContractFactory(this string pair) => pair.ContractFactory(false, 0);
    public static Contract ContractFactory(this string pair, bool isInTest,int multiplier) {
      if(pair.IsNullOrWhiteSpace()) throw new Exception(new { pair } + "");
      pair = pair.ToUpper();
      return (pair.IsCurrenncy()
       ? FxContract(pair)
       : pair.IsIndex()
       ? Index(pair.Split(' ').First(), "")
       : pair.IsOption()
       ? Option(pair)
       : pair.IsFuture()
       ? Future(pair)
       : pair.IsCommodity()
       ? Commodity(pair)
       : USStock(pair)).SetTestConId(isInTest, multiplier);
    }
    public static Contract FxContract(string pair) {
      return FxPair(pair);
    }
    public static Contract CommodityContract(string commodity) {
      return Commodity(commodity);
    }
    public static Contract Commodity(string symbol) {
      //EXSTART::usstock::csharp
      Contract contract = new Contract() { LocalSymbol = symbol };
      contract.Symbol = symbol;
      contract.SecType = "CMDTY";
      contract.Currency = "USD";
      contract.Exchange = "SMART";
      //EXEND
      return contract;
    }
    public static Contract USStock(string symbol) {
      //EXSTART::usstock::csharp
      Contract contract = new Contract() { LocalSymbol = symbol };
      contract.Symbol = symbol;
      contract.SecType = "STK";
      contract.Currency = "USD";
      contract.Exchange = "SMART";
      //EXEND
      return contract;
    }
    public static Contract Future(string symbol) {
      Contract contract = new Contract();
      contract.SecType = "FUT";
      //contract.Exchange = "GLOBEX";
      contract.Currency = "USD";
      contract.LocalSymbol = symbol;
      contract.IncludeExpired = true;
      ;
      //EXEND
      return contract;
    }
    public static Contract Index(string symbol, string exchange) {
      //EXSTART::normaloption::csharp
      Contract contract = new Contract();
      contract.Symbol = symbol;
      contract.SecType = "IND";
      contract.Exchange = exchange;
      contract.Currency = "USD";
      return contract;
    }
    //SPXW  180305C02680000
    public static Contract Option(string symbol) {
      //EXSTART::normaloption::csharp
      Contract contract = new Contract();
      contract.LocalSymbol = symbol;
      contract.SecType = "OPT";
      contract.Exchange = "SMART";
      contract.Currency = "USD";
      return contract;
    }

    public static Contract Option(string symbol, DateTime lastDate, double strike, bool isCall, string tradingClass = null)
      => Option(symbol, lastDate.ToTWSDateString(), strike, isCall, tradingClass);
    public static Contract Option(string symbol, string lastDate, double strike, bool isCall, string tradingClass = null) {
      //EXSTART::normaloption::csharp
      Contract contract = new Contract();
      contract.Symbol = symbol;
      contract.SecType = "OPT";
      contract.Exchange = "SMART";
      contract.Currency = "EUR";
      contract.LastTradeDateOrContractMonth = lastDate;
      contract.Strike = 100;
      contract.Right = isCall ? "C" : "P";
      contract.Multiplier = "100";
      //Often, contracts will also require a trading class to rule out ambiguities
      if(!string.IsNullOrWhiteSpace(tradingClass))
        contract.TradingClass = tradingClass;
      //EXEND
      return contract;
    }


    /*
     * Usually, the easiest way to define a Stock/CASH contract is through these four attributes.
     */
    static Contract FxPair(string instrument) {
      var pair2 = (from Match m in Regex.Matches(Regex.Replace(instrument, @"\W*", ""), @"\w{3}") select m.Value.ToUpper()).ToArray();
      //EXSTART::eurgbpfx::csharp
      Contract contract = new Contract() { LocalSymbol = string.Join(".", pair2) };
      contract.Symbol = pair2[0];
      contract.SecType = "CASH";
      contract.Currency = pair2[1];
      contract.Exchange = "IDEALPRO";
      //EXEND
      return contract;
    }
    public static Contract EurGbpFx() {
      //EXSTART::eurgbpfx::csharp
      Contract contract = new Contract();
      contract.Symbol = "EUR";
      contract.SecType = "CASH";
      contract.Currency = "GBP";
      contract.Exchange = "IDEALPRO";
      //EXEND
      return contract;
    }

    public static Contract EuropeanStock() {
      //EXSTART::europeanstock::csharp
      Contract contract = new Contract();
      contract.Symbol = "SMTPC";
      contract.SecType = "STK";
      contract.Currency = "EUR";
      contract.Exchange = "BATEEN";
      //EXEND
      return contract;
    }

    public static Contract OptionAtIse() {
      //EXSTART::optionatise::csharp
      Contract contract = new Contract();
      contract.Symbol = "BPX";
      contract.SecType = "OPT";
      contract.Currency = "USD";
      contract.Exchange = "ISE";
      contract.LastTradeDateOrContractMonth = "20160916";
      contract.Right = "C";
      contract.Strike = 65;
      contract.Multiplier = "100";
      //EXEND
      return contract;
    }

    public static Contract USStock() {
      //EXSTART::usstock::csharp
      Contract contract = new Contract();
      contract.Symbol = "IBKR";
      contract.SecType = "STK";
      contract.Currency = "USD";
      contract.Exchange = "SMART";
      //EXEND
      return contract;
    }

    public static Contract OptionAtBOX() {
      //EXSTART::optionatbox::csharp
      Contract contract = new Contract();
      contract.Symbol = "GOOG";
      contract.SecType = "OPT";
      contract.Exchange = "BOX";
      contract.Currency = "USD";
      contract.LastTradeDateOrContractMonth = "20170120";
      contract.Strike = 615;
      contract.Right = "C";
      contract.Multiplier = "100";
      //EXEND
      return contract;
    }

    /*
     * Option contracts require far more information since there are many contracts having the exact same
     * attributes such as symbol, currency, strike, etc.
     */
    public static Contract NormalOption() {
      //EXSTART::normaloption::csharp
      Contract contract = new Contract();
      contract.Symbol = "BAYN";
      contract.SecType = "OPT";
      contract.Exchange = "DTB";
      contract.Currency = "EUR";
      contract.LastTradeDateOrContractMonth = "20161216";
      contract.Strike = 100;
      contract.Right = "C";
      contract.Multiplier = "100";
      //Often, contracts will also require a trading class to rule out ambiguities
      contract.TradingClass = "BAY";
      //EXEND
      return contract;
    }

    /*
     * This contract for example requires the trading class too in order to prevent any ambiguity.
     */
    public static Contract OptionWithTradingClass() {
      Contract contract = new Contract();
      contract.Symbol = "SANT";
      contract.SecType = "OPT";
      contract.Exchange = "MEFFRV";
      contract.Currency = "EUR";
      contract.LastTradeDateOrContractMonth = "20190621";
      contract.Strike = 7.5;
      contract.Right = "C";
      contract.Multiplier = "100";
      contract.TradingClass = "SANEU";
      //EXEND
      return contract;
    }

    /*
     * Future contracts also require an expiration date but are less complicated than options.
     */
    public static Contract SimpleFuture() {
      Contract contract = new Contract();
      contract.Symbol = "ES";
      contract.SecType = "FUT";
      contract.Exchange = "GLOBEX";
      contract.Currency = "USD";
      contract.LastTradeDateOrContractMonth = "201612";
      //EXEND
      return contract;
    }

    /*
     * Rather than giving expiration dates we can also provide the local symbol
     * attributes such as symbol, currency, strike, etc.
     */
    public static Contract FutureWithLocalSymbol() {
      Contract contract = new Contract();
      contract.SecType = "FUT";
      contract.Exchange = "GLOBEX";
      contract.Currency = "USD";
      contract.LocalSymbol = "ESU6";
      ;
      //EXEND
      return contract;
    }

    /*
     * Note the space in the symbol!
     */
    public static Contract WrongContract() {
      Contract contract = new Contract();
      contract.Symbol = " IJR ";
      contract.ConId = 9579976;
      contract.SecType = "STK";
      contract.Exchange = "SMART";
      contract.Currency = "USD";
      //EXEND
      return contract;
    }

    public static Contract FuturesOnOptions() {
      Contract contract = new Contract();
      contract.Symbol = "ES";
      contract.SecType = "FOP";
      contract.Exchange = "GLOBEX";
      contract.Currency = "USD";
      contract.LastTradeDateOrContractMonth = "20160617";
      contract.Strike = 1810;
      contract.Right = "C";
      contract.Multiplier = "50";
      //EXEND
      return contract;
    }

    /*
     * It is also possible to define contracts based on their ISIN (IBKR STK sample).
     *
     */
    public static Contract ByISIN() {
      Contract contract = new Contract();
      contract.SecIdType = "ISIN";
      contract.SecId = "US45841N1072";
      contract.Exchange = "SMART";
      contract.Currency = "USD";
      contract.SecType = "STK";
      //EXEND
      return contract;
    }

    /*
     * Or their conId (EUR.USD sample).
     * Note: passing a contract containing the conId can cause problems if one of the other provided
     * attributes does not match 100% with what is in IB's database. This is particularly important
     * for contracts such as Bonds which may change their description from one day to another.
     * If the conId is provided, it is best not to give too much information as in the example below.
     */
    public static Contract ByConId() {
      Contract contract = new Contract();
      contract.SecType = "CASH";
      contract.ConId = 12087792;
      contract.Exchange = "IDEALPRO";
      //EXEND
      return contract;
    }

    /*
     * Ambiguous contracts are great to use with reqContractDetails. This way you can
     * query the whole option chain for an underlying. Bear in mind that there are
     * pacing mechanisms in place which will delay any further responses from the TWS
     * to prevent abuse.
     */
    public static Contract OptionForQuery() {
      Contract contract = new Contract();
      contract.Symbol = "FISV";
      contract.SecType = "OPT";
      contract.Exchange = "SMART";
      contract.Currency = "USD";
      //EXEND
      return contract;
    }

    /*
     * STK Combo contract
     * Leg 1: 43645865 - IBKR's STK
     * Leg 2: 9408 - McDonald's STK
     */
    public static Contract StockComboContract() {
      Contract contract = new Contract();
      contract.Symbol = "MCD";
      contract.SecType = "BAG";
      contract.Currency = "USD";
      contract.Exchange = "SMART";

      ComboLeg leg1 = new ComboLeg();
      leg1.ConId = 43645865;
      leg1.Ratio = 1;
      leg1.Action = "BUY";
      leg1.Exchange = "SMART";

      ComboLeg leg2 = new ComboLeg();
      leg2.ConId = 9408;
      leg2.Ratio = 1;
      leg2.Action = "SELL";
      leg2.Exchange = "SMART";

      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.Add(leg1);
      contract.ComboLegs.Add(leg2);

      //EXEND
      return contract;
    }

    /*
     * CBOE Volatility Index Future combo contract
     * Leg 1: 195538625 - FUT expiring 2016/02/17
     * Leg 2: 197436571 - FUT expiring 2016/03/16
     */
    public static Contract FutureComboContract() {
      Contract contract = new Contract();
      contract.Symbol = "VIX";
      contract.SecType = "BAG";
      contract.Currency = "USD";
      contract.Exchange = "CFE";

      ComboLeg leg1 = new ComboLeg();
      leg1.ConId = 195538625;
      leg1.Ratio = 1;
      leg1.Action = "BUY";
      leg1.Exchange = "CFE";

      ComboLeg leg2 = new ComboLeg();
      leg2.ConId = 197436571;
      leg2.Ratio = 1;
      leg2.Action = "SELL";
      leg2.Exchange = "CFE";

      contract.ComboLegs = new List<ComboLeg>();
      contract.ComboLegs.Add(leg1);
      contract.ComboLegs.Add(leg2);

      //EXEND
      return contract;
    }
  }
}