// jscs:disable
/// <reference path="../Scripts/pnotify.custom.min.js" />
/// http://sciactive.github.io/pnotify/#demos-simple
/// <reference path="http://knockoutjs.com/downloads/knockout-3.3.0.js" />
/// <reference path="https://code.jquery.com/jquery-2.1.3.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/underscore.js/1.8.2/underscore.js" />
/// <reference path="https://cdnjs.cloudflare.com/ajax/libs/knockout.mapping/2.4.1/knockout.mapping.min.js" />
/// <reference path="../Scripts/bootstrap-notify.min.js" />
/// <reference path="https://cdn.rawgit.com/ValYouW/jqPropertyGrid/9218bbd5df05bf7efe58591f434ea27ece11a045/jqPropertyGrid.js" />
/*global ko,_,PNotify*/

//var D3KD = this.D3KD || {};

(function () {
  // #region Globals
  "use strict";
  var chat;
  var pair = "usdjpy";
  var NOTE_ERROR = "error";
  function settingsGrid() { return $("#settingsGrid"); }
  // #endregion

  // #region Reset plotter
  var resetPlotter = _.throttle(resetPlotterImpl, 250);
  var ratesInFlight = false;
  var ratesInFlight2 = false;
  function resetPlotterImpl() {
    askRates();
    askRates2();
  }
  function askRates() {
    if (ratesInFlight) return;
    ratesInFlight = true;
    chat.server.askRates(1200, dataViewModel.firstDate().toISOString(), dataViewModel.lastDate().toISOString(), pair)
      .done(function (response) {
        dataViewModel.updateChart(response);
      })
      .always(function () { ratesInFlight = false; });
  }
  function askRates2() {
    if (ratesInFlight2) return;
    ratesInFlight2 = true;
    chat.server.askRates2(1200, dataViewModel.firstDate2().toISOString(), dataViewModel.lastDate2().toISOString(), pair)
      .done(function (response) {
        dataViewModel.updateChart2(response);
      })
      .always(function () { ratesInFlight2 = false; });
  }
  // #endregion

  // #region dataViewModel
  var dataViewModel = new DataViewModel();
  function DataViewModel() {
    var self = this;

    // #region Locals
    function lineChartDataEmpty() {
      return [{ d: new Date("1/1/1900"), c: 0, v: 0, m: 0 }];// jshint ignore:line
    }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    var lineChartData2 = ko.observableArray(lineChartDataEmpty());
    // #region Server proxies
    // #region pending request messages
    var pendingMessages = {};
    function clearPendingMessages(key) {// jshint ignore:line
      var notes = (pendingMessages[key] || []);
      while (notes.length)
        notifyClose(notes.pop());
    }
    function addPendingError(key, message, settings) {
      return addPendingMessage(key, message, $.extend({ type: NOTE_ERROR }, settings));
    }
    function addPendingMessage(key, message, settings) {
      pendingMessages[key] = pendingMessages[key] || [];
      var note = showInfoPerm(message, $.extend({
        hide: false,
        icon: 'fa fa-spinner fa-spin',
      }, settings));
      pendingMessages[key].push(note);
      return note;
    }
    // #endregion
    function serverCall(name, args, done) {
      var note = addPendingMessage(name, name + " is in progress ...");
      var r = chat.server[name].apply(chat.server, args)
        .always(function () {
          //clearPendingMessages(name);
        }).fail(function (error) {
          notifyClose(note);
          addPendingError(name, error + "", { title: name, icon: true });
        }).done(function () {
          resetPlotter();
          note.update({
            type: "success",
            text: "Done",
            icon: 'picon picon-task-complete',
            hide: true,
            delay: 1000
          });
        })
      ;
      if (done) r.done(function (data) { done(data, note); });
    }
    function setTradeLevelActive(levelIndex) {
      serverCall("startTrades", [pair, levelIndex === 0], resetPlotter);
    }
    this.flipTradeLevels = function () {
      serverCall("flipTradeLevels", [pair], resetPlotter);
    };
    this.setTradeLevelsFlip = function (l) {
      serverCall("setPresetTradeLevels", [pair, l, null], function () {
        serverCall("flipTradeLevels", [pair], resetPlotter);
      });
    }
    this.setTradeLevels = function (l, isBuy) {
      serverCall("setPresetTradeLevels", [pair, l, isBuy === undefined ? null : isBuy], resetPlotter);
    };
    function toggleIsActive(chartNum/*date, event*/) {
      serverCall("toggleIsActive", [pair, chartNum]);
    }
    function toggleStartDate(chartNum) {
      serverCall("toggleStartDate", [pair, chartNum]);
    }
    function setCorridorStartDate(chartNumber, index) {
      serverCall("setCorridorStartDateToNextWave", [pair, chartNumber, index === 1]);
    }
    function setTradeCount(tc) {
      chat.server.setTradeCount(pair, tc);
    }
    function setTradeLevel(isBuy, data) {
      serverCall("setTradeLevel", [pair, isBuy, data.value], resetPlotter);
    }
    function setTradeCloseLevel(isBuy, data) {
      serverCall("setTradeCloseLevel", [pair, isBuy, data.value], resetPlotter);
    }
    function moveCorridorWavesCount(chartIndex,step) {
      serverCall("moveCorridorWavesCount", [pair, chartIndex, step * 0.1], function (priceCma, note) {
        note.update({
          type: "success",
          text: "Done: " + priceCma,
          icon: 'picon picon-task-complete',
          hide: true,
          delay: 1000
        });
        resetPlotter();
      });
    }
    function saveTradeSettings() {
      var ts = settingsGrid().jqPropertyGrid('get');
      settingsGrid().empty();
      serverCall("saveTradeSettings", [pair, ts], resetPlotter);
    }
    function readTradeSettings() {
      settingsGrid().empty();
      serverCall("readTradeSettings", [pair], function (ts) {
        var tsMeta = {
          CorridorCrossesMaximum: {
            type: 'number',
            options: { step: 1, numberFormat: "n" }
          },
          TakeProfitBSRatio: { type: 'number', options: { step: 0.1, numberFormat: "n" } }
        };
        settingsGrid().jqPropertyGrid(ts, tsMeta);
      });
    }
    // #endregion
    // #endregion

    // #region Public
    // #region Server enums
    this.tradeLevelBys = ko.observableArray([]);
    this.setTradeLevelBuy = setTradeLevel.bind(null,true);
    this.setTradeLevelSell = setTradeLevel.bind(null, false);
    this.setTradeCloseLevelBuy = setTradeCloseLevel.bind(null, true);
    this.setTradeCloseLevelSell = setTradeCloseLevel.bind(null, false);
    this.moveCorridorWavesCount = moveCorridorWavesCount;
    // #endregion
    // #region Trade Settings
    this.saveTradeSettings = saveTradeSettings;
    this.setTradeCount = setTradeCount;
    this.toggleIsActive = toggleIsActive;
    this.readTradeSettings = readTradeSettings;
    // #endregion
    // #region News
    this.readNews = function () {
      serverCall("readNews", [], function (news) {
        self.news(setJsonDate(news, "Time"));
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
    this.chartArea = [{}, {}];
    this.chartData = ko.observable(defaultChartData(0));
    this.chartData2 = ko.observable(defaultChartData(1));
    var priceEmpty = { ask: NaN, bid: NaN };
    this.price = ko.observable(priceEmpty);
    // #region updateChart(2)
    this.updateChart = updateChart;
    this.updateChart2 = updateChart2;
    var commonChartParts = {};
    function updateChart(response) {
      setTrendDates(response.trendLines, response.trendLines2);
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
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.trendLines2, response.trendLines1, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true, 0, response.hasStartDate, response.cmaPeriod));
    }
    function updateChart2(response) {
      if (!commonChartParts.tradeLevels) return;
      if (!response.rates) {
        self.chartData2(chartDataFactory(null, response.trendLines, response.trendLines2, response.trendLines1, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod));
        return;
      }
      setTrendDates(response.trendLines, response.trendLines2);
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
      self.chartData2(chartDataFactory(lineChartData2, response.trendLines, response.trendLines2, response.trendLines1, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod));
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
    function chartDataFactory(data, trendLines, trendLines2, trendLines1, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData, chartNum, hasStartDate, cmaPeriod) {
      setTrendDates(trendLines, trendLines2, trendLines1);
      return {
        data: data ? data() : [],
        trendLines: trendLines,
        trendLines2: trendLines2,
        trendLines1: trendLines1,
        tradeLevels: tradeLevels,
        askBid: askBid || {},
        trades: trades || [],
        isTradingActive: isTradingActive || false,

        setTradeLevelActive: setTradeLevelActive,
        setCorridorStartDate: setCorridorStartDate,
        toggleIsActive: toggleIsActive,
        toggleStartDate: toggleStartDate,
        moveCorridorWavesCount: moveCorridorWavesCount,

        shouldUpdateData: shouldUpdateData,
        chartNum: chartNum,
        hasStartDate: hasStartDate,
        cmaPeriod: cmaPeriod
      };
    }
    function continuoseDates(data, dates) {// jshint ignore:line
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
    function defaultChartData(chartNum) { return chartDataFactory(lineChartData, { dates: [] }, {}, {}, null, null, null, false, false, chartNum, false, 0); }
    // #endregion
  }
  // #endregion

  // #region Init SignalR hub
  var host = location.host.match(/localhost/i) ? "ruleover.com:91" : location.host;
  var hubUrl = location.protocol + "//" + host + "/signalr/hubs";
  document.title = document.title + ":" + location.port;
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

      dataViewModel.price(response.price);
      delete response.price;

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
    // #region Start the connection.
    //$.connection.hub.logging = true;
    $.connection.hub.start().done(function () {

      //#region Load static data
      chat.server.readTradeLevelBys().done(function (levels) {
        dataViewModel.tradeLevelBys.push.apply(dataViewModel.tradeLevelBys, $.map(levels, function (v, n) { return { text: n, value: v }; }));
      });
      //dataViewModel.readNews();
      $.when.apply($, []).done(function () {
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
  /* #region keep it for future
  function notify_growl(message, settings) {
    return $.notify(message, $.extend({
      element: $("body").eq(0),
      delay: 2000,
      placement: {
        from: "bottom",
        align: "right"
      }
    }, settings));
  }
  #endregion */
  function notifyClose(note) {
    if (note.remove) note.remove();
  }
  //var stack_topleft = { "dir1": "down", "dir2": "right", "push": "top" };
  //var stack_custom = { "dir1": "right", "dir2": "down" };
  //var stack_custom2 = { "dir1": "left", "dir2": "up", "push": "top" };
  //var stack_bar_top = { "dir1": "down", "dir2": "right", "push": "top", "spacing1": 0, "spacing2": 0 };
  //var stack_bar_bottom = { "dir1": "up", "dir2": "right", "spacing1": 0, "spacing2": 0 };
  var stack_bottomleft = { "dir1": "up", "dir2": "right", "push": "top", "spacing1": 10, "spacing2": 10, firstpos1: 10, firstpos2: 10 };
  function notify(message, type, settings) {
    return new PNotify($.extend(
      $.isPlainObject(message) ? message : { text: message }
      , {
        type: type,
        delay: 3000,
        icon: true,
        addclass: "stack-bottomright",
        stack: stack_bottomleft,
        hide: true
      }
      , settings));
  }
  function showInfoPerm(message, settings) {
    return showInfo(message, $.extend({ delay: 0 }, settings));
  }
  function showInfo(message, settings) {
    return notify(message,"info", settings);
  }
  function showSuccess(message, settings) {// jshint ignore:line
    return notify(message, "success", $.extend({ type: "success" }, settings));
  }
  function showError(message, settings) {
    return notify(message, NOTE_ERROR, settings);
  }
  /* jshint ignore:end */
  function showErrorPerm(message,settings) {
    return showError(message, $.extend({ delay: 0,hide:false }, settings));
  }
  /** 
    * @param {TrendLines[]} tls - TrendLines object
    */
  function setTrendDates() {
    $.makeArray(arguments)
    .forEach(function (tls) {
      if (tls) tls.dates = (tls.dates || []).map(function (d) { return new Date(d); });
    });
  }
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