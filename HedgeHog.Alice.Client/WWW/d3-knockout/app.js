// jscs:disable
/// <reference path="http://knockoutjs.com/downloads/knockout-3.3.0.js" />
/// <reference path="https://code.jquery.com/jquery-2.1.3.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/underscore.js/1.8.2/underscore.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/knockout.mapping/2.4.1/knockout.mapping.min.js" />
/// <reference path="../Scripts/bootstrap-notify.min.js" />
/// <reference path="https://cdn.rawgit.com/ValYouW/jqPropertyGrid/9218bbd5df05bf7efe58591f434ea27ece11a045/jqPropertyGrid.js" />
/*global ko,_*/

//var D3KD = this.D3KD || {};

(function () {
  // #region Globals
  "use strict";
  var chat;
  var pair = "usdjpy";
  function settingsGrid() { return $("#settingsGrid"); }
  // #endregion

  // #region Reset plotter
  var resetPlotter = _.throttle(resetPlotterImpl, 250);
  function resetPlotterImpl() {
    chat.server.askRates(1200, dataViewModel.firstDate().toISOString(), dataViewModel.lastDate().toISOString(), pair);
    chat.server.askRates2(1200, dataViewModel.firstDate2().toISOString(), dataViewModel.lastDate2().toISOString(), pair);
  }
  // #endregion

  // #region dataViewModel
  var dataViewModel = new DataViewModel();
  function DataViewModel() {
    var self = this;

    // #region Locals
    function lineChartDataEmpty() {
      return [{ d: new Date("1/1/1900"), c: 0 }];// jshint ignore:line
    }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    var lineChartData2 = ko.observableArray(lineChartDataEmpty());
    // #region Server proxies
    // #region pending request messages
    var pendingMessages = [];
    function clearPendingMessages() { pendingMessages.forEach(function (m) { m.close(); }) }
    function addPendingMessage(message, settings) { pendingMessages.push(showInfo(message, $.extend({ delay: 0 }, settings))); }
    // #endregion
    function serverCall(name, args, done) {
      var r = chat.server[name].apply(chat.server, args)
        .then(clearPendingMessages)
        .fail(function (error) {
          $.notify(error + "");
        });
      if (done) r.done(done);
      addPendingMessage(name + " is in progress ...");
    }
    function setTradeLevelActive(levelIndex) {
      serverCall("startTrades", [pair, levelIndex === 0], resetPlotter);
    }
    this.flipTradeLevels = function () {
      serverCall("flipTradeLevels", [pair], resetPlotter);
    };
    this.setTradeLevels = function (l, isBuy) {
      chat.server.setPresetTradeLevels(pair, l, isBuy === undefined ? null : isBuy)
      .fail(function (e) {
        alert(e.message);
      });
      resetPlotter();
    };
    function toggleIsActive(/*date, event*/) {
      chat.server.toggleIsActive(pair);
      resetPlotter();
    }
    function toggleStartDate() {
      serverCall("toggleStartDate", [pair]);
    }
    function saveTradeSettings() {
      var ts = settingsGrid().jqPropertyGrid('get');
      serverCall("saveTradeSettings", [pair, ts], function () {
        showSuccess("Trade settings saved.");
      });
    }
    function setCorridorStartDate(chartNumber, index) {
      serverCall("setCorridorStartDateToNextWave", [pair, chartNumber, index === 1], function () { showSuccess("Done."); });
    }
    function setTradeCount(tc) {
      chat.server.setTradeCount(pair, tc);
    }
    // #endregion
    // #endregion

    // #region Public
    // #region Trade Settings
    this.saveTradeSettings = saveTradeSettings;
    this.setTradeCount = setTradeCount;
    // #endregion
    // #region News
    this.readNews = function () {
      chat.server.readNews().done(function (news) {
        self.news(setJsonDate(news, "Time"));
      }).fail(function (error) {
        console.log('SignalR(readNews) error: ' + error);
      });
    };
    this.news = ko.observableArray([]);
    this.newsGrouped = ko.pureComputed(function () {
      showInfo("News Loaded");
      return _.chain(self.news())
        .groupBy(function (n) {
          return n.Time.getFullYear() + "-" + n.Time.getMonth() + "-" + n.Time.getDate();
        })
        .map(function (g) {
          return { date: g[0].Time.toString().match(/.+?\s.+?.+?\s[\S]+/)[0], values: g };
        }).value();
    });
    // #endregion
    this.profit = ko.observable(0);
    // #region Charts
    this.chartData = ko.observable(defaultChartData(0));
    this.chartData2 = ko.observable(defaultChartData(1));
    // #region updateChart(2)
    this.updateChart = updateChart;
    this.updateChart2 = updateChart2;
    var commonChartParts = {};
    function updateChart(response) {
      setTrendDates(response.trendLines);
      setTrendDates(response.trendLines2);
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(Date.parse(d.d));
      });
      var rates2 = response.rates2;
      rates2.forEach(function (d) {
        d.d = new Date(Date.parse(d.d));
      });
      var endDate = rates[0].d;
      var startDate = new Date(Date.parse(response.dateStart));
      lineChartData.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData.push.apply(lineChartData, rates);
      lineChartData.unshift.apply(lineChartData, rates2);
      //lineChartData.sort(function (a, b) { return a.d < b.d ? -1 : 1; });

      commonChartParts = { tradeLevels: response.tradeLevels, askBid: response.askBid, trades: response.trades };
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.trendLines2, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true, 0, response.hasStartDate));
    }
    function updateChart2(response) {
      if (!commonChartParts.tradeLevels) return;
      if (!response.rates) {
        self.chartData2(chartDataFactory(null, response.trendLines, response.trendLines2, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1));
        return;
      }
      setTrendDates(response.trendLines);
      setTrendDates(response.trendLines2);
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(Date.parse(d.d));
      });
      //rates = continuoseDates(rates, response.trendLines.dates);
      var x1 = _.last(lineChartData2());
      var x2 = _.last(rates);
      var shouldUpdateData = x1.d.valueOf() !== x2.d.valueOf() && x1.c !== x2.c;

      //if (shouldUpdateData)
      var endDate = rates[0].d;
      var startDate = new Date(Date.parse(response.dateStart));
      lineChartData2.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData2.push.apply(lineChartData2, rates);
      self.chartData2(chartDataFactory(lineChartData2, response.trendLines, response.trendLines2, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate));
    }
    // #endregion
    // #region LastDate
    this.lastDate = lastDate;
    this.lastDate2 = lastDate2;
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
    // #region FirstDate
    this.firstDate = firstDate;
    this.firstDate2 = firstDate2;
    function firstDateImpl(lineChartData) {
      return (lineChartData()[0] || {}).d;
    }
    function firstDate() {
      return firstDateImpl(lineChartData);
    }
    function firstDate2() {
      return firstDateImpl(lineChartData2);
    }
    // #endregion
    // #endregion
    // #endregion

    // #region Helpers
    function chartDataFactory(data, trendLines, trendLines2, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData, chartNum, hasStartDate) {
      setTrendDates(trendLines);
      setTrendDates(trendLines2);
      return {
        data: data ? data() : [],
        trendLines: trendLines,
        trendLines2: trendLines2,
        tradeLevels: tradeLevels,
        askBid: askBid || {},
        trades: trades || [],
        isTradingActive: isTradingActive || false,
        setTradeLevelActive: setTradeLevelActive,
        setCorridorStartDate: setCorridorStartDate,
        toggleIsActive: toggleIsActive,
        toggleStartDate: toggleStartDate,
        shouldUpdateData: shouldUpdateData,
        chartNum: chartNum,
        hasStartDate: hasStartDate
      };
    }
    function continuoseDates(data, dates) {
      var dates2 = [];
      data.reverse().reduce(function (prevValue, current) {
        var cd = current.d;
        if (prevValue) {
          current.d = prevValue = dateAdd(prevValue, "minute", -1);
        } else prevValue = current.d;
        if (dates.length > 0)
          dates.forEach(function (d) {
            if (d.valueOf() >= cd.valueOf()) {
              removeItem(dates, d);
              dates2.push(prevValue);
            }
          });
        return prevValue;
      }, 0);
      Array.prototype.push.apply(dates, dates2);
      return data.reverse();
    }
    function removeItem(array, item) {
      var i = array.indexOf(item);
      array.splice(i, 1);
    }
    function defaultChartData(chartNum) { return chartDataFactory(lineChartData, { dates: [] }, {}, null, null, null, false, false, chartNum); }
    // #endregion
  }
  // #endregion

  // #region Init SignalR hub
  var host = location.host.match(/localhost/i) ? "ruleover.com:91" : location.host;
  var hubUrl = location.protocol + "//" + host + "/signalr/hubs";
  $.getScript(hubUrl, init);
  // Init SignaR client
  function init() {
    //Set the hubs URL for the connection
    $.connection.hub.url = "http://" + host + "/signalr";
    $.connection.hub.error(function (error) {
      showErrorPerm(error);
    });
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
      if (pair === pairChanged)
        chat.server.askChangedPrice(pair);
    }
    chat.client.addRates = addRates;
    chat.client.addRates2 = addRates2;
    chat.client.addMessage = addMessage;
    chat.client.priceChanged = priceChanged;
    // #endregion
    var defs = [];
    // #region Start the connection.
    //$.connection.hub.logging = true;
    $.connection.hub.start().done(function () {

      //#region Load static data
      var defTS = defs.push($.Deferred()) - 1;
      chat.server.readTradeSettings(pair).done(function (ts) {
        var tsMeta = {
          CorridorCrossesMaximum: {
            type: 'number',
            options: { step: 1, numberFormat: "n" }
          },
          TakeProfitBSRatio: { type: 'number', options: { step: 0.1, numberFormat: "n" } }
        };
        settingsGrid().jqPropertyGrid(ts, tsMeta);
        defs[defTS].resolve();
      });
      dataViewModel.readNews();
      $.when.apply($, defs).done(function () {
        ko.applyBindings(dataViewModel);
      });
      //#endregion

      // #region This section should be implemented in dataViewModel
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
      $('#buySellInit').click(function () { invokeByName("wrapTradeInCorridor"); });
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
      // #endregion
    });
    // #endregion
    //setInterval(updateLineChartData, 10);
  }
  // #endregion

  // #region Helpers
  function notify(message, settings) {
    return $.notify(message, $.extend({
      element: $("body").eq(0),
      delay: 2000,
      placement: {
        from: "bottom",
        align: "right"
      }
    }, settings));
  }
  function showInfoShort(message, settings) {
    return showInfo(message, $.extend({ delay: 1000 }, settings));
  }
  function showInfo(message, settings) {
    return notify(message, settings);
  }
  function showSuccess(message, settings) {
    return notify(message, $.extend({ type: "success" }, settings));
  }
  function showError(message, settings) {
    return notify(message, $.extend({ type: "danger" }, settings));
  }
  /* jshint ignore:end */
  function showErrorPerm(message,settings) {
    return notify(message, $.extend({ delay: 0 }, settings));
  }
  function setTrendDates(tls) { if (tls) tls.dates = (tls.dates || []).map(function (d) { return new Date(d); }); }
  function setJsonDate(data, name) {
    data.forEach(function (d) {
      d[name] = new Date(Date.parse(d[name]));
    });
    return data;
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
    };
  })();
  // #endregion
  // #endregion
})();