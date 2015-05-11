// jscs:disable
/// <reference path="../scripts/traverse.js" />
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
/**
 * Read - read data from server
 * Ask - ask data from server and forget. Server will fire "sendXXX" method related to askXXX
 * Send - method fired from server to sen info to clien
 */
(function () {
  // #region Globals
  "use strict";
  // enable global JSON date parsing
  //JSON.useDateParser();
  var chat;
  var pair = "usdjpy";
  var NOTE_ERROR = "error";
  function settingsGrid() { return $("#settingsGrid"); }
  $(function () {
    $("#settingsDialog").on("click", ".pgGroupRow", function () {
      $(this).nextUntil(".pgGroupRow").toggle();
    });
  });
  // #endregion

  // #region Reset plotter
  var resetPlotter = _.throttle(resetPlotterImpl, 250);
  var ratesInFlight = false;
  var ratesInFlight2 = false;
  function resetPlotterImpl() {
    askRates();
    askRates2();
  }
  function isInFlight(date) {
    return date && getSecondsBetween(new Date(), date) < 1;
  }
  function askRates() {
    if (isInFlight(ratesInFlight))
      return;
    ratesInFlight = new Date();
    chat.server.askRates(1200, dataViewModel.firstDate().toISOString(), dataViewModel.lastDate().toISOString(), pair)
      .done(function (response) {
        if (response)
          dataViewModel.updateChart(response);
      })
      .always(function () { ratesInFlight = false; });
  }
  function askRates2() {
    if (isInFlight(ratesInFlight2))
      return;
    ratesInFlight2 = new Date();
    chat.server.askRates2(1200, dataViewModel.firstDate2().toISOString(), dataViewModel.lastDate2().toISOString(), pair)
      .done(function (response) {
        dataViewModel.updateChart2(response);
      })
      .always(function () { ratesInFlight2 = false; });
  }
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
        var isCustom = typeof done === 'string';
        var msg = isCustom ? "\n" + done : "";
        resetPlotter();
        note.update({
          type: "warning",
          text: name + " is done" + msg,
          icon: 'picon picon-task-complete',
          hide: true,
          delay: isCustom ? 3000 : 1000
        });
      })
    ;
    if (done) r.done(function (data) { done(data, note); });
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
    function wrapTradeInCorridor() {
      serverCall("wrapTradeInCorridor", [pair], "Close levels were reset to None.");
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
    };
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
      serverCall("moveCorridorWavesCount", [pair, chartIndex, step], function (priceCma, note) {
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
    /**
     * @param {Object} ts
     */
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
          TradingAngleRange_: { type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TakeProfitXRatio: { type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TradingDistanceX: { type: 'number', options: { step: 0.1, numberFormat: "n" } },
          PriceCmaLevels_: { type: 'number', options: { step: 0.1, numberFormat: "n" } }
        };
        var properties = {}, meta = {};
        $.map(ts, function (v, n) {
          properties[n] = v.v;
          meta[n] = { group: v.g };
        });
        settingsGrid().jqPropertyGrid(properties, $.extend(true, tsMeta, meta));
      });
    }
    function readTradingConditions() {
      serverCall("readTradingConditions", [pair], function (tcs) {
        self.tradeConditions($.map(tcs, function (tc) {
          return { name: tc, checked: ko.observable(false) };
        }));
      });
    }
    // #endregion
    // #endregion

    // #region Public
    // #region Server enums
    this.refreshOrders = function () { serverCall("refreshOrders", []); };
    this.tradeLevelBys = ko.observableArray([]);
    this.setTradeLevelBuy = setTradeLevel.bind(null,true);
    this.setTradeLevelSell = setTradeLevel.bind(null, false);
    this.setTradeCloseLevelBuy = setTradeCloseLevel.bind(null, true);
    this.setTradeCloseLevelSell = setTradeCloseLevel.bind(null, false);
    this.wrapTradeInCorridor = wrapTradeInCorridor;
    this.wrapCurrentPriceInCorridor = function () { serverCall("wrapCurrentPriceInCorridor", [pair]); };
    this.moveCorridorWavesCount = moveCorridorWavesCount;
    this.tradeConditions = ko.observableArray([]);
    var closedTrades = [];
    var mustShowClosedTrades2 = ko.observable(false);
    this.showClosedTrades2Text = ko.pureComputed(function () { return mustShowClosedTrades2() ? "ON" : "OFF"; });
    this.toggleClosedTrades2 = function () {
      mustShowClosedTrades2(!mustShowClosedTrades2());
      askRates2();
    };
    this.toggleClosedTrades = function () {
      if (closedTrades.length) {
        closedTrades = [];
        resetPlotter();
      } else readClosedTrades();
    }
    this.readClosedTrades = readClosedTrades;
    function readClosedTrades(d, e, force) {
      serverCall("readClosedTrades", [pair], function (trades) {
        closedTrades = prepDates(trades).map(function (t) {
          return {
            timeOpen: t.Time,
            timeClose: t.TimeClose,
            isBuy: t.IsBuy,
            open: t.Open,
            close: t.Close,
            grossPL: t.GrossPL,
            kind: t.KindString,
            isClosed: t.KindString === "Closed"
          };
        });
      });
    }
    // #endregion
    // #region Trade Settings
    this.saveTradeSettings = saveTradeSettings;
    this.resetCloseLevels = function () {
      self.setTradeCloseLevelBuy({ value: 0 });
      self.setTradeCloseLevelSell({ value: 0 });
    };
    this.setTradeCount = setTradeCount;
    this.toggleIsActive = toggleIsActive;
    this.readTradeSettings = readTradeSettings;

    this.tradingConditionsReady = ko.observable(false);
    this.readTradingConditions = readTradingConditions;
    this.saveTradeConditions = function () {
      var tcs = self.tradeConditions()
        .filter(function (tc) {
          return tc.checked();
        }).map(function (tc) {
          return tc.name;
        });
      serverCall("setTradingConditions", [pair, tcs]);
    };
    this.getTradingConditions = function () {
      self.tradingConditionsReady(false);
      function hasName(name) { return function (name2) { return name === name2; }; }
      serverCall("getTradingConditions", [pair], function (tcs) {
        self.tradeConditions().forEach(function (tc) {
          tc.checked(tcs.filter(hasName(tc.name)).length > 0);
        });
        self.tradingConditionsReady(true);
      });
    };
    // #endregion
    // #region News
    this.readNews = function () {
      serverCall("readNews", [], function (news) {
        self.news(prepDates(news));
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
    // #region Info bar
    this.profit = ko.observable(0);
    this.openTradeGross = ko.observable(0);
    var tradeConditionsInfos = ko.observableArray() ;
    this.syncTradeConditionInfos = function (tci) {
      var tcid = toKoDictionary(tci);
      while (tradeConditionsInfos().length > tcid.length)
        tradeConditionsInfos.pop();
      var i = 0, l = tradeConditionsInfos().length;
      for (; i < l; i++) {
        tradeConditionsInfos()[i].key(tcid[i].key());
        tradeConditionsInfos()[i].value(tcid[i].value());
      }
      for (; i < tcid.length; i++)
        tradeConditionsInfos.push(tcid[i]);
    };
    this.tradeConditionInfos = tradeConditionsInfos;
    // #endregion
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
    var prepResponse = prepDates.bind(null, ["rates", "rates2"]);
    function updateChart(response) {
      prepResponse(response);
      if (response.rates.length === 0) return;
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(d.d);
      });
      var rates2 = response.rates2;
      rates2.forEach(function (d) {
        d.d = new Date(d.d);
      });
      var endDate = rates[0].d;
      var startDate = new Date(response.dateStart);
      lineChartData.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData.push.apply(lineChartData, rates);
      lineChartData.unshift.apply(lineChartData, rates2);
      //lineChartData.sort(function (a, b) { return a.d < b.d ? -1 : 1; });

      commonChartParts = { tradeLevels: response.tradeLevels, askBid: response.askBid, trades: response.trades };
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.trendLines2, response.trendLines1, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true, 0, response.hasStartDate, response.cmaPeriod, closedTrades, self.openTradeGross));
    }
    function updateChart2(response) {
      prepResponse(response);
      if (!commonChartParts.tradeLevels) return;
      if (!response.rates) {
        self.chartData2(chartDataFactory(null, response.trendLines, response.trendLines2, response.trendLines1, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod, closedTrades, self.openTradeGross));
        return;
      }
      if (response.rates.length === 0) return;
      var rates = response.rates;
      rates.forEach(function (d) {
        d.d = new Date(d.d);
      });
      rates = continuoseDates(rates, [response.trendLines.dates,response.trendLines1.dates,response.trendLines2.dates]);
      var x1 = _.last(lineChartData2());
      var x2 = _.last(rates);
      var shouldUpdateData = x1.d.valueOf() !== x2.d.valueOf() && x1.c !== x2.c;

      //if (shouldUpdateData)
      var endDate = rates[0].d;
      var startDate = response.dateStart;
      lineChartData2.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData2.push.apply(lineChartData2, rates);
      self.chartData2(chartDataFactory(lineChartData2, response.trendLines, response.trendLines2, response.trendLines1, commonChartParts.tradeLevels, commonChartParts.askBid, commonChartParts.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod, mustShowClosedTrades2() ? closedTrades : [], self.openTradeGross));
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
    function prepDates(blocked, root) {
      if (arguments.length == 1) {
        root = blocked;
        blocked = [];
      }
      var reISO = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2}(?:\.{0,1}\d*))(?:Z|(\+|-)([\d|:]*))?$/;
      traverse(root).forEach(function (x) {
        if (blocked.indexOf(this.key) >= 0) {
          this.block();
        } else if (reISO.exec(x))
          this.update(new Date(x));
      });
      return root;
    }
    function chartDataFactory(data, trendLines, trendLines2, trendLines1, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData, chartNum, hasStartDate, cmaPeriod, closedTrades, openTradeGross) {
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
        cmaPeriod: cmaPeriod,
        closedTrades: closedTrades,
        openTradeGross: openTradeGross
      };
    }
    function continuoseDates(data, dates) {// jshint ignore:line
      var ds = dates.map(function (ds) { return { dates2: [], dates: ds.reverse() }; });
      data.reverse().reduce(function (prevValue, current) {
        var cd = current.d;
        if (prevValue) {
          current.d = prevValue = dateAdd(prevValue, "minute", -1);
        } else prevValue = current.d;
        ds.forEach(function (d0) {
          if (d0.dates.length > 0)
            d0.dates.forEach(function (d) {
              if (d.valueOf() >= cd.valueOf()) {
                removeItem(d0.dates, d);
                d0.dates2.push(prevValue);
              }
            });
        });
        return prevValue;
      }, 0);
      ds.forEach(function (d0) {
        Array.prototype.push.apply(d0.dates, d0.dates2.reverse());
      });
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

    // #region Disconnect/Reconnect
    var errorNotes = [];
    function closeDisconnectNote() { closeErrorNote("TimeoutException"); }
    function closeReconnectNote() { closeErrorNote("reconnect"); }
    function openReconnectNote(note) { openErrorNote("reconnect", note); }
    function openErrorNote(key,note) {
      closeErrorNote(key);
      errorNotes.push([key, note]);
    }
    function closeErrorNote(key) {
      errorNotes.filter(function (a) { return a[0] === key; }).forEach(function (note) {
        notifyClose(note[1]);
        errorNotes.splice($.inArray(note, errorNotes), 1);
      });
    }
    $.connection.hub.error(function (error) {
      var key = typeof error.source === 'string' ? error.source : error.message;
      openErrorNote(key, showErrorPerm(error));
    });
    $.connection.hub.start(function () {
      closeReconnectNote();
    });
    $.connection.hub.disconnected(function () {
      closeDisconnectNote();
      openReconnectNote(showInfoPerm("Reconnecting ..."));
      setTimeout(function () {
        $.connection.hub.start();
      }, 5000); // Restart connection after 5 seconds.
    });
    // #endregion
    // Declare a proxy to reference the hub.
    chat = $.connection.myHub;
    // #region Create functions that the hub can call to broadcast messages.
    function addMessage(response) {
      if (isDocHidden()) return;

      if (!$('#rsdMin').is(":focus")) $('#rsdMin').val(response.rsdMin);
      delete response.rsdMin;

      dataViewModel.profit(response.prf);
      delete response.prf;

      dataViewModel.openTradeGross(response.otg);
      delete response.otg;

      dataViewModel.price(response.price);
      delete response.price;

      dataViewModel.syncTradeConditionInfos(response.tci);
      delete response.tci;

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
    chat.client.tradesChanged = dataViewModel.readClosedTrades;
    var sendChartHandlers = [dataViewModel.updateChart.bind(dataViewModel), dataViewModel.updateChart2.bind(dataViewModel)];
    chat.client.sendChart = function (chartPair, chartNum, chartData) {
      if (chartPair.toLowerCase() === pair.toLowerCase())
        sendChartHandlers[chartNum](chartData);
    }
    // #endregion
    // #region Start the connection.
    //$.connection.hub.logging = true;
    $.connection.hub.start().done(function (a) {
      try{
        showInfo(JSON.parse(a.data)[0].name + " started");
      } catch (e) {
        showErrorPerm("Unexpected start data:\n" + JSON.stringify(a.data) + "\nNeed refresh");
        return;
      }
      //#region Load static data
      var defTL = serverCall("readTradeLevelBys", [], function (levels) {
        dataViewModel.tradeLevelBys.push.apply(dataViewModel.tradeLevelBys, $.map(levels, function (v, n) { return { text: n, value: v }; }));
      });
      var defTC = dataViewModel.readTradingConditions();
      //dataViewModel.readNews();
      $.when.apply($, [defTL,defTC]).done(function () {
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
      function moveTradeLeve(isBuy, pips) { serverCall("moveTradeLevel", [pair, isBuy, pips]); }
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
      $('#manualToggle').click(function () { serverCall("manualToggle", [pair]); });
      $('#sell').click(function () { serverCall("sell", [pair]); });
      $('#buy').click(function () { serverCall("buy", [pair]); });
      $('#flipTradeLevels').click(function () { serverCall("flipTradeLevels",[pair]); });
      $('#rsdMin').change(function () {
        chat.server.setRsdTreshold(pair, $('#rsdMin').val());
        resetPlotter();
      });
      // #endregion
    });
    // #endregion
    //setInterval(updateLineChartData, 10);
  }
  // #endregion

  // #region Helpers
  function toKoDictionary(o) {
    return toDictionary(o, toKo, toKo);
    function toKo(k) { return ko.observable(k); }
  }
  function toDictionary(o, keyMap, valueMap) {
    return $.map(o, function (v, n) { return { key: keyMap ? keyMap(n) : n, value: valueMap ? valueMap(v) : v }; });
  }
  function getSecondsBetween(startDate, endDate) {
    return (startDate - endDate) / 1000;
  }
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
    if ((note || {}).remove) note.remove();
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
    return showInfo(message, $.extend({ delay: 0, hide: false }, settings));
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