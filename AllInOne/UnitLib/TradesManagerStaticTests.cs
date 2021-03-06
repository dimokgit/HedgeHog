﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HedgeHog.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace HedgeHog.Shared.Tests {
  [TestClass()]
  public class TradesManagerStaticTests {
    [TestMethod()]
    public void PipAmount() {
      try {
        Assert.AreEqual(18.251244785358632, TradesManagerStatic.PipAmount("USD_JPY", 217000, 118.896, 0.01));
        Assert.Fail("Not ArgumentNullException was not thrown.");
      } catch(ArgumentNullException) {
        TradesManagerStatic.AccountCurrency = "uSd";
        Assert.AreEqual(18.251244785358632, TradesManagerStatic.PipAmount("USD_JPY", 217000, 118.896, 0.01));
        Assert.AreEqual(21.7, TradesManagerStatic.PipAmount("eurUSD", 217000, 118.896, 0.0001));
        try {
          TradesManagerStatic.PipAmount("eurUSX", 217000, 118.896, 0.0001);
          Assert.Fail("Not Supported exception was not thrown.");
        } catch(NotSupportedException) { }
        try {
          TradesManagerStatic.PipAmount("USD", 217000, 118.896, 0.01);
          Assert.Fail("Not ArgumentException was not thrown.");
        } catch(ArgumentException) {
        }
      } finally {
        TradesManagerStatic.AccountCurrency = null;
      }
    }

    [TestMethod()]
    public void PipCostTest() {
      try {
        TradesManagerStatic.AccountCurrency = "uSd";
        Assert.AreEqual(0.1, TradesManagerStatic.PipCost("eur/usd", 118.896, 1000, 0.0001));
        Assert.AreEqual(Math.Round(0.0841071188265375, 14), Math.Round(TradesManagerStatic.PipCost("USDJPY", 118.896, 1000, 0.01), 14));
      } finally {
        TradesManagerStatic.AccountCurrency = null;
      }
    }
    [TestMethod()]
    public void PipsAndLotToMoney() {
      try {
        TradesManagerStatic.AccountCurrency = "uSd";
        var ptm = TradesManagerStatic.PipsAndLotToMoney("usdjpy", 2.1, 217000, 118.896, 0.01);
        Assert.AreEqual(38.327614, Math.Round(ptm, 6));
        Assert.AreEqual(2.100000, Math.Round(TradesManagerStatic.MoneyAndLotToPips("usdjpy", ptm, 217000, 118.896, 0.01), 6));

        ptm = TradesManagerStatic.PipsAndLotToMoney("eur/usd", 2.1, 20000, 1.3333, 0.0001);
        Assert.AreEqual(4.2, ptm);
        Assert.AreEqual(2.1, TradesManagerStatic.MoneyAndLotToPips("eurusd", ptm, 20000, 1.5555, 0.0001));

        Assert.AreEqual(100, TradesManagerStatic.PipsAndLotToMoney("spy", (255 - 254) / 0.01, 100, 254, 0.01));
      } finally {
        TradesManagerStatic.AccountCurrency = null;
      }
    }

    [TestMethod()]
    public void IsCurrenncyTest() {
      Assert.IsTrue("usd/jpy".IsCurrenncy());
      Assert.IsFalse("XAUusd".IsCurrenncy());
    }

    [TestMethod()]
    public void PipByPairTest() {
      TradesManagerStatic.AccountCurrency = "USD";
      Assert.AreEqual(0.088733900342957, TradesManagerStatic.PipsAndLotToMoney("USD.JPY", 1, 1000, 112.6965, 0.01).Round(15));
    }

    [TestMethod()]
    public void IsFuture() {
      var f = "RTYH9";
      Assert.IsTrue(TradesManagerStatic.IsFuture(f));
      Assert.AreEqual("RTY", TradesManagerStatic.FutureCode(f));
    }

    [TestMethod()]
    public void HedgeRatioByValue() {
      var res = TradesManagerStatic.HedgeRatioByValue(" ",true, ("C1",new string[0], 2992.25, 12.75, 50, ""), ("C2", new string[0], 7854.25, 49.75, 20, ""));
      Assert.AreEqual(("C1", 1), res[0]);
      Assert.AreEqual(("C2", 0.610225), res[1]);
    }
    [TestMethod()]
    public void HedgeRatio() {
      var res = TradesManagerStatic.HedgeQuanitiesByValue(10, " ",true, ("C1", new string[0], 2992.25, 12.75, 50, ""), ("C2", new string[0], 7854.25, 49.75, 20, ""));
      Assert.AreEqual(2, res.quantity);
      Assert.AreEqual(("C1", 5), res.contracts[0]);
      Assert.AreEqual(("C2", 3), res.contracts[1]);
    }

    [TestMethod()]
    public void HedgeRatios() {
      var res = TradesManagerStatic.HedgeQuanitiesByValue(10, " ",true, ("C1", new string[0], 2992.25, 12, 50, ""), ("C1", new string[0], 2992.25, 0.75, 50, ""), ("C2", new string[0], 7854.25, 49.75, 20, ""));
      Assert.AreEqual(("C1", 8), res.contracts[0]);
      Assert.AreEqual(("C2", 5), res.contracts[1]);
    }

    [TestMethod()]
    public void PriceFromProfit() {
      var price = TradesManagerStatic.PriceFromProfitAmount(509, -5, 20, 6759.84);
      Assert.AreEqual(6754.75, price);
    }
  }
}
/*
function CalcPV()
{
with (document.f)
{
	if(!ls.value)
	{
		alert('Enter your position size, please.');
		return false;
	}
	else if((!price.value) && (currency.value != SecondSelCur))
	{
		alert('Enter the current price, please.');
		return false;
	}
	price.value = price.value.replace(",", ".");
	var t;
	var UnitCosts;

	if (currency.value != "JPY") UnitCosts = 0.0001; else UnitCosts = 0.01
	if (SecondAffCur != 0){
	 if (stype == " Ask price") {
     if (SecondAffCur == "JPY") UnitCosts = UnitCosts / (price.value / 100); 
     else UnitCosts = UnitCosts / price.value;
   }
	 else {if (SecondAffCur == "JPY") UnitCosts = (UnitCosts * price.value) / 100; else UnitCosts = UnitCosts * price.value;}
	}
	pip.value = UnitCosts * ls.value;
	createCookie('earnforex_psc_currency',currency.value,31);
}
	return true;
}
*/
