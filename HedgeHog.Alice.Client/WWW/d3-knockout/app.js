/// <reference path="../Scripts/linq.js" />
/// <reference path="../bower_components/d3/d3.js" />
// jscs:disable
/// <reference path="../scripts/traverse.js" />
/// <reference path="../Scripts/pnotify.custom.min.js" />
/// http://sciactive.github.io/pnotify/#demos-simple
/// <reference path="../Scripts/knockout-3.4.2.js" />
/// <reference path="../Scripts/knockout.mapping-latest.js" />
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

//Array.prototype.average = Array.prototype.average || function () {
//  return this.sum() / (this.length || 1);
//}
(function () {
  Array.prototype.sum = Array.prototype.sum || function () {
    return this.reduce(function (sum, a) { return sum + Number(a) }, 0);
  }
  function standardDeviation(values) {
    var avg = average(values);

    var squareDiffs = values.map(function (value) {
      var diff = value - avg;
      var sqrDiff = diff * diff;
      return sqrDiff;
    });

    var avgSquareDiff = average(squareDiffs);

    var stdDev = Math.sqrt(avgSquareDiff);
    return stdDev;
    function average(data) {
      var sum = data.reduce(function (sum, value) {
        return sum + value;
      }, 0);

      var avg = sum / data.length;
      return avg;
    }
  }

  var isMobile = /Mobi/.test(navigator.userAgent);
  var devVersion = "(v2)";
  //#region ko binding
  ko.bindingHandlers.elementer = {
    init: function (element, valueAccessor/*, allBindings, viewModel, bindingContext*/) {
      valueAccessor()(element);
    }
  };
  ko.bindingHandlers.draggable = {
    init: function (element, valueAccessor/*, allBindings, viewModel, bindingContext*/) {
      $(element).draggable({
        //handle: ".modal-header"
      });
    }
  };
  ko.bindingHandlers.toggler = {
    init: function (element, valueAccessor/*, allBindings, viewModel, bindingContext*/) {
      var value = valueAccessor();
      var selector = ko.unwrap(value);
      var togglee = $(element).find(selector);
      $(element).click(function () {
        if (togglee.is(":visible"))
          togglee.hide("slide", { direction: "up" }, 1000);
        else
          togglee.show("slide", { direction: "down" }, 1000);
      });
    }
  };
  //#endregion
  // #region Globals
  "use strict";
  if (!Array.prototype.find) {
    Object.defineProperty(Array.prototype, "find", {
      value: function (predicate) {
        if (this === null) {
          throw new TypeError('Array.prototype.find called on null or undefined');
        }
        if (typeof predicate !== 'function') {
          throw new TypeError('predicate must be a function');
        }
        var list = Object(this);
        var length = list.length >>> 0;
        var thisArg = arguments[1];
        var value;

        for (var i = 0; i < length; i++) {
          value = list[i];
          if (predicate.call(thisArg, value, i, list)) {
            return value;
          }
        }
        return undefined;
      }
    });
  }
  // enable global JSON date parsing
  //JSON.useDateParser();
  var chat;
  var pair = (getQueryVariable("pair") || "").toUpperCase();

  var NOTE_ERROR = "error";
  function settingsGrid() { return $("#settingsGrid"); }
  $(function () {
    $("#settingsDialog").on("click", ".pgGroupRow", function () {
      $(this).nextUntil(".pgGroupRow").toggle();
    });
  });
  // #endregion
  var scOptions = { 'disable_in_input': true };
  $(function (e) {
    shortcut.add("B", function () {
      serverCall("toggleCanTrade", withNoNote(pair, true), function (d) {
        d[0] ? showSuccess("Buy is on") : showError("Buy is off");
      });
    }, scOptions);
    shortcut.add("S", function () {
      serverCall("toggleCanTrade", withNoNote(pair, false), function (d) {
        d[0] ? showSuccess("Sell is on") : showError("Sell is off");
      });
    }, scOptions);
    shortcut.add("SHIFT+B", function () {
      serverCall("setCanTrade", withNoNote(pair, false, true), showError.bind(null, "Buy is off"));
    }, scOptions);
    shortcut.add("SHIFT+S", function () {
      serverCall("setCanTrade", withNoNote(pair, false, false), showError.bind(null, "Sell is off"));
    }, scOptions);
    shortcut.add("F", function () {
      serverCall("flipTradeLevels", withNoNote(pair), "Flipped");
    }, scOptions);
    shortcut.add("C", function () {
      dataViewModel.closeTrades();
    }, scOptions);
    shortcut.add("A", function () {
      dataViewModel.toggleIsActive(0);
    }, scOptions);
    shortcut.add("M", function () {
      dataViewModel.manualToggle();
    }, scOptions);
  });


  // #region Reset plotter
  var resetPlotterThrottleTime = 0.5 * 1000;
  var resetPlotterThrottleTime2 = 1 * 1000;
  var resetPlotter = _.throttle(askRates, resetPlotterThrottleTime);
  var resetPlotter2 = _.throttle(askRates2, resetPlotterThrottleTime2);
  var dateMin = new Date("1/1/9999");
  var ratesInFlight = dateMin;
  var ratesInFlight2 = dateMin;
  function keyNote(text) { return typeof text === "string" ? { keyNote: text } : text.keyNote; };
  var openInFlightNote = _.throttle(showError, 2 * 1000);
  var openInFlightNotePerm = _.throttle(showWarning, 2 * 1000);
  function isInFlight(date, index) {
    var secsInFlight = getSecondsBetween(new Date(), date);
    if (secsInFlight > 3)
      openInFlightNotePerm("In flight(" + index + ") > " + secsInFlight, keyNote("InFlightDelay"));
    if (secsInFlight > 6) return false;
    return date && secsInFlight > 0;
  }
  function isConnected() {
    return $.connection.hub.state === 1;
  }

  var askRateFirstDate, askRateLastDate;
  var askRateFirstDate2, askRateLastDate2;
  function askRatesDatesReset() {
    askRateFirstDate = new Date("1/1/1900");
  }
  function askRatesDatesReset2() {
    askRateFirstDate2 = new Date("1/1/1900");
  }
  var askRate2FirstDate, askRate2LastDate;
  function askRates() {
    if (!isConnected() || isInFlight(ratesInFlight, 0))
      return;
    ratesInFlight = new Date();
    chat.server.askRates(1200, (askRateFirstDate || dataViewModel.firstDate()).toISOString(), (askRateFirstDate || dataViewModel.lastDate()).toISOString(), pair, 't1')
      .done(function (response) {
        Enumerable.from(response)
          .forEach(function (r) {
            dataViewModel.updateChart(r);
          });
        resetPlotter2();
      })
      .fail(function (e) {
        showErrorPerm(e, keyNote("askRates"));
      }).always(function () {
        ratesInFlight = dateMin;
      });
    askRateFirstDate = null;
  }
  function askRates2() {
    if (!isConnected() || isInFlight(ratesInFlight2, 2))
      return;
    ratesInFlight2 = new Date();
    chat.server.askRates(1200, (askRateFirstDate2 || dataViewModel.firstDate2()).toISOString(), (askRateFirstDate2 || dateAdd(dataViewModel.lastDate2(), "minute", -5)).toISOString(), pair, 'M1')
      .done(function (response) {
        Enumerable.from(response)
          .forEach(function (r) {
            dataViewModel.updateChart2(r);
          });
      }).fail(function (e) {
        showErrorPerm(e, keyNote("askRates2"));
      }).always(function () {
        ratesInFlight2 = dateMin;
      });
    askRateFirstDate2 = null;
  }
  function askPriceChanged() {
    return chat.server.askChangedPrice(pair)
      .done(function (response) {
        addMessage(response);
      })
      .fail(function (e) {
        showErrorPerm(e, keyNote("askPriceChanged"));
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
  function withNoNote() {
    var args = _.toArray(arguments);
    args.noNote = true;
    return args;
  }
  var serverMethodsRefresh = [];
  function serverCall(name, args, done, fail, always) {
    var method = chat.server[name];
    if (!method) {
      showErrorPerm("Server method " + name + " not found.");
      var p = $.Deferred();
      p.rejectWith("Server method " + name + " not found.");
      return p;
    }
    var noNote = args.noNote;
    var note = noNote ? { update: $.noop } : addPendingMessage(name, name + " is in progress ...");
    try {
      var r = chat.server[name].apply(chat.server, args)
        .always(function () {
          (always || $.noop)();
        }).fail(function (error) {
          notifyClose(note);
          if (fail) fail(error);
          else addPendingError(name, error + "", { title: name, icon: true });
        }).done(function () {
          var isCustom = typeof done === 'string';
          var msg = isCustom ? "\n" + done : "";
          if (serverMethodsRefresh.some(function (s) { return toLowerCase(s) == toLowerCase(name); }))
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
      return r;
    } catch (e) {
      if (fail) fail(e);
      else throw e;
    }
  }
  var readingCombos = false;
  function readCombos(force) {
    if (!force && readingCombos) return;
    var comboExits = dataViewModel.liveStraddles().map(function (c) {
      return ko.unwrap(c.combo) + "," + ko.unwrap(c.exit) + "," + ko.unwrap(c.exitDelta);
    });
    var args = [pair, dataViewModel.comboGap(), dataViewModel.numOfCombos(), parseFloat(dataViewModel.comboCurrentStrikeLevel()), comboExits];
    args.noNote = true;
    readingCombos = true;
    serverCall("readStraddles", args
      , function () {
      }, function () {
        readingCombos = false;
      });
  }

  // #endregion

  // #region dataViewModel
  var dataViewModel = new DataViewModel();
  function DataViewModel() {
    var self = this;
    this.canTrade = ko.observable(false);
    // #region formatters
    this.chartDateFormat = d3.timeFormat('%m/%d/%Y %H:%M:%S');
    // #endregion
    // #region Locals
    function lineChartDataEmpty() {
      return [{ d: new Date("1/1/1900"), do: new Date("1/1/1900"), c: 0, v: 0, m: 0 }];// jshint ignore:line
    }
    // #endregion
    this.pairs = ko.observableArray();
    this.pairCurrent = ko.observable(pair);
    this.pairCurrent.subscribe(function (pc) {
      if (pc.toUpperCase() == pair) return;
      var newUrl = location.href.replace(location.search, "") + "?pair=" + pc;
      location = newUrl;
    });
    this.pairHedgedCurrent = ko.observable(pair);
    this.pairHedgedCurrent.subscribe(function (ph) {
      serverCall("setHedgedPair", [pair, ph]);
    });

    this.isVirtual = ko.observable(true);
    var inPause = this.inPause = ko.observable(true);
    this.togglePause = togglePause;
    function togglePause(chartNum) {
      serverCall("togglePause", [pair, chartNum || 0], function (pause) {
        inPause(pause);
      });
    }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    this.clearChartData = function () { lineChartData(lineChartDataEmpty()); }
    var lineChartData2 = ko.observableArray(lineChartDataEmpty());
    this.clearChartData2 = function () { lineChartData2(lineChartDataEmpty()); }

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
    var tradePresetLevel = this.tradePresetLevel = ko.observable(0);
    var tradeTrends = this.tradeTrends = ko.observable("");
    this.tradeTrendsInt = ko.pureComputed(function () {
      return tradeTrends().split(',').map(parseFloat);
    });
    var tradeTrendIndex = this.tradeTrendIndex = ko.observable(0);
    this.setTradeLevels = function (level, isBuy) {
      serverCall("setPresetTradeLevels", [pair, level, isBuy === undefined ? null : isBuy], function () {
        tradePresetLevel(level);
      });
    };
    this.setTradeTrendIndex = function (index) {
      serverCall("setTradeTrendIndex", [pair, index], function () {
        tradeTrendIndex(index);
      });
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
    this.setTradeRate = function setTradeLeve(isBuy, rate) {
      var args = [pair, isBuy, rate];
      args.noNote = true;
      serverCall("setTradeRate", args);
    }
    this.manualToggle = function () {
      serverCall("manualToggle", [pair]);
    }
    // #endregion

    // #region Buy/Sell
    this.hasTrades = ko.observable();
    this.isQuickTrade = ko.observable(false);
    this.toggleQuickTrade = function () {
      this.isQuickTrade(!this.isQuickTrade());
    }.bind(this);
    this.sell = function () { serverCall("sell", [pair]); }
    this.buy = function () { serverCall("buy", [pair]); }
    var openTrades = ko.observable({});
    this.canShowClose = ko.pureComputed(function () {
      return Object.keys(openTrades()).length || self.hasTrades();
    });
    this.closeTrades = function () {
      serverCall("closeTrades", [pair], resetPlotter);
    }
    this.closeTradesAll = function () {
      serverCall("closeTradesAll", [pair], resetPlotter);
    }
    this.canShowBuyButton = ko.pureComputed(function () {
      return !openTrades().buy;
    });
    this.canShowSellButton = ko.pureComputed(function () {
      return !openTrades().sell;
    });
    // #endregion

    // #region Start Stop Trades
    var tradeLevels = ko.observable({});
    this.showStopTrades = ko.pureComputed(function () {
      return tradeLevels().canBuy || tradeLevels().canSell;
    });
    this.showBuyTrades = ko.pureComputed(function () {
      return !tradeLevels().canBuy;
    });
    this.showSellTrades = ko.pureComputed(function () {
      return !tradeLevels().canSell;
    });
    this.stopTrades = function () {
      serverCall("stopTrades", [pair], resetPlotter);
    }
    this.startBuyTrades = function () {
      serverCall("startTrades", [pair, true], resetPlotter);
    }
    this.startSellTrades = function () {
      serverCall("startTrades", [pair, false], resetPlotter);
    }

    // #endregion

    function moveCorridorWavesCount(chartIndex, step) {
      if (chartIndex !== 0) return alert("chartIndex:" + chartIndex + " is not supported");
      var name = "PriceCmaLevels_";
      readTradeSettings(chartIndex, function (ts) {
        var value = Math.round((ts[name].v + step / 10) * 10) / 10;
        saveTradeSetting(chartIndex, name, value, function (ts, note) {
          var pcl = (ts || {})[name].v;
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
    function saveTradeSetting(chartNum, name, value, done) {
      var ts = {};
      ts[name] = value;
      serverCall("saveTradeSettings", [pair, chartNum, ts], done);
    }
    function saveTradeSettings(chartNum) {
      var ts = settingsGrid().jqPropertyGrid('get');
      settingsGrid().empty();
      serverCall("saveTradeSettings", [pair, chartNum, ts]);
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
          TipRatio: { options: { step: 0.1 } },
          RatesDistanceMin: { options: { step: 0.1 } },
          DoAdjustExitLevelByTradeTime: { name: "Adjust Exit By Trade" },
          MoveWrapTradeWithNewTrade: { name: "ForceWrapTrade" },
          TradingRatioByPMC: { name: "Lot By PMC" },
          LimitProfitByRatesHeight: { name: "Limit Profit By Height" },
          FreezeCorridorOnTradeOpen: { name: "Freeze On TradeOpen" },
          TradingAngleRange_: { name: "Trading Angle", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TakeProfitXRatio: { name: "Take ProfitX", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          TradingDistanceX: { name: "Trading DistanceX", type: 'number', options: { step: 0.1, numberFormat: "n" } },
          PriceCmaLevels_: { name: "PriceCmaLevels", type: 'number', options: { step: 0.001, numberFormat: "n" } },
          CorridorLengthDiff: { name: "CorridorLengthDiff", type: 'number', options: { step: 0.01, numberFormat: "n" } },

          TradeDirection: {
            type: "options", options: [
              { text: "None", value: "None" },
              { text: "Up", value: "Up" },
              { text: "Down", value: "Down" },
              { text: "Both", value: "Both" },
              { text: "Auto", value: "Auto" }]
          },
          TradingDistanceFunction: { name: "Trading Distance", type: "options", options: tradingMacroTakeProfitFunction() },
          TakeProfitFunction: { name: "Take Profit", type: "options", options: tradingMacroTakeProfitFunction() },
          LevelSellBy: { name: "Level Sell", type: "options", options: tradeLevelBys() },
          LevelBuyBy: { name: "Level Buy", type: "options", options: tradeLevelBys() },
          LevelSellCloseBy: { name: "Level Sell Close", type: "options", options: tradeLevelBys() },
          LevelBuyCloseBy: { name: "Level Buy Close", type: "options", options: tradeLevelBys() },
          ScanCorridorBy: { name: "ScanCorridor", type: "options", options: scanCorridorFunction() },
          RatesLengthBy: { name: "RatesLength", type: "options", options: ratesLengthFunction() },
          VoltageFunction: { name: "Voltage", type: "options", options: voltageFunction() },
          VoltageFunction2: { name: "Voltage 2", type: "options", options: voltageFunction() },
          CorridorCalcMethod: { name: "Corr Calc", type: "options", options: corridorCalculationMethod() },
          MovingAverageType: { type: "options", options: movingAverageType() },
          BarPeriod: { name: "Bars Period", type: "options", options: barsPeriodType() },
          PairHedge: { type: "options", options: self.pairs() },
          Strategy: { type: "options", options: strategyType() },

          //

          WaveSmoothBy: { name: "WaveSmoothBy", type: "options", options: waveSmoothByFunction() },

          WaveFirstSecondRatioMin: { name: "Wave 1/2 Ratio" }
        };
        var properties = {}, meta = {};
        $.map(ts, function (v, n) {
          properties[n] = v.v;
          meta[n] = { group: v.g, name: v.dn };
        });
        settingsGrid().jqPropertyGrid(properties, $.extend(true, tsMeta, meta));
      });
    }
    this.newTradingMacroName = ko.observable();
    this.saveTradingMacros = function () {
      serverCall("saveTradingMacros", [self.newTradingMacroName()]);
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

    // #region Public

    // #region Server enums
    this.toggleStartDate = toggleStartDate.bind(null, 0);
    this.rsdMin = ko.observable();
    this.rsdMin.subscribe(function (rsd) { setRsdMin(0, rsd); });
    this.rsdMin2 = ko.observable();
    this.rsdMin2.subscribe(function (rsd) { setRsdMin(1, rsd); });
    this.refreshCharts = function () {
      this.clearChartData();
      this.clearChartData2();
      askPriceChanged();
    }.bind(this);
    this.wrapTradeInCorridor = wrapTradeInCorridor;
    this.moveCorridorWavesCount = moveCorridorWavesCount;
    this.tradeConditions = ko.observableArray([]);
    var closedTrades = [];
    var mustShowClosedTrades2 = ko.observable(true);
    this.mustShowClosedTrades = ko.pureComputed(function () { return mustShowClosedTrades2() });
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
            dates: [t.Time, t.TimeClose],
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
    this.setTradeCount = setTradeCount;
    this.toggleIsActive = toggleIsActive;
    this.loadTradeSettings = loadTradeSettings.bind(null, 0);
    this.loadTradeSettings2 = loadTradeSettings.bind(null, 1);
    var tradeSettingsCurrent = this.tradeSettingsCurrent = ko.observable(0);

    this.tradeOpenActionsReady = ko.observable(false);

    // #region tradeConditions
    this.tradingConditionsReady = ko.observable(false);
    this.readTradingConditions = readTradingConditions;
    this.saveTradeConditions = saveChecked.bind(null, self.tradeConditions, "setTradingConditions");
    this.getTradingConditions = getChecked.bind(null, self.tradingConditionsReady, "getTradingConditions", self.tradeConditions);
    // #endregion
    function getChecked(isReadeObservable, serverMethod, checkedSubject) {
      isReadeObservable(false);
      function hasName(name) { return function (name2) { return name === name2; }; }
      serverCall(serverMethod, [pair], function (tcs) {
        checkedSubject().forEach(function (tc) {
          tc.checked(tcs.filter(hasName(tc.name)).length > 0);
        });
        isReadeObservable(true);
      });
    }
    function saveChecked(checkeds, serverMethod) {
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
    // #region wwwSettings
    var wwwSettingsElement;
    this.wwwSettingsDialog = function (element) {
      wwwSettingsElement = element;
    }
    var wwwSettingsGridElement;
    this.wwwSettingsGrid = function (element) {
      wwwSettingsGridElement = element;
    }
    this.showNegativeVolts = ko.observable(-10000);
    this.showNegativeVolts2 = ko.observable(-10000);
    this.doShowChartBid = ko.observable("p");

    // #region refreshChartsInterval
    this.refreshChartsInterval = ko.observable(1000 * 10);
    this.refreshChartsInterval.subscribe(function () {
      if (resetPlotterHandler()) {
        clearInterval(resetPlotterHandler());
        resetPlotterHandler(0);
      }
    })
    this.refreshCharts2Interval = ko.observable(1000 * 10);
    this.refreshCharts2Interval.subscribe(function () {
      if (resetPlotter2Handler()) {
        clearInterval(resetPlotter2Handler());
        resetPlotter2Handler(0);
      }
    })
    // #endregion

    this.showNegativeVoltsParsed = ko.pureComputed(function () {
      var e = eval(this.showNegativeVolts());
      return Array.isArray(e) ? e : [e];
    }, this);
    this.showNegativeVolts2Parsed = ko.pureComputed(function () {
      var e = eval(this.showNegativeVolts2());
      return Array.isArray(e) ? e : [e];
    }, this);
    this.wwwSettingProperties = ko.pureComputed(function () {
      function gettype(v) { return typeof v === "boolean" ? "checkbox" : "text" }
      return [
        { n: "doShowChartBid", v: self.doShowChartBid, t: gettype(self.doShowChartBid()) },
        { n: "refreshChartsInterval", v: self.refreshChartsInterval, t: gettype(self.refreshChartsInterval()) },
        { n: "showNegativeVolts", v: self.showNegativeVolts, t: gettype(self.showNegativeVolts()) },
        { n: "showNegativeVolts2", v: self.showNegativeVolts2, t: gettype(self.showNegativeVolts2()) }
      ];
    });
    this.showWwwSettings = function () {
      $(wwwSettingsElement).dialog({
        title: "WWW Settings", width: "auto", dialogClass: "dialog-compact",
        dragStop: function (event, ui) { $(this).dialog({ width: "auto", height: "auto" }); },
        close: function () { $(this).dialog("destroy"); }
      });
      //$(wwwSettingsGridElement).jqPropertyGrid(properties);
    };
    // #endregion
    // #region Butterflies
    this.activeCombos = ko.pureComputed(function () {
      return this.currentCombos()
        .filter(function (v) {
          if (v && !v.isActive)
            v.isActive = ko.observable(false);
          return v && v.isActive();
        })
        .sort(function (l, r) {
          return l.i() < r.i() ? 1 : -1;
        });
    }, this);
    this.comboQuantity = ko.observable(1);
    this.comboCurrentStrikeLevel = ko.observable("");
    this.toggleComboCurrentStrikeLevel = function () {
      self.comboCurrentStrikeLevel(!self.comboCurrentStrikeLevel() ? self.priceAvg() : "");
    }
    this.comboGap = ko.observable(1);
    this.numOfCombos = ko.observable(0);
    this.comboGap.subscribe(function (v) {
      readCombos(true);
      dataViewModel.butterflies([]);
    })
    this.newProfit = function (a, b, c) {
      var profitAmount = parseFloat(b.target.value);
      if (!isNaN(profitAmount)) {
        var instrument = a.combo();
        var orderId = a.orderId();
        serverCall("updateCloseOrder", [instrument, orderId, null, profitAmount]);
      }
    }
    this.showNextInput = function (a, b, c) {
      var v = $(b.target).toggle().text().replace("$", "");
      $(b.target).next("input").eq(0).toggle().focus().val(v);
    }
    this.hideNextInput = function (a, b, c) {
      $(b.target).toggle();
      $(b.target).prev("span").toggle();
    }
    this.newTakeProfit = function (a, b, c) {
      var limit = parseFloat(b.target.value);
      if (!isNaN(limit)) {
        var instrument = a.combo();
        var orderId = a.orderId();
        serverCall("updateCloseOrder", [instrument, orderId, limit, null]);
      }
    }

    this.orders = ko.mapping.fromJS(ko.observableArray());
    this.bullPuts = ko.mapping.fromJS(ko.observableArray());
    this.options = ko.mapping.fromJS(ko.observableArray());
    this.butterflies = ko.mapping.fromJS(ko.observableArray());
    this.currentCombos = ko.pureComputed(function () {
      return this.butterflies()
        .concat(this.bullPuts())
        .concat(this.options());
    }, this);
    this.liveStraddles = ko.mapping.fromJS(ko.observableArray());//;
    this.liveCombos = ko.pureComputed(function () {
      return self.liveStraddles()
        .filter(function (v) { return v && v.combo; })
        .map(function (v) {
          if (!v.exitDelta)
            v.exitDelta = ko.observable();
          return v;
        });
    }, this);
    this.combosMin = ko.pureComputed(function () {
      return [];
      return this.butterflies()
        .sort(function (v1, v2) { return v1.avg() - v2.avg(); })
        .map(function (v) { return { bid: v.bid(), ask: v.ask(), spread: v.ask() - v.bid(), avg: v.avg() }; })
        .slice(0, 1);
    }, this);
    this.combosStats = ko.pureComputed(function () {
      var combos = this.butterflies();
      var top2 = combos
        //.sort(function (v1, v2) { return v1.avg - v2.avg; })
        .slice(0, 2);
      var wSum = top2.map(function (v) { return v.strike() / v.delta(); }).sum();
      var sum = top2.map(function (v) { return 1 / v.delta(); }).sum();
      var strike = wSum / sum;
      var spread = top2.map(function (v) { return v.ask() - v.bid(); }).sum() / 2;
      var price = this.price.peek();
      var stDev = standardDeviation(combos.map(function (v) { return (v.ask() + v.bid()) / 2; }));
      return { strikeDelta: strike - (price.bid + price.ask) / 2, spread: spread, stDev: stDev };
    }, this);
    this.butterfliesDialog = ko.observable();
    this.butterfliesDialog.subscribe(function () {
      setTimeout(self.showButterflies, 1000);
    });
    this.openButterfly = function (isBuy, key) {
      this.canTrade(false);
      var combo = ko.utils.unwrapObservable(ko.utils.unwrapObservable(key).i);
      serverCall("openButterfly", [combo, (isBuy ? 1 : -1) * this.comboQuantity()]
        , null
        , null
        , function () { this.canTrade(false); }.bind(this)
      );
    }.bind(this);
    this.closeCombo = function (key) {
      this.canTrade(false);
      serverCall("closeCombo", [ko.utils.unwrapObservable(key)], null, null, function () { this.canTrade(false); }.bind(this));
    }.bind(this);
    this.cancelAllOrders = function (key) {
      serverCall("cancelAllOrders", []);
    };
    this.chartElement = ko.observable();
    var stopCombos = true;
    this.showButterflies = function () {
      showCombos = true;
      stopCombos = false;
      readCombos.bind(this)();
      this.butterflies([]);
      this.options([]);
      this.bullPuts([]);
      var shouldToggle = ko.observable(true);
      $(this.butterfliesDialog()).dialog({
        title: "Batterflies", width: "auto", dialogClass: "dialog-compact",
        dragStart: function () { shouldToggle(false); },
        dragStop: function (event, ui) {
          setTimeout(function () { shouldToggle(true); }, 100);
          $(this).dialog({ width: "auto", height: "auto" });
        },
        open: dialogCollapse(shouldToggle),
        close: function () {
          showCombos = false;
          stopCombos = true;
          $(this).dialog("destroy");
        },
        position: { my: "left top", at: "left top", of: self.chartElement() }
      });
    }.bind(this);

    // #endregion
    // #region hedgingRatiosDialog
    var stophedgingRatios;
    this.hedgingRatios = ko.observableArray();
    this.hedgingStats = ko.observableArray();
    this.hedgingRatiosDialog = ko.observable();
    var hedgingRatiosError = this.hedgingRatiosError = ko.observable(true);
    this.showHedgingRatios = function () {
      stophedgingRatios = false;
      hedgingRatiosError(true);
      readHedgingRatios.bind(this)();
      var shouldToggle = ko.observable(true);
      $(this.hedgingRatiosDialog()).dialog({
        title: "Hedging Ratios", width: "auto", dialogClass: "dialog-compact",
        dragStart: function () { shouldToggle(false); },
        dragStop: function (event, ui) {
          setTimeout(function () { shouldToggle(true); }, 100);
          $(this).dialog({ width: "auto", height: "auto" });
        },
        open: dialogCollapse(shouldToggle),
        close: function () {
          stophedgingRatios = true;
          $(this).dialog("destroy");
        }
      });
    }.bind(this);
    function readHedgingRatios() {
      var args = [pair];
      args.noNote = true;
      serverCall("readHedgingRatios", args, function (ret) {
        hedgingRatiosError(false);
        this.hedgingRatios(ret.hrs);
        this.hedgingStats($.map(ret.stats[0] || {}, function (v, n) { return { n: n, v: v }; }));
        this.hedgeTradesVirtual(ret.htvs || []);
        if (!stophedgingRatios)
          setTimeout(readHedgingRatios.bind(this), 2000);
      }.bind(this),
        function (error) {
          hedgingRatiosError(true);
          //showWarning("readHedgingRatios: " + error);
          setTimeout(readHedgingRatios.bind(this), 5000);
        }.bind(this));
    }
    this.openHedgeTrade = function (hp) {
      serverCall("openHedge", [pair, hp.IsBuy]);
      self.startAccounting();
    }
    this.openHedgeTrade = function (hp) {
      serverCall("openHedge", [pair, hp.IsBuy]);
      self.startAccounting();
    }
    this.hedgeVirtualDate = ko.observable(d3.timeFormat("%m/%d/%Y ")(new Date()));
    this.openHedgeVirtual = function (buy) {
      serverCall("openHedgeVirtual", [pair, buy, this.hedgeVirtualDate()]);
    }.bind(this);
    this.hedgeTradesVirtual = ko.observableArray([]);
    this.hedgeTradesVirtualDataSource = ko.pureComputed(function () {
      var columns = self.hedgeTradesVirtual().slice(0, 1).map(function (k) {
        return Object.keys(k);
      });
      var values = self.hedgeTradesVirtual().map(function (k) {
        return $.map(k, function (v) { return v; });
      });
      return columns.concat(values);
    });
    this.clearHedgeVirtualTrades = serverCall.bind(this, "clearHedgeVirtualTrades", [pair]);
    // #endregion

    // #region WwwInfo
    var wwwInfoElement;
    var currentWwwInfoChartNum;
    var stopWwwInfo;
    this.wwwInfoDialog = function (element) {
      var table = $(element).find("table");
      wwwInfoElement = table[0];
    }
    this.startWwwInfo = function (chartNum) {
      currentWwwInfoChartNum = chartNum;
      stopWwwInfo = false;
      $(wwwInfoElement).dialog({
        title: "WWW Info",
        width: "auto",
        dialogClass: "dialog-compact",
        dragStop: function (event, ui) {
          $(this).dialog({
            width: "auto",
            height: "auto"
          });
        },
        close: function () {
          stopWwwInfo = true;
          $(this).dialog("destroy");
        }
      });
      getWwwInfo(chartNum);

    }
    function getWwwInfo(chartNum) {
      var args = [pair, chartNum];
      args.noNote = true;
      var foo = getWwwInfo.bind(null, chartNum);
      serverCall("getWwwInfo", args,
        function (info) {
          wwwInfoRaw(info);
          if (!stopWwwInfo)
            setTimeout(foo, 1000);
        },
        function (error) {
          showWarning("getWwwInfo: " + error);
          setTimeout(foo, 5000);
        });
    }
    var wwwInfoRaw = ko.observable();
    this.wwwInfo = ko.pureComputed(function () {
      return $.map(wwwInfoRaw(), function (v, n) { return { n: n, v: v }; });
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
      function sync(tradeConditionsInfos, tcid) {
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
    // #region Strategies
    var strategyFilter = this.strategyFilter = ko.observable("");
    this.clearStrategy = function () {
      serverCall("clearStrategy", [pair], function () {
        readStrategies();
      });
    }
    var strategies = this.strategies = ko.observableArray();
    this.strategiesFiltered = ko.pureComputed(function () {
      return strategies().filter(function (s) {
        return s.nick.match(new RegExp(strategyFilter(), "i"));
      });
    })
    this.strategiesSelected = ko.pureComputed(function () {
      return strategies().filter(function (s) {
        return s.isSelected();
      });
    });
    var strategySelected = this.strategySelected = ko.observable();
    strategySelected.subscribe(function (s) {
      if (!s) return;
      setStrategy(s);
    });
    //#region
    this.offersDialog = ko.observable();
    this.offers = ko.observableArray();
    function mapOffers(offers) { this.offers(offers.map(function (o) { return ko.mapping.fromJS(o); })); }
    this.showOffers = function () {
      serverCall("readOffers", [], function (offers) {
        mapOffers.bind(this)(offers);
        $(this.offersDialog()).modal("show");
      }.bind(this));
    }.bind(this);
    this.loadOffers = function () {
      serverCall("loadOffers", [], mapOffers.bind(this));
    }.bind(this);
    this.getMMRs = function () {
      serverCall("getMMRs", []);
    }.bind(this);
    this.updateOffer = function (a, b, c) {
      serverCall("updateMMRs", [a.pair(), a.mmrBuy(), a.mmrSell()]);
    }
    this.saveOffers = function () {
      serverCall("saveOffers", []);
    }
    //#endregion
    this.strategiesDialog = ko.observable();
    var strategyNick = this.strategyNick = ko.observable();
    this.strategyNameInput = ko.observable();
    this.strategyNameDialog = ko.observable();
    this.saveStrategy = function () {
      serverCall("saveStrategy", [pair, this.strategyNick()], function () {
        setTimeout(this.showStrategies.bind(this), 1000);
      }.bind(this));
    }.bind(this);
    this.updateStrategy = function (data) {
      this.hideStrategies();
      this.strategyNick(data.nick);
      $(this.strategyNameDialog()).modal("show");
      //serverCall("saveStrategy", [pair, nick]);
    }.bind(this);
    this.showStrategies = function () {
      $(this.strategyNameDialog()).modal("hide");
      this.readStrategies();
      $(this.strategiesDialog()).modal("show");
      $(this.strategiesDialog()).find('.modal-dialog').draggable({
        handle: ".modal-header"
      });
    }.bind(this);
    this.hideStrategies = function () { $(this.strategiesDialog()).modal("hide"); }.bind(this);
    var setStrategy = this.setStrategy = function (strategy) {
      var strategyNick = (strategy || {}).nick || this.strategyNick();
      if (!strategyNick) return showErrorPerm("Empty strategy nick");
      serverCall("loadStrategy", [pair, strategyNick], " loading <b>" + strategyNick + "</b>")
        .done(function () {
          $(this.strategyNameDialog()).modal("hide");
          this.hideStrategies();
        }.bind(this));
    }.bind(this);
    this.removeStrategy = function (permanent) {
      var note = permanent ? "removed" : "archived";
      serverCall("removeStrategy", [this.strategyNick(), permanent], " " + note + " strategy <b>" + this.strategyNick() + "</b>")
        .done(function () {
          this.showStrategies();
          setTimeout(this.readStrategies.bind(this), 1000);
        }.bind(this));
    }.bind(this);
    var readStrategies = this.readStrategies = function readStrategies() {
      return serverCall("readStrategies", [pair], function (strategies) {
        this.strategies(strategies.map(function (s2) {
          var s = s2[0];
          return {
            nick: s.nick,
            name: s.diff.join("\n") || s.name,
            uri: s.uri,
            isActive: s.isActive,
            isSelected: ko.observable(false)
          };
        }));
      }.bind(this));
    }.bind(this);

    // #endregion
    // #region Charts
    this.chartArea = [
      {
        mouseData: ko.observableArray(),
        mouseClickData: ko.observable()
      },
      {
        mouseData: ko.observableArray(),
        mouseClickData: ko.observable()
      }];
    this.chartData = ko.observable(defaultChartData(0));
    this.chartData2 = ko.observable(defaultChartData(1));
    var priceEmpty = { ask: NaN, bid: NaN };
    this.price = ko.observable(priceEmpty);
    this.priceAvg = ko.pureComputed(function () {
      return (self.price().ask + self.price().bid) / 2;
    });
    // #region updateChart(2)
    this.updateChart = updateChart;
    this.updateChart2 = updateChart2;
    var commonChartParts = {};
    var prepResponse = prepDates.bind(null, ["rates", "rates2"]);
    var updateChartIntervalAverages = [ko.observable(), ko.observable()];
    var updateChartCmas = [ko.observable(), ko.observable()];
    this.stats = { ucia: updateChartIntervalAverages, ucCmas: updateChartCmas };
    this.mustShowStata = ko.pureComputed(function () {
      return
    });
    this.isTradingActive = isTradingActive = ko.observable(true);
    var resetPlotterHandler = ko.observable();
    var resetPlotter2Handler = ko.observable();
    var lastRefreshDate = ko.observable(new Date(1900, 1));
    var lastRefreshDate2 = ko.observable(new Date(1900, 1));
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
      if (response.isTrader) {
        tradeLevels(response.tradeLevels);
        openTrades(response.trades);
      }
      //lineChartData.sort(function (a, b) { return a.d < b.d ? -1 : 1; });
      response.waveLines.forEach(function (w, i) { w.bold = i == sumStartIndexById(); });
      isTradingActive(response.isTradingActive);
      var chartData = chartDataFactory(lineChartData, getTrends(response), response.tradeLevels, response.askBid, response.trades, response.isTradingActive, true, 0, response.hasStartDate, response.cmaPeriod, closedTrades, self.openTradeGross(), response.tpsHigh, response.tpsLow, response.canBuy, response.canSell, response.waveLines);
      chartData.com = prepDates($.extend(true, {}, self.com));
      chartData.com2 = prepDates($.extend(true, {}, self.com2));
      chartData.com3 = prepDates($.extend(true, {}, self.com3));
      chartData.isHedged = response.ish;
      chartData.hph = response.hph;
      chartData.vfs = !!response.vfs;
      resetRefreshChartInterval(chartData, lineChartData, lastRefreshDate, askRatesDatesReset);
      chartData.vfss = chartData.vfs & response.vfss;
      chartData.tps2High = response.tps2High;
      chartData.tps2Low = response.tps2Low;
      chartData.tpsCurr2 = response.tpsCurr2;
      self.chartData(chartData);
      updateChartCmas[0](cma(updateChartCmas[0](), 10, getSecondsBetween(new Date(), d)));
      dataViewModel.price(response.askBid);
    }
    function updateChart2(response) {
      var d = new Date();
      updateChartIntervalAverages[1](cma(updateChartIntervalAverages[1](), 10, getSecondsBetween(new Date(), ratesInFlight2)));

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
      prepResponse(response);
      var endDate = rates[0].do;
      var startDate = new Date(response.dateStart);
      lineChartData2.remove(function (d) {
        return d.do >= endDate || d.do < startDate;
      });
      lineChartData2.push.apply(lineChartData2, rates);
      lineChartData2.unshift.apply(lineChartData2, rates2);
      if (response.isTrader) {
        tradeLevels(response.tradeLevels);
        openTrades(mustShowClosedTrades2() ? response.trades : []);
      }
      var closedTradesLocal = mustShowClosedTrades2()
        ? closedTrades.map(function (ct) { return $.extend(true, {}, ct); })
        : [];
      function mapDates(v) { return v.dates || null; }
      var trends = getTrends(response);
      var com = prepDates($.extend(true, {}, self.com));
      var com2 = prepDates($.extend(true, {}, self.com2));
      var com3 = prepDates($.extend(true, {}, self.com3));
      var com4 = prepDates($.extend(true, {}, self.com4));
      var bth = (self.bth || []).map(function (o) { return prepDates($.extend(true, {}, o)); });
      var afh = (self.afh || []).map(function (o) { return prepDates($.extend(true, {}, o)); });
      var moreDates = []
        .concat(response.waveLines.map(mapDates))
        .concat(closedTradesLocal.map(mapDates))
        .concat(trends.map(mapDates))
        .concat(bth.map(mapDates))
        .concat(afh.map(mapDates))
        .concat([com, com2, com3, com4].map(mapDates));
      var ratesAll = continuoseDates("minute", lineChartData2(), moreDates);
      var shouldUpdateData = true;
      if (response.isTrader)
        commonChartParts.tradeLevels = response.tradeLevels;
      var chartData2 = chartDataFactory(ratesAll, trends, response.tradeLevels, response.askBid, response.trades, response.isTradingActive, shouldUpdateData, 1, response.hasStartDate, response.cmaPeriod, closedTradesLocal, self.openTradeGross(), response.tpsHigh, response.tpsLow, response.canBuy, response.canSell, response.waveLines);
      chartData2.com = com;
      chartData2.com2 = com2;
      chartData2.com3 = com3;
      chartData2.com4 = com4;
      chartData2.bth = bth;
      chartData2.afh = afh;
      chartData2.tickDate = lineChartData()[0].d;
      chartData2.tickDateEnd = Enumerable.from(lineChartData()).last().d;
      chartData2.vfs = !!response.vfs;
      chartData2.isHedged = response.ish;
      chartData2.hph = response.hph;
      resetRefreshChartInterval(chartData2, lineChartData2, lastRefreshDate2, askRatesDatesReset2);
      chartData2.vfss = chartData2.vfs && response.vfss;
      chartData2.tps2High = response.tps2High;
      chartData2.tps2Low = response.tps2Low;
      chartData2.tpsCurr2 = response.tpsCurr2;
      response.waveLines.forEach(function (w, i) {
        w.bold = i == sumStartIndexById();
        w.color = w.isOk ? "limegreen" : "";
      });
      self.chartData2(chartData2);
      updateChartCmas[1](cma(updateChartCmas[1](), 10, getSecondsBetween(new Date(), d)));
      dataViewModel.price(response.askBid);
    }
    function resetRefreshChartInterval(chartData, chartRates, lastRefreshDate, askRatesDatesReset) {
      if ((chartData.vfs || chartData.hph) && chartRates().length > 2) {
        var ratio = 300;
        var lastDate = Enumerable.from(chartRates()).last().d;
        var diffMax = (lastDate - chartRates()[0].d) / ratio;
        if (lastDate - lastRefreshDate() > diffMax) {
          lastRefreshDate(lastDate);
          askRatesDatesReset();
        }
      }
    }
    function getTrends(response) {
      return [response.trendLines0, response.trendLines1, response.trendLines, response.trendLines2, response.trendLines3];
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
    function firstDateImpl(lineChartData, key) {
      return (lineChartData()[0] || {})[key];
    }
    function firstDate() {
      return firstDateImpl(lineChartData, 'd');
    }
    function firstDate2() {
      return firstDateImpl(lineChartData2, 'do');
    }
    // #endregion
    // #endregion
    // #region Read Enums
    var tradingMacroTakeProfitFunction = this.tradingMacroTakeProfitFunction = ko.observableArray();
    var tradeLevelBys = this.tradeLevelBys = ko.observableArray();
    var scanCorridorFunction = this.scanCorridorFunction = ko.observableArray();
    var ratesLengthFunction = this.ratesLengthFunction = ko.observableArray();
    var voltageFunction = this.voltageFunction = ko.observableArray();
    var corridorCalculationMethod = this.corridorCalculationMethod = ko.observableArray()
    var movingAverageType = this.movingAverageType = ko.observableArray()
    var barsPeriodType = this.barsPeriodType = ko.observableArray()
    var strategyType = this.strategyType = ko.observableArray();

    var waveSmoothByFunction = this.waveSmoothByFunction = ko.observableArray();
    // #endregion
    // #region GetAccounting
    var accounting = this.accounting = ko.observableArray();
    var accountingDialog;
    var stopAccounting = false;
    var accountingError = this.accountingError = ko.observable(true);
    this.grossToExit = ko.observable();
    this.saveGrossToExit = function () {
      serverCall("saveGrossToExit", [this.grossToExit()], function () {
        this.grossToExit("");
      }.bind(this));
    }.bind(this);
    //
    this.profitByHedgeRatioDiff = ko.observable();
    this.saveProfitByHedgeRatioDiff = function () {
      serverCall("saveProfitByHedgeRatioDiff", [this.profitByHedgeRatioDiff()]);
    }.bind(this);
    this.accountingDialog = function (element) {
      var table = $(element).find("table");
      accountingDialog = table[0];
    };
    function getAccounting() {
      var args = [pair];
      args.noNote = true;
      function getByKey(a, key) {
        var i = a.findIndex(function (x) { return x.n == key; });
        return i < 0 ? null : (a.splice(i, 1)[0] || {}).v;
      }
      serverCall("getAccounting", args,
        function (acc) {

          var gte = getByKey(acc, "grossToExitRaw");
          if (gte && !self.grossToExit())
            self.grossToExit(gte);

          var prof = getByKey(acc, "profitByHedgeRatioDiff");
          if (prof && !self.profitByHedgeRatioDiff())
            self.profitByHedgeRatioDiff(prof);

          accounting(acc);
          accountingError(false);
          if (!stopAccounting)
            setTimeout(getAccounting, 1000);
        }.bind(this),
        function (error) {
          accountingError(true);
          //showErrorPerm("getAccounting: " + error);
          if (!stopAccounting)
            setTimeout(getAccounting, 1000);
        }.bind(this));
    }
    this.startAccounting = function () {
      stopAccounting = false;
      accountingError(true);
      accounting([]);
      var shouldToggle = ko.observable(true);
      $(accountingDialog).dialog({
        title: "Accounting",
        width: "auto",
        dialogClass: "dialog-compact",
        dragStop: function (event, ui) {
          setTimeout(function () { shouldToggle(true); }, 100);
          $(this).dialog({
            width: "auto",
            height: "auto"
          });
        },
        dragStart: function () { shouldToggle(false); },
        open: dialogCollapse(shouldToggle),
        close: function () {
          stopAccounting = true;
          $(this).dialog("destroy");
        }
      });
      getAccounting();
    };
    // #endregion
    //#region WaveRanges
    var currentWareRangesChartNum = 1;
    function getWaveRanges() {
      var args = [pair, currentWareRangesChartNum];
      args.noNote = true;
      serverCall("getWaveRanges", args,
        function (wrs) {
          wrs.forEach(function (wr) {
            wr.Power = { mx: false, v: wr.Distance.v * wr.HSD.v };
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
    function waveRangeValue(prop, wr) { return wr[prop].v; }
    this.waveRangesDialog = function (element) {
      var table = $(element).find("table");
      waveRangesDialog = table[0];
      table.on("click", "tbody tr", function (a, b) {
        var koData = ko.dataFor(this);
        var uid = waveRangeValue(waveRangesUidProp, koData);
        sumStartIndex(uid == sumStartIndex() ? 0 : uid);
      });
    };
    var waveRanges = ko.observableArray();
    this.waveRanges = ko.pureComputed(function () {
      var avg = waveRanges().find(function (wr) {
        return !!wr.IsStats;
      });
      var wrs = waveRanges().filter(function (wr) {
        return !wr.IsStats;
      });
      var prop = "DistanceCma";
      wrs.forEach(function (wr) {
        wr.isLong = wr[prop] > avg[prop];
      });
      return wrs;
    });
    this.waveRangesStats = ko.pureComputed(function () {
      return waveRanges().filter(function (wr) {
        return !!wr.IsStats;
      });
    });
    this.sumStartIndex = sumStartIndex;
    this.dbrSum = ko.pureComputed(sumByIndex.bind(null, "DistanceByRegression"));
    this.wbhSum = ko.pureComputed(sumByIndex.bind(null, "WorkByHeight"));
    this.distanceSum = ko.pureComputed(sumByIndex.bind(null, "Distance"));
    this.distanceCmaSum = ko.pureComputed(sumByIndex.bind(null, "DistanceCma"));
    this.sumStartIndexById = ko.pureComputed(sumStartIndexById);
    function fuzzyFind(array, prop, value) {
      if (!array || !array.length) return null;
      var diffs = array.map(function (v) {
        return { v: v, d: Math.abs(prop(v) - value) };
      });
      var r = _.chain(diffs).sortBy('d').first().value();
      return r.v;
    }
    var waveRangesUidProp = "StDev";
    function sumStartIndexById() {
      var uid = sumStartIndex();
      var wr = fuzzyFind(waveRanges(), waveRangeValue.bind(null, waveRangesUidProp), uid);
      return waveRanges().indexOf(wr);
    }
    function sumByIndex(prop) {
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
        dialogClass: "dialog-compact",
        dragStop: function (event, ui) {
          $(this).dialog({
            width: "auto",
            height: "auto"
          });
        },
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
    //#region replayDialog
    var replayDialog = this.replayDialog = ko.observable();
    var isReplayOn = this.isReplayOn = ko.observable(false);
    var replayDateStart = this.replayDateStart = ko.observable();
    var readReplayProcID = 0;
    function clearReadReplayArguments() {
      clearInterval(readReplayProcID);
      readReplayProcID = 0;
    }
    function readReplayArguments() {
      serverCall("readReplayArguments", withNoNote(pair), function (ra) {
        lastRefreshDate(new Date(1900, 0));
        lastRefreshDate2(new Date(1900, 0));
        replayDateStart(d3.timeFormat("%m/%d/%y %H:%M")(new Date(ra.DateStart)));
        isReplayOn(ra.isReplayOn);
        if (ra.isReplayOn && !readReplayProcID)
          readReplayProcID = setInterval(readReplayArguments, 5 * 1000);
        if (!ra.isReplayOn && readReplayProcID) clearReadReplayArguments();
      }, clearReadReplayArguments);
    }
    this.showReplayDialog = function () {
      $(replayDialog()).dialog({
        title: "Replay Controls",
        width: "auto",
        minHeight: 20,
        dragStop: function (event, ui) {
          $(this).dialog({
            width: "auto",
            height: "auto"
          });
        },
        dialogClass: "dialog-compact"
      });
      readReplayArguments();
    }
    this.startReplay = function () {
      serverCall("startReplay", [pair, replayDateStart()], function (replayArguments) {
        if (replayArguments.LastWwwError)
          showErrorPerm(replayArguments.LastWwwError, keyNote("startReplay"));
      });
      readReplayProcID = setInterval(readReplayArguments, 3 * 1000);
    }
    this.stopReplay = function () {
      serverCall("stopReplay", [pair], readReplayArguments);
    }
    // #endregion

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
    function chartDataFactory(data, trends, tradeLevels, askBid, trades, isTradingActive, shouldUpdateData, chartNum, hasStartDate, cmaPeriod, closedTrades, openTradeGross, tpsHigh, tpsLow, canBuy, canSell, waveLines) {
      function shrikData(data) { return data.length > 50 ? data : []; }
      return {
        data: data,
        trends: trends,
        waveLines: waveLines,
        tradeLevels: tradeLevels,
        askBid: askBid || {},
        trades: trades || [],
        isTradingActive: isTradingActive || false,

        setTradeLevelActive: setTradeLevelActive,
        setCorridorStartDate: setCorridorStartDate,
        togglePause: togglePause,
        toggleStartDate: toggleStartDate,
        moveCorridorWavesCount: moveCorridorWavesCount,

        shouldUpdateData: shouldUpdateData,
        chartNum: chartNum,
        hasStartDate: hasStartDate,
        cmaPeriod: cmaPeriod,
        closedTrades: closedTrades,
        openTradeGross: openTradeGross,
        tpsHigh: tpsHigh,
        tpsLow: tpsLow,

        canBuy: canBuy,
        canSell: canSell
      };
    }
    function continuoseDates(interval, data, dates) {// jshint ignore:line
      var ds = dates.map(function (ds) { return { dates2: [], dates: (ds || []).reverse() }; });
      data.reverse().reduce(function (prevValue, current) {
        var cdo = current.do;
        current.d = prevValue = (prevValue ? dateAdd(prevValue, interval, -1) : current.d);
        ds.forEach(function (d0) {
          if (d0.dates.length > 0)
            d0.dates.forEach(function (d) {
              if (d.valueOf() >= cdo.valueOf()) {
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
    function defaultChartData(chartNum) {
      return chartDataFactory(chartNum ? lineChartData2 : lineChartData, [{ dates: [] }, {}, {}, {}], null, null, null, false, false, chartNum, false, 0);
    }
    function dialogCollapse(shouldToggle) {
      return function () {
        $(this).prev().click(function () {
          if (shouldToggle()) $(this).next().toggle();
        });
      }
    }
    // #endregion
  }
  // #endregion

  // #region Init SignalR hub
  var host = location.host.match(/localhost/i) ? "ruleover.com:91" : location.host;
  var hubUrl = location.protocol + "//" + host + "/signalr/hubs";
  //$.getScript(hubUrl, init);
  // Init SignaR client
  init();
  var showCombos = false;
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
    var priceChanged = (function () {
      var _inFlightPriceChanged = dateMin;
      return _priceChanged;

      function _isPriceChangeInFlight() {
        var secsInFlight = getSecondsBetween(new Date(), _inFlightPriceChanged);
        if (secsInFlight > 3) openInFlightNote("PriceChange In flight > " + secsInFlight, keyNote("InFlightPriceChaneDelay"));
        if (secsInFlight > 6) return false;
        return _inFlightPriceChanged && secsInFlight > 0;
      }

      function _priceChanged(pairChanged) {
        if (showCombos) {
          readCombos();
        }
        if (!isDocHidden() && pair.toUpperCase() === pairChanged.toUpperCase()) {
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
    chat.client.lastWwwErrorChanged = function (le) {
      showErrorPerm(le, keyNote("lastWwwErrorChanged"));
    };
    chat.client.marketIsOpening = function (market) {
      showInfoPerm(JSON.stringify(market));
    };
    chat.client.newsIsComming = function (news) {
      showWarningPerm(JSON.stringify(news));
    };
    chat.client.warning = function (message) {
      showWarningPerm(message);
    };
    chat.client.bullPuts = function (options) {
      var isNew = dataViewModel.bullPuts().length == 0;
      if (!isNew)
        options.forEach(function (v) {
          delete v.isActive;
        });
      var map = {
        key: function (item) {
          return ko.utils.unwrapObservable(item.i);
        }
      };
      ko.mapping.fromJS(options, map, dataViewModel.bullPuts);
    };
    chat.client.options = function (options) {
      var isNew = dataViewModel.options().length == 0;
      if (!isNew)
        options.forEach(function (v) {
          delete v.isActive;
        });
      var map = {
        key: function (item) {
          return ko.utils.unwrapObservable(item.i);
        }
      };
      ko.mapping.fromJS(options, map, dataViewModel.options);
    };
    chat.client.butterflies = function (butterflies) {
      var isNew = dataViewModel.butterflies().length == 0;
      if (!isNew)
        butterflies.forEach(function (v) {
          delete v.isActive;
        });
      var map = {
        key: function (item) {
          return ko.utils.unwrapObservable(item.i);
        }
      };
      ko.mapping.fromJS(butterflies, map, dataViewModel.butterflies);
    };
    chat.client.liveCombos = function (combos) {
      var isNew = dataViewModel.liveStraddles().length != combos.length;
      var ignore = isNew ? [] : ["exit"];
      if (!isNew)
        combos.forEach(function (v) {
          delete v.exit;
          delete v.exitDelta;
        });
      else dataViewModel.liveStraddles([]);
      var map = {
        key: function (item) {
          return ko.utils.unwrapObservable(item.combo);
        },
        'ignore': ignore
      };
      ko.mapping.fromJS(combos, map, dataViewModel.liveStraddles);
      readingCombos = false;
    };
    chat.client.orders = function (orders) {
      ko.mapping.fromJS(orders, {}, dataViewModel.orders);
    };
    // #endregion
    // #region Start the connection.
    //$.connection.hub.logging = true;
    $.connection.hub.start().done(function (a) {
      try {
        showInfo(JSON.parse(a.data)[0].name + " started");
      } catch (e) {
        showErrorPerm("Unexpected start data:\n" + JSON.stringify(a.data) + "\nNeed refresh");
        return;
      }
      serverCall("readPairs", [], function (pairs) {
        dataViewModel.pairs(pairs);
        if (pair && pairs.length) {
          if (pairs.some(function (p) { return p.toUpperCase() === pair; }))
            return afterPairIsAvailible();
          return showErrorPerm(JSON.stringify({ pair: pair, pairs: pairs }));
        }
        else
          switch (pairs.length) {
            case 0:
              showErrorPerm(JSON.stringify({ pairs: pairs }));
              break;
            default:
              pair = pair || pairs.filter(function (p) { return p; })[0];
              dataViewModel.pairCurrent(pair);
              showWarning("Using " + pair);
              afterPairIsAvailible();
              if (pairs.length > 1)
                showErrorPerm(JSON.stringify({ pairs: pairs }));
              break;
          }
      });

      function afterPairIsAvailible() {
        //#region Load static data
        var defTDT = dataViewModel.readTradeDirectionTriggers();
        var defTC = dataViewModel.readTradingConditions();
        var defTOC = dataViewModel.readTradeOpenActions();
        serverCall("isInVirtual", [], function (response) {
          dataViewModel.isVirtual(response);
        })
        //#region Read Enums
        serverCall("readEnum", ["RatesLengthFunction"], function (enums) {
          dataViewModel.ratesLengthFunction(mapEnumsForSettings(enums));
        });
        function mapEnumsForSettings(enums) {
          return Object.keys(enums).map(function (v) { return { text: v, value: v } });
        }
        var defTPF = serverCall("readEnum", ["TradingMacroTakeProfitFunction"], function (enums) {
          dataViewModel.tradingMacroTakeProfitFunction(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["TradeLevelBy"], function (enums) {
          dataViewModel.tradeLevelBys(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["WaveSmoothBys"], function (enums) {
          dataViewModel.waveSmoothByFunction(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["ScanCorridorFunction"], function (enums) {
          dataViewModel.scanCorridorFunction(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["VoltageFunction"], function (enums) {
          dataViewModel.voltageFunction(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["CorridorCalculationMethod"], function (enums) {
          dataViewModel.corridorCalculationMethod(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["MovingAverageType"], function (enums) {
          dataViewModel.movingAverageType(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["BarsPeriodType"], function (enums) {
          dataViewModel.barsPeriodType(mapEnumsForSettings(enums));
        });
        serverCall("readEnum", ["Strategies"], function (enums) {
          dataViewModel.strategyType(mapEnumsForSettings(enums));
        });
        serverCall("readHedgedPair", [pair], function (hp) { dataViewModel.pairHedgedCurrent(hp); });
        //#endregion 
        //#region read trade-related data
        serverCall("getPresetTradeLevels", [pair], function (l) {
          dataViewModel.tradePresetLevel(l[0] || 0);
        });
        dataViewModel.readStrategies();
        dataViewModel.readClosedTrades();
        //#endregion
        //dataViewModel.readNews();
        $.when.apply($, [defTC, defTOC, defTDT]).done(function () {
          ko.applyBindings(dataViewModel);
        });
        serverCall("readTitleRoot", [], function (t) {
          document.title = t + devVersion + "::" + pair;
        });
        //#endregion
      }
      // #region This section should be implemented in dataViewModel
      $('#sendmessage').click(function () {
        // Call the Send method on the hub.
        chat.server.send(pair, $('#message').val());
        // Clear text box and reset focus for next comment.
        $('#message').val('').focus();
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
      $('#sell').click(function () { serverCall("sell", [pair]); });
      $('#buy').click(function () { serverCall("buy", [pair]); });
      //$('#flipTradeLevels').click(function () { serverCall("flipTradeLevels",[pair]); });
      // #endregion
    });
    // #endregion
    //setInterval(updateLineChartData, 10);
  }
  // #endregion

  // #region Note Reopener
  var errorNotes = [];
  function closeDisconnectNote() { closeErrorNote("TimeoutException"); }
  function closeReconnectNote() { closeErrorNote("reconnect"); }
  function openReconnectNote(note) { openErrorNote("reconnect", note); }
  function openErrorNote(key, note) {
    closeErrorNote(key);
    errorNotes.push([key, _.isFunction(note) ? note() : note]);
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
  // #region notify
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
    return notify(message, "warning", settings);
  }
  function showSuccess(message, settings) {// jshint ignore:line
    return notify(message, "success", $.extend({ type: "success" }, settings));
  }
  function showError(message, settings) {
    if (isMobile) return showWarning(message, settings);
    var keyNote = (settings || {}).keyNote;
    var note = notify((keyNote ? keyNote + ":" : "") + message, NOTE_ERROR, settings);
    if (!keyNote && (settings || {}).delay === 0)
      keyNote = message + "";
    if (keyNote) openErrorNote(keyNote, note);
    return note;
  }
  /* jshint ignore:end */
  function showErrorPerm(message, settings) {
    if (isMobile) return showWarning(message, settings);
    return showError(message, $.extend({ delay: 5000, hide: false }, settings));
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
  // #endregion

  function addMessage(response) {
    if (isDocHidden()) return;

    delete response.tps;
    delete response.wp;

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

    dataViewModel.com = response.com;
    delete response.com;

    dataViewModel.com2 = response.com2;
    delete response.com2;

    dataViewModel.com3 = response.com3;
    delete response.com3;

    dataViewModel.com4 = response.com4;
    delete response.com4;

    dataViewModel.bth = response.bth;
    delete response.bth;

    dataViewModel.afh = response.afh;
    delete response.afh;

    dataViewModel.tradePresetLevel(response.tpls[0] || 0);
    delete response.tpls;

    dataViewModel.tradeTrends(response.tts || "");
    delete response.tts;

    dataViewModel.tradeTrendIndex(response.tti[0] || -1);
    delete response.tti;

    dataViewModel.inPause(response.ip);
    delete response.ip;
    dataViewModel.hasTrades(response.ht);
    delete response.ht;

    $('#discussion').text(JSON.stringify(response).replace(/["{}]/g, ""));

    resetPlotter();
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
  function getQueryVariable(variable) {
    var query = window.location.search.substring(1);
    var vars = query.split('&');
    for (var i = 0; i < vars.length; i++) {
      var pair = vars[i].split('=');
      if (decodeURIComponent(pair[0]).toLowerCase() == variable.toLowerCase()) {
        return decodeURIComponent(pair[1]);
      }
    }
    console.log('Query variable %s not found', variable);
  }
  // #endregion
})();