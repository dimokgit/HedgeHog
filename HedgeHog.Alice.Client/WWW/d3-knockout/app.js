/// <reference path="../Scripts/linq.js" />
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
/*global ko,_,PNotify,Enumerable,traverse*/

//var D3KD = this.D3KD || {};
/**
 * Read - read data from server
 * Ask - ask data from server and forget. Server will fire "sendXXX" method related to askXXX
 * Send - method fired from server to sen info to clien
 */
(function () {
  //#region ko binding
  ko.bindingHandlers.elementer = {
    init: function (element, valueAccessor/*, allBindings, viewModel, bindingContext*/) {
      valueAccessor()(element);
    }
  };
  //#endregion
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
  var resetPlotterThrottleTime = 0.5 * 1000;
  var resetPlotterThrottleTime2 = 1 * 1000;
  var resetPlotter = _.throttle(askRates, resetPlotterThrottleTime);
  var resetPlotter2 = _.throttle(askRates2, resetPlotterThrottleTime2);
  var dateMin = new Date("1/1/9999");
  var ratesInFlight = dateMin;
  var ratesInFlight2 = dateMin;
  function isInFlight(date, index) {
    var secsInFlight = getSecondsBetween(new Date(), date);
    if (secsInFlight > 30) openErrorNote("InFlightDelay", showErrorPerm("In flight(" + index + ") > " + secsInFlight));
    if (secsInFlight > 60) return false;
    return date && secsInFlight > 0;
  }
  function askRates() {
    if (isInFlight(ratesInFlight,0))
      return;
    ratesInFlight = new Date();
    chat.server.askRates(1200, dataViewModel.firstDate().toISOString(), dataViewModel.lastDate().toISOString(), pair, 0)
      .done(function (response) {
        Enumerable.from(response)
        .forEach(function (r) {
          dataViewModel.updateChart(r);
        });
        resetPlotter2();
      })
      .fail(function (e) {
        showErrorPerm(e);
      }).always(function () {
        ratesInFlight = dateMin;
      });
  }
  function askRates2() {
    if (isInFlight(ratesInFlight2,2))
      return;
    ratesInFlight2 = new Date();
    chat.server.askRates(1200, dataViewModel.firstDate2().toISOString(), dataViewModel.lastDate2().toISOString(), pair, 1)
      .done(function (response) {
        Enumerable.from(response)
        .forEach(function (r) {
          dataViewModel.updateChart2(r);
        });
      }).fail(function (e) {
        showErrorPerm(e);
      }).always(function () {
        ratesInFlight2 = dateMin;
      });
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
  function serverCall(name, args, done,fail) {
    var method = chat.server[name];
    if (!method) {
      showErrorPerm("Server method " + name + " not found.");
      var p = $.Deferred();
      p.rejectWith("Server method " + name + " not found.");
      return p;
    }
    var noNote = args.noNote;
    var note = noNote ? { update: $.noop } : addPendingMessage(name, name + " is in progress ...");
    var r = chat.server[name].apply(chat.server, args)
      .always(function () {
        //clearPendingMessages(name);
      }).fail(function (error) {
        notifyClose(note);
        if (fail) fail(error);
        else addPendingError(name, error + "", { title: name, icon: true });
      }).done(function () {
        var isCustom = typeof done === 'string';
        var msg = isCustom ? "\n" + done : "";
        resetPlotter();
        note.update({
          type: "warning",
          text: name + " is done" + msg,
          icon: 'picon picon-task-complete',
          hide: true,
          delay: isCustom ? 5000 : 1000
        });
      })
    ;
    if ($.isFunction(done)) r.done(function (data) {
      done(data, note);
    });
  }
  // #endregion

  // #region dataViewModel
  var dataViewModel = new DataViewModel();
  function DataViewModel() {
    var self = this;

    // #region Locals
    function lineChartDataEmpty() {
      return [{ d: new Date("1/1/1900"),do: new Date("1/1/1900"), c: 0, v: 0, m: 0 }];// jshint ignore:line
    }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    var lineChartData2 = ko.observableArray(lineChartDataEmpty());
    // #region Server proxies
    function wrapTradeInCorridor() {
      serverCall("wrapTradeInCorridor", [pair], "<b>Close levels were reset to None.</b>");
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
    this.resetTradeLevels = function () { this.setTradeLevels(0); }.bind(this);
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
    // #region TradeLevel
    function setTradeLevel(isBuy, data) {
      serverCall("setTradeLevel", [pair, isBuy, parseTradeLevelBy(data.value)], resetPlotter);
    }
    function setTradeCloseLevel(isBuy, data) {
      serverCall("setTradeCloseLevel", [pair, isBuy, parseTradeLevelBy(data.value)], resetPlotter);
    }
    var tradeLevelBysRaw = ko.observable();
    function parseTradeLevelBy(levelName) {
      var l = parseInt(levelName);
      if (!isNaN(l)) return l;
      l = tradeLevelBysRaw()[levelName];
      if (l === undefined) {
        var error = "TradeLevelBy:" + levelName + " does not exist.";
        showErrorPerm(error);
        throw error;
      }
      return l;
    }
    // #endregion
    function moveCorridorWavesCount(chartIndex, step) {
      if (chartIndex !== 0) return alert("chartIndex:" + chartIndex + " is not supported");
      var name = "PriceCmaLevels_";
      readTradeSettings(chartIndex,function (ts) {
        var value = Math.round((ts[name].v + step / 10) * 10) / 10;
        saveTradeSetting(chartIndex,name, value, function (ts,note) {
          var pcl = (ts||{})[name].v;
          note.update({
            type: "success",
            text: "Set " + name + " to " + pcl,
            icon: 'picon picon-task-complete',
            hide: true,
            delay: 1000
          });
        });
      });
    }
    function moveCorridorWavesCount_(chartIndex, step) {
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
    // #region TradeSettings
    /**
     * @param {Object} ts
     */
    function saveTradeSetting(chartNum, name, value,done) {
      var ts = {};
      ts[name] = value;
      serverCall("saveTradeSettings", [pair,chartNum, ts], done);
    }
    function saveTradeSettings(chartNum) {
      var ts = settingsGrid().jqPropertyGrid('get');
      settingsGrid().empty();
      serverCall("saveTradeSettings", [pair,chartNum, ts]);
    }
    function readTradeSettings(chartNum, done) {
      if (arguments.length !== 2) return alert("readTradeSettings must have two arguments");
      serverCall("readTradeSettings", [pair, chartNum], done);
    }
    function loadTradeSettings(chartNum) {
      if (arguments.length < 1) return alert("loadTradeSettings must have at least one argument");
      tradeSettingsCurrent(chartNum);
      settingsGrid().empty();
      readTradeSettings(chartNum, function (ts) {
        var tsMeta = {
          TradeCountMax: {
            type: 'number',
            options: { step: 1, numberFormat: "n" }
          },
          DoAdjustExitLevelByTradeTime: { name: "Adjust Exit By Trade" },
          MoveWrapTradeWithNewTrade:{name:"ForceWrapTrade"},
          TradingRatioByPMC: { name: "Lot By PMC" },
          LimitProfitByRatesHeight: { name: "Limit Profit By Height" },
          FreezeCorridorOnTradeOpen: { name: "Freeze On TradeOpen" },
          TradingAngleRange_: { name: "Trading Angle", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TakeProfitXRatio: { name: "Take ProfitX", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TradingDistanceX: { name: "Trading DistanceX", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          PriceCmaLevels_: { name: "PriceCmaLevels", type: 'number', options: { step: 0.01, numberFormat: "n" } },
          CorridorLengthDiff: { name: "CorridorLengthDiff", type: 'number', options: { step: 0.01, numberFormat: "n" } },
          
          TradeDirection: {
            type: "options", options: [
              { text: "None", value: "None" },
              { text: "Up", value: "Up" },
              { text: "Down", value: "Down" },
              { text: "Both", value: "Both" },
              { text: "Auto", value: "Auto" }]
          },
          TradingDistanceFunction: {
            name: "Trading Distance", type: "options", options: [
              { text: "BuySellLevels", value: "BuySellLevels" },
              { text: "RatesHeight", value: "RatesHeight" },
              { text: "Pips", value: "Pips" },
              { text: "Green", value: "Green" },
              { text: "Red", value: "Red" },
              { text: "Blue", value: "Blue" },
              { text: "Wave", value: "Wave" }
            ]
          },
          TakeProfitFunction: {
            name: "Take Profit", type: "options", options: [
              { text: "BuySellLevels", value: "BuySellLevels" },
              { text: "RatesHeight", value: "RatesHeight" },
              { text: "Pips", value: "Pips" },
              { text: "Green", value: "Green" },
              { text: "Red", value: "Red" },
              { text: "Blue", value: "Blue" },
              { text: "Wave", value: "Wave" }
            ]
          },
          WaveFirstSecondRatioMin: { name: "Wave 1/2 Ratio" }
        };
        var properties = {}, meta = {};
        $.map(ts, function (v, n) {
          properties[n] = v.v;
          meta[n] = { group: v.g };
        });
        settingsGrid().jqPropertyGrid(properties, $.extend(true, tsMeta, meta));
      });
    }
    // #endregion
    // #region TradingConditions
    function readTradingConditions() {
      return serverCall("readTradingConditions", [pair], function (tcs) {
        self.tradeConditions($.map(tcs, function (tc) {
          return { name: tc, checked: ko.observable(false) };
        }));
      });
    }
    // #endregion
    // #region TradeDirectionTriggers
    // #endregion
    function setRsdMin(chartNum, rsd) {
      serverCall("setRsdTreshold", [pair, chartNum, rsd]);
    }
    // #endregion
    // #endregion

    // #region Public
    // #region Server enums
    this.toggleStartDate = toggleStartDate.bind(null, 0);
    this.rsdMin = ko.observable();
    this.rsdMin.subscribe(function (rsd) { setRsdMin(0, rsd); });
    this.rsdMin2 = ko.observable();
    this.rsdMin2.subscribe(function (rsd) { setRsdMin(1, rsd); });
    this.refreshOrders = function () { serverCall("refreshOrders", []); };
    this.tradeLevelBysRaw = tradeLevelBysRaw;
    this.tradeLevelBys = ko.observableArray([]);
    this.setTradeLevelBuy = setTradeLevel.bind(null,true);
    this.setTradeLevelSell = setTradeLevel.bind(null, false);
    this.setTradeCloseLevelBuy = setTradeCloseLevel.bind(null, true);
    this.setTradeCloseLevelSell = setTradeCloseLevel.bind(null, false);
    this.wrapTradeInCorridor = wrapTradeInCorridor;
    this.wrapCurrentPriceInCorridor = function () { serverCall("wrapCurrentPriceInCorridor", [pair], "Close levels were reset"); };
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
    };
    this.readClosedTrades = readClosedTrades;
    function readClosedTrades() {
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
    this.saveTradeSettings = function () {
      saveTradeSettings(tradeSettingsCurrent());
    };
    this.setCloseLevelsToGreen = function () {
      self.setTradeCloseLevelBuy({ value: "PriceHigh0" });
      self.setTradeCloseLevelSell({ value: "PriceLow0" });
    };
    this.setCloseLevelsToGRB1 = function () {
      self.setTradeCloseLevelBuy({ value: "PriceMax1" });
      self.setTradeCloseLevelSell({ value: "PriceMin1" });
    };
    this.resetCloseLevels = function () {
      self.setTradeCloseLevelBuy({ value: 0 });
      self.setTradeCloseLevelSell({ value: 0 });
    };
    this.setTradeCount = setTradeCount;
    this.toggleIsActive = toggleIsActive;
    this.loadTradeSettings = loadTradeSettings.bind(null, 0);
    this.loadTradeSettings2 = loadTradeSettings.bind(null, 1);
    var tradeSettingsCurrent = this.tradeSettingsCurrent = ko.observable(0);

    this.tradeOpenActionsReady = ko.observable(false);

    // #region tradeConditions
    this.tradingConditionsReady = ko.observable(false);
    this.readTradingConditions = readTradingConditions;
    this.saveTradeConditions = saveChecked.bind(null,self.tradeConditions, "setTradingConditions");
    this.getTradingConditions = getChecked.bind(null,self.tradingConditionsReady, "getTradingConditions", self.tradeConditions) ;
    // #endregion
    function getChecked(isReadeObservable,serverMethod,checkedSubject) {
      isReadeObservable(false);
      function hasName(name) { return function (name2) { return name === name2; }; }
      serverCall(serverMethod, [pair], function (tcs) {
        checkedSubject().forEach(function (tc) {
          tc.checked(tcs.filter(hasName(tc.name)).length > 0);
        });
        isReadeObservable(true);
      });
    }
    function saveChecked (checkeds,serverMethod) {
      var tcs = checkeds()
        .filter(function (tc) {
          return tc.checked();
        }).map(function (tc) {
          return tc.name;
        });
      serverCall(serverMethod, [pair, tcs]);
    }

    // #region tradeDirectionTriggers
    this.tradeDirectionTriggers = ko.observableArray();
    this.tradeDirectionTriggersReady = ko.observable(false);
    this.readTradeDirectionTriggers = function readTradeDirectionTriggers() {
      return serverCall("readTradeDirectionTriggers", [pair], function (tcs) {
        self.tradeDirectionTriggers($.map(tcs, function (tc) {
          return { name: tc, checked: ko.observable(false) };
        }));
      });
    };
    this.saveTradeDirectionTriggers = saveChecked.bind(null, self.tradeDirectionTriggers, "setTradeDirectionTriggers");
    this.getTradeDirectionTriggers = getChecked.bind(null, self.tradeDirectionTriggersReady, "getTradeDirectionTriggers", self.tradeDirectionTriggers);
    // #endregion
    // #endregion
    // #region TradeOpenActions
    function readTradeOpenActions() {
      return serverCall("readTradeOpenActions", [pair], function (tcs) {
        self.tradeOpenActions($.map(tcs, function (tc) {
          return { name: tc, checked: ko.observable(false) };
        }));
      });
    }
    this.readTradeOpenActions = readTradeOpenActions;
    this.tradeOpenActions = ko.observableArray([]);
    this.getTradeOpenActions = function () {
      self.tradeOpenActionsReady(false);
      function hasName(name) { return function (name2) { return name === name2; }; }
      serverCall("getTradeOpenActions", [pair], function (tcs) {
        self.tradeOpenActions().forEach(function (tc) {
          tc.checked(tcs.filter(hasName(tc.name)).length > 0);
        });
        self.tradeOpenActionsReady(true);
      });
    };
    this.saveTradeOpenActions = function () {
      var tcs = self.tradeOpenActions()
        .filter(function (tc) {
          return tc.checked();
        }).map(function (tc) {
          return tc.name;
        });
      serverCall("setTradeOpenActions", [pair, tcs]);
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
    var tradeConditionsInfosAnd = ko.observableArray();
    var tradeConditionsInfosOr = ko.observableArray();
    this.syncTradeConditionInfos = function (tci) {
      sync(tradeConditionsInfosAnd, toKoDictionary(tci.And || {}));
      sync(tradeConditionsInfosOr, toKoDictionary(tci.Or || {}));
      function sync(tradeConditionsInfos,tcid) {
        while (tradeConditionsInfos().length > tcid.length)
          tradeConditionsInfos.pop();
        var i = 0, l = tradeConditionsInfos().length;
        for (; i < l; i++) {
          tradeConditionsInfos()[i].key(tcid[i].key());
          tradeConditionsInfos()[i].value(tcid[i].value());
        }
        for (; i < tcid.length; i++)
          tradeConditionsInfos.push(tcid[i]);
      }
    };
    this.tradeConditionInfosAnd = tradeConditionsInfosAnd;
    this.tradeConditionInfosOr = tradeConditionsInfosOr;
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
    var updateChartIntervalAverages = [ko.observable(), ko.observable()];
    var updateChartCmas = [ko.observable(), ko.observable()];
    this.stats = { ucia: updateChartIntervalAverages, ucCmas: updateChartCmas };
    function updateChart(response) {
      var d = new Date();
      updateChartIntervalAverages[0](cma(updateChartIntervalAverages[0](), 10, getSecondsBetween(new Date(), ratesInFlight)));
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
      response.waveLines.forEach(function (w, i) { w.bold = i == sumStartIndexById(); });
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.trendLines2, response.trendLines1, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true, 0, response.hasStartDate, response.cmaPeriod, closedTrades, self.openTradeGross,response.tpsAvg,response.canBuy,response.canSell,response.waveLines));
      updateChartCmas[0](cma(updateChartCmas[0](), 10, getSecondsBetween(new Date(), d)));
    }
    function updateChart2(response) {
      var d = new Date();
      updateChartIntervalAverages[1](cma(updateChartIntervalAverages[1](), 10, getSecondsBetween(new Date(), ratesInFlight2)));
      prepResponse(response);
      if (response.rates.length === 0) return;
      var rates = response.rates;
      var rates2 = response.rates2;
      if (rates.length + rates2.length === 0) return;
      rates.forEach(function (d) {
        d.d = d.do = new Date(d.d);
      });
      rates2.forEach(function (d) {
        d.d = d.do = new Date(d.d);
      });
      var endDate = rates[0].d;
      var startDate = new Date(response.dateStart);
      lineChartData2.remove(function (d) {
        return d.d >= endDate || d.d < startDate;
      });
      lineChartData2.push.apply(lineChartData2, rates);
      lineChartData2.unshift.apply(lineChartData2, rates2);
      var ratesAll = continuoseDates("minute",lineChartData2(), [response.trendLines.dates, response.trendLines1.dates, response.trendLines2.dates]);
      var shouldUpdateData = true;
      if (response.isTrader)
        commonChartParts.tradeLevels = response.tradeLevels;
      var chartData2 = chartDataFactory(ratesAll, response.trendLines, response.trendLines2, response.trendLines1, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod, mustShowClosedTrades2() ? closedTrades : [], self.openTradeGross,0, response.canBuy, response.canSell,response.waveLines);
      chartData2.tickDate = lineChartData()[0].d;
      response.waveLines.forEach(function (w, i) { w.bold = i == sumStartIndexById(); });
      self.chartData2(chartData2);
      updateChartCmas[1](cma(updateChartCmas[1](), 10, getSecondsBetween(new Date(), d)));
    }
    // #endregion
    // #region LastDate
    this.lastDate = lastDate;
    this.lastDate2 = lastDate2;
    function lastDateImpl(lineChartData) {
      var lastIndex = Math.max(1, lineChartData().length - 1);
      return (lineChartData()[lastIndex] || {}).d || dateMin;
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
    function firstDateImpl(lineChartData,key) {
      return (lineChartData()[0] || {})[key];
    }
    function firstDate() {
      return firstDateImpl(lineChartData,'d');
    }
    function firstDate2() {
      return firstDateImpl(lineChartData2, 'do');
    }
    // #endregion
    // #endregion
    // #region Read Enums
    // #endregion
    //#region WaveRanges
    var currentWareRangesChartNum = 0;
    function getWaveRanges() {
      var args = [pair,currentWareRangesChartNum];
      args.noNote = true;
      serverCall("getWaveRanges", args,
        function (wrs) {
          wrs.forEach(function (wr) {
          });
          waveRanges(wrs);
          if (stopWaveRanges)
            showInfo("getWaveRanges stopped");
          else
            setTimeout(getWaveRanges, 1000);
        },
        function (error) {
          showErrorPerm("getWaveRanges: " + error);
        });
    }
    var waveRangesDialog;
    var sumStartIndex = ko.observable(0);
    function waveRangeValue(prop,wr) { return wr[prop].v; }
    this.waveRangesDialog = function (element) {
      var table = $(element).find("table") ;
      waveRangesDialog = table[0];
      table.on("click", "tbody tr", function (a, b) {
        var koData = ko.dataFor(this);
        var uid = waveRangeValue("Angle", koData);
        sumStartIndex(uid == sumStartIndex() ? 0 : uid);
      });
    };
    var waveRanges = ko.observableArray();

    this.waveRanges = ko.pureComputed(function () {
      return waveRanges().filter(function (wr) {
        return !wr.IsStats;
      });
    });
    this.waveRangesStats = ko.pureComputed(function () {
      return waveRanges().filter(function (wr) {
        return !!wr.IsStats;
      });
    });
    this.sumStartIndex = sumStartIndex;
    this.dbrSum = ko.pureComputed(sumByIndex.bind(null, "DistanceByRegression"));
    this.heightSum = ko.pureComputed(sumByIndex.bind(null, "Height"));
    this.wbhSum = ko.pureComputed(sumByIndex.bind(null, "WorkByHeight"));
    this.distanceSum = ko.pureComputed(sumByIndex.bind(null, "Distance"));
    this.sumStartIndexById = ko.pureComputed(sumStartIndexById);
    function fuzzyFind(array, prop, value) {
      if (!array || !array.length) return null;
      var diffs = array.map(function (v) {
        return { v: v, d: Math.abs(prop(v) - value) };
      });
      var r = _.chain(diffs).sortBy('d').first().value();
      return r.v;
    }
    function sumStartIndexById() {
      var uid = sumStartIndex();
      var wr = fuzzyFind(waveRanges(), waveRangeValue.bind(null, "Angle"), uid);
      return waveRanges().indexOf(wr);
    }
    function sumByIndex (prop) {
      var i = sumStartIndexById();
      return i <= 0
        ? 0
        : Math.round(waveRanges().slice(0, i).reduce(function (a, b) {
          return a + waveRangeValue(prop, b);
        }, 0) - waveRangeValue(prop, waveRanges()[i]));
    }
    this.startWaveRanges = function (chartNum) {
      currentWareRangesChartNum = chartNum;
      stopWaveRanges = false;
      $(waveRangesDialog).dialog({
        title: "Wave Ranges",
        width: "auto",
        dialogClass:"dialog-compact",
        close: function () {
          self.stopWaveRanges();
          $(this).dialog("destroy");
        }
      });
      getWaveRanges();
    };
    var stopWaveRanges = false;
    this.stopWaveRanges = function () {
      stopWaveRanges = false;
    };

    //#endregion
    // #endregion

    // #region Helpers
    var signalRMap = {
      cmp: "cmaPeriod",
    };
    function prepDates(blocked, root) {
      if (arguments.length === 1) {
        root = blocked;
        blocked = [];
      }
      var reISO = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2}(?:\.{0,1}\d*))(?:Z|(\+|-)([\d|:]*))?$/;
      traverse(root).forEach(function (x) {
        if (blocked.indexOf(this.key) >= 0) {
          this.block();
          return;
        }
        if (reISO.exec(x))
          this.update(new Date(x));
        var mappedKey = signalRMap[this.key];
        if (mappedKey) {
          var n = this.node;
          this.delete();
          this.parent.node[mappedKey] = n;
        }
      });
      return root;
    }
    function chartDataFactory(data, trendLines, trendLines2, trendLines1, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData, chartNum, hasStartDate, cmaPeriod, closedTrades, openTradeGross, tpsAvg, canBuy, canSell, waveLines) {
      return {
        data: data ? ko.unwrap(data) : [],
        trendLines: trendLines,
        trendLines2: trendLines2,
        trendLines1: trendLines1,
        waveLines:waveLines,
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
        openTradeGross: openTradeGross,
        tpsAvg: tpsAvg,

        canBuy: canBuy,
        canSell:canSell
      };
    }
    function continuoseDates(interval, data, dates) {// jshint ignore:line
      var ds = dates.map(function (ds) { return { dates2: [], dates: ds.reverse() }; });
      data.reverse().reduce(function (prevValue, current) {
        var cd = current.d;
        current.d = prevValue = (prevValue ? dateAdd(prevValue, interval, -1) : current.d);
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
  //$.getScript(hubUrl, init);
  // Init SignaR client
  init();
  function init() {
    //Set the hubs URL for the connection
    //$.connection.hub.url = "http://" + host + "/signalr";

    // #region Disconnect/Reconnect
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

      dataViewModel.rsdMin(response.rsdMin);
      delete response.rsdMin;

      dataViewModel.rsdMin2(response.rsdMin2);
      delete response.rsdMin2;

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
    var priceChanged = (function () {
      var _inFlightPriceChanged = dateMin;
      return _priceChanged;

      function _isPriceChangeInFlight() {
        var secsInFlight = getSecondsBetween(new Date(), _inFlightPriceChanged);
        if (secsInFlight > 3) openErrorNote("InFlightPriceChaneDelay", showErrorPerm("PriceChange In flight > " + secsInFlight));
        if (secsInFlight > 6) return false;
        return _inFlightPriceChanged && secsInFlight > 0;
      }

      function _priceChanged(pairChanged) {
        if (!isDocHidden() && pair === pairChanged) {
          if (_isPriceChangeInFlight())
            return;
          _inFlightPriceChanged = new Date();
          chat.server.askChangedPrice(pair)
            .done(function (response) {
              addMessage(response);
            })
            .fail(function (e) {
              showErrorPerm(e);
            })
            .always(function () {
              _inFlightPriceChanged = dateMin;
            });
        }
      }
    })();
    chat.client.addMessage = addMessage;
    chat.client.priceChanged = priceChanged;
    chat.client.tradesChanged = dataViewModel.readClosedTrades;
    chat.client.marketIsOpening = function (market) {
      showInfoPerm(JSON.stringify(market));
    };
    chat.client.newsIsComming = function (news) {
      showWarningPerm(JSON.stringify(news));
    };
    chat.client.warning = function (message) {
      showWarningPerm(message);
    };
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
        dataViewModel.tradeLevelBysRaw(levels);
        dataViewModel.tradeLevelBys.push.apply(dataViewModel.tradeLevelBys, $.map(levels, function (v, n) { return { text: n, value: v }; }));
      });
      var defTDT = dataViewModel.readTradeDirectionTriggers();
      var defTC = dataViewModel.readTradingConditions();
      var defTOC = dataViewModel.readTradeOpenActions();
      //dataViewModel.readNews();
      $.when.apply($, [defTL,defTC,defTOC,defTDT]).done(function () {
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
      //$('#flipTradeLevels').click(function () { serverCall("flipTradeLevels",[pair]); });
      // #endregion
    });
    // #endregion
    //setInterval(updateLineChartData, 10);
  }
  // #endregion

  // #region Helpers
  // #region Note Reopener
  var errorNotes = [];
  function closeDisconnectNote() { closeErrorNote("TimeoutException"); }
  function closeReconnectNote() { closeErrorNote("reconnect"); }
  function openReconnectNote(note) { openErrorNote("reconnect", note); }
  function openErrorNote(key, note) {
    closeErrorNote(key);
    errorNotes.push([key, note]);
  }
  function closeErrorNote(key) {
    errorNotes.filter(function (a) { return a[0] === key; }).forEach(function (note) {
      notifyClose(note[1]);
      errorNotes.splice($.inArray(note, errorNotes), 1);
    });
  }
  // #endregion
  function toKoDictionary(o) {
    return toDictionary(o, toKo, toKo);
    function toKo(k) { return ko.observable(k); }
  }
  function toDictionary(o, keyMap, valueMap) {
    return $.map(o, function (v, n) { return { key: keyMap ? keyMap(n) : n, value: valueMap ? valueMap(v) : v }; });
  }
  function getSecondsBetween(nowDate, thenDate) {
    return (nowDate - thenDate) / 1000;
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
    return notify(message, "info", settings);
  }
  function showWarningPerm(message, settings) {
    return showWarning(message, $.extend({ delay: 0, hide: false }, settings));
  }
  function showWarning(message, settings) {
    return notify(message,"warning", settings);
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
  function cma(MA, Periods, NewValue) {
    if (MA === null || MA === undefined) {
      return NewValue;
    }
    return MA + (NewValue - MA) / (Periods + 1);
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