/// <reference path="http://knockoutjs.com/downloads/knockout-3.3.0.js" />
/// <reference path="https://code.jquery.com/jquery-2.1.3.min.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/underscore.js/1.8.2/underscore.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/knockout.mapping/2.4.1/knockout.mapping.min.js" />

/// <reference path="view-models/line-data-view-model.js" />
/*global ko*/

var D3KD = this.D3KD || {};

(function () {
  "use strict";
  // #region isHidden
  var isDocHidden = (function () {
    var hidden, visibilityChange;
    if (typeof document.hidden !== "undefined") { // Opera 12.10 and Firefox 18 and later support 
      hidden = "hidden";
      visibilityChange = "visibilitychange";
    } else if (typeof document.mozHidden !== "undefined") {
      hidden = "mozHidden";
      visibilityChange = "mozvisibilitychange";
    } else if (typeof document.msHidden !== "undefined") {
      hidden = "msHidden";
      visibilityChange = "msvisibilitychange";
    } else if (typeof document.webkitHidden !== "undefined") {
      hidden = "webkitHidden";
      visibilityChange = "webkitvisibilitychange";
    }
    return function () {
      return document[hidden];
    }
  })();
  // #endregion
  var chat;
  var pair = "usdjpy";
  var resetPlotter = _.throttle(resetPlotterImpl, 250);
  function resetPlotterImpl() {
    chat.server.askRates(1200, dataViewModel.lastDate().toISOString(), pair);
    chat.server.askRates2(1200, dataViewModel.lastDate2().toISOString(), pair);
  }

  // #region dataViewModel
  var dataViewModel = new function () {
    var self = this;
    /// Local
    function lineChartDataEmpty() { return [{ d: new Date("1/1/1900"), c: 0 }]; }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    var lineChartData2 = ko.observableArray(lineChartDataEmpty());
    var trendLines = {};
    var tradeLevels = {};
    var trades = [];
    var askBid = {};
    /// Public
    // #region Server proxies
    function setTradeLevelActive(levelIndex) {
      chat.server.startTrades(pair, levelIndex == 0);
      resetPlotter();
    }
    this.flipTradeLevels = function () {
      chat.server.flipTradeLevels(pair);
      resetPlotter();
    }
    this.setTradeLevels = function (date, event) {
      var l = parseInt(event.target.value);
      chat.server.setPresetTradeLevels(pair, l);
      resetPlotter();
    }
    function toggleIsActive(date, event) {
      chat.server.toggleIsActive(pair);
      resetPlotter();
    }
    function saveTradeSettings(tradeSettings) {
      chat.server.saveTradeSettings(pair, tradeSettings);
    }
    function setTradeCount(tc) {
      chat.server.setTradeCount(pair, tc);
    }
    // #endregion

    // #region Trade Settings
    this.tradeSettings = {
      IsTakeBack: ko.observable(),
      LimitProfitByRatesHeight: ko.observable(),
      CorridorCrossesMaximum:ko.observable()
    };
    this.saveTradeSettings = function (ts) {
      saveTradeSettings(ko.mapping.toJS(ts));
    }
    this.setTradeCount = setTradeCount;
    // #endregion
    this.chartNum = ko.observable(0);
    this.profit = ko.observable(0);
    this.chartData = ko.observable(defaultChartData());
    this.chartData2 = ko.observable(defaultChartData());
    this.chartNum.subscribe(function () {
      lineChartData(lineChartDataEmpty());
    });
    this.updateChart = updateChart;
    this.updateChart2 = updateChart2;
    this.lastDate = lastDate;
    this.lastDate2 = lastDate2;
    /// Locals
    var commonChartParts = {};
    /// Implementations
    function defaultChartData() { return chartDataFactory(lineChartData, { dates: [] }, {}) }
    function updateChart(response) {
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(Date.parse(d.d))
      });
      var endDate = rates[0].d;
      var startDate = new Date(Date.parse(response.dateStart));
      lineChartData.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData.push.apply(lineChartData, rates);
      commonChartParts = { tradeLevels: response.tradeLevels, askBid: response.askBid, trades: response.trades };
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true));
    }
    function continuoseDates(data) {
      data.reverse().reduce(function (prevValue, current) {
        if (prevValue) {
          current.d = prevValue = dateAdd(prevValue, "minute", -1);
        } else prevValue = current.d;
        return prevValue;
      }, 0);
      return data.reverse();
    }
    var emptyChartData = ko.observableArray([]);
    function updateChart2(response) {
      if (!commonChartParts.tradeLevels) return;
      if (!response.rates) {
        self.chartData2(chartDataFactory(null, response.trendLines, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData));
        return;
      }
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(Date.parse(d.d))
      });
      rates = continuoseDates(rates);
      var x1 = _.last(lineChartData2());
      var x2 = _.last(rates);
      var shouldUpdateData = x1.d.valueOf() != x2.d.valueOf() && x1.c != x2.c;

      //if (shouldUpdateData)
      var endDate = rates[0].d;
      var startDate = new Date(Date.parse(response.dateStart));
      lineChartData2.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData2.push.apply(lineChartData2, rates);
      self.chartData2(chartDataFactory(lineChartData2, response.trendLines, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData));
    }
    // #region LastDate
    function lastDateImpl(lineChartData) {
      var lastIndex = Math.max(0, lineChartData().length - 1);
      return (lineChartData()[lastIndex] || {}).d;
    }
    function lastDate() {
      return lastDateImpl(lineChartData);
    }
    function lastDate2() {
      return lastDateImpl(lineChartData2);
    }
    // #endregion
    function chartDataFactory(data, trendLines, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData) {
      if (trendLines)
        trendLines.dates = (trendLines.dates || []).map(function (d) { return new Date(d); });
      return {
        data: data ? data() : [],
        trendLines: trendLines,
        tradeLevels: tradeLevels,
        askBid: askBid || {},
        trades: trades || [],
        isTradingActive: isTradingActive || false,
        setTradeLevelActive: setTradeLevelActive,
        toggleIsActive: toggleIsActive,
        shouldUpdateData: shouldUpdateData
      };
    }
  }
  // #endregion
  var host = location.host.match(/localhost/i) ? "ruleover.com:91" : location.host;
  var hubUrl = location.protocol + "//" + host + "/signalr/hubs";
  $.getScript(hubUrl, init);
  function init() {
    //Set the hubs URL for the connection
    $.connection.hub.url = "http://" + host + "/signalr";
    // Declare a proxy to reference the hub.
    chat = $.connection.myHub;

    // #region Create functions that the hub can call to broadcast messages.
    function addMessage(response) {
      if (isDocHidden()) return;

      if (!$('#rsdMin').is(":focus")) $('#rsdMin').val(response.rsdMin);
      delete response.rsdMin;

      dataViewModel.profit(response.prf);
      delete response.prf;

      $('#discussion').text(JSON.stringify(response).replace(/["{}]/g, ""));

      resetPlotter();
    }
    function addRates(response) {
      dataViewModel.updateChart(response);
    }
    function addRates2(response) {
      dataViewModel.updateChart2(response);
    }
    function priceChanged(pairChanged) {
      if (pair == pairChanged)
        chat.server.askChangedPrice(pair);
    }
    function readTradeSettings(tradeSettings) {
      dataViewModel.tradeSettings = ko.mapping.fromJS(tradeSettings);
      ko.applyBindings(dataViewModel);
    }
    chat.client.addRates = addRates;
    chat.client.addRates2 = addRates2;
    chat.client.addMessage = addMessage;
    chat.client.priceChanged = priceChanged;
    chat.client.readTradeSettings = readTradeSettings;
    // #endregion
    // #region Start the connection.
    $.connection.hub.start().done(function () {
      chat.server.readTradeSettings(pair);
      chat.server.readNews().done(function (news) {
        debugger;
      });

      $('#sendmessage').click(function () {
        // Call the Send method on the hub.
        chat.server.send(pair, $('#message').val());
        // Clear text box and reset focus for next comment.
        $('#message').val('').focus();
      });
      $('#stopTrades').click(function () {
        chat.server.stopTrades(pair);
        resetPlotter();
      });
      $('#closeTrades').click(function () {
        chat.server.closeTrades(pair);
        resetPlotter();
      });
      $('#tradeCount').click(function () {
        chat.server.setTradeCount(pair, $("#tradeCounts").val());
        resetPlotter();
      });
      function moveTradeLeve(isBuy, pips) {
        chat.server.moveTradeLevel(pair, isBuy, pips);
        resetPlotter();
      }
      var pipStep = 0.5;
      $('#buyUp').click(function () {
        moveTradeLeve(true, pipStep);
      });
      $('#buyDown').click(function () {
        moveTradeLeve(true, -pipStep);
      });
      $('#sellUp').click(function () {
        moveTradeLeve(false, pipStep);
      });
      $('#sellDown').click(function () {
        moveTradeLeve(false, -pipStep);
      });
      $('#buySellInit').click(function () { invokeByName("wrapTradeInCorridor") });
      $('#manualToggle').click(function () {
        chat.server.manualToggle(pair);
        resetPlotter();
      });
      $('#sell').click(function () { invokeByName("sell"); });
      $('#buy').click(function () { invokeByName("buy"); });
      $('#toggleStartDate').click(function () { invokeByName("toggleStartDate"); });
      $('#toggleIsActive').click(function () { invokeByName("toggleIsActive"); });
      $('#flipTradeLevels').click(function () { invokeByName("flipTradeLevels"); });
      $('#setDefaultTradeLevels').click(function () { invokeByName("setDefaultTradeLevels"); });
      $('#alwaysOn').click(function () { invokeByName("setAlwaysOn"); });
      $('#rsdMin').change(function () {
        chat.server.setRsdTreshold(pair, $('#rsdMin').val());
        resetPlotter();
      });

      function invokeByName(func) {
        chat.server[func](pair);
        resetPlotter();
      }
    });
    // #endregion
    //setInterval(updateLineChartData, 10);
  }
  function dateAdd(date, interval, units) {
    var ret = new Date(date); //don't change original date
    switch (interval.toLowerCase()) {
      case 'year': ret.setFullYear(ret.getFullYear() + units); break;
      case 'quarter': ret.setMonth(ret.getMonth() + 3 * units); break;
      case 'month': ret.setMonth(ret.getMonth() + units); break;
      case 'week': ret.setDate(ret.getDate() + 7 * units); break;
      case 'day': ret.setDate(ret.getDate() + units); break;
      case 'hour': ret.setTime(ret.getTime() + units * 3600000); break;
      case 'minute': ret.setTime(ret.getTime() + units * 60000); break;
      case 'second': ret.setTime(ret.getTime() + units * 1000); break;
      default: ret = undefined; break;
    }
    return ret;
  }
}());