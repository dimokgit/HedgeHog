/// <reference path="http://knockoutjs.com/downloads/knockout-3.3.0.js" />
/// <reference path="view-models/line-data-view-model.js" />
/*global ko*/

var D3KD = this.D3KD || {};

(function () {
  "use strict";
  var dataViewModel = new function () {
    self = this;
    /// Local
    function lineChartDataEmpty() { return [{ d: new Date("1/1/1900"), c: 0 }]; }
    var lineChartData = ko.observableArray(lineChartDataEmpty());
    var trendLines = {};
    var tradeLevels = {};
    var trades = [];
    var askBid = {};
    /// Public
    this.chartNum = ko.observable(0);
    this.profit = ko.observable(0);
    this.chartData = ko.observable(defaultChartData());
    this.chartNum.subscribe(function () {
      lineChartData(lineChartDataEmpty());
    });
    this.updateChart = updateChart;
    this.lastDate = lastDate;
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
      self.chartData(chartDataFactory(lineChartData, response.trendLines, response.tradeLevels, response.askBid, response.trades,response.isTradingActive));
    }
    function lastDate() {
      var lastIndex = Math.max(0, lineChartData().length - 1);
      return (lineChartData()[lastIndex] || {}).d;
    }
    function chartDataFactory(data, trendLines, tradeLevels, askBid, trades, isTradingActive) {
      trendLines.dates = (trendLines.dates || []).map(function (d) { return new Date(d); });
      return {
        data: data(),
        trendLines: trendLines,
        tradeLevels: tradeLevels,
        askBid: askBid || {},
        trades: trades || [],
        isTradingActive: isTradingActive || false
      };
    }
  }

  var host = location.host.match(/localhost/i) ? "ruleover.com:91" : location.host;
  var hubUrl = location.protocol + "//" + host + "/signalr/hubs";
  $.getScript(hubUrl, init);
  var pair = "usdjpy";
  function plotterSrc() { return "http://" + host + "/" + pair + $("#plotterNum").val() + "?" + new Date().getTime(); }
  function init() {
    $('#plotter').attr("src", plotterSrc());
    //Set the hubs URL for the connection
    $.connection.hub.url = "http://" + host + "/signalr";

    // Declare a proxy to reference the hub.
    var chat = $.connection.myHub;

    // Create a function that the hub can call to broadcast messages.
    function addMessage(response) {
      if (!$('#rsdMin').is(":focus")) $('#rsdMin').val(response.rsdMin);
      delete response.rsdMin;
      dataViewModel.profit(response.prf);
      delete response.prf;
      $('#discussion').html(JSON.stringify(response).replace(/["{}]/g, ""));
      resetPlotter();
      chat.server.askRates(1200, dataViewModel.lastDate().toISOString(), pair, dataViewModel.chartNum());
    }
    function addRates(response) {
      dataViewModel.updateChart(response);
    }
    chat.client.addRates = addRates;
    chat.client.addMessage = addMessage;
    chat.client.newInfo = function (info) {
      $('#discussion').append('<li><strong>' + info + '</strong></li>');
    }
    // Get the user name and store it to prepend to messages.
    //$('#displayname').val(prompt('Enter your name:', ''));
    // Set initial focus to message input box.
    $('#message').focus();
    // Start the connection.
    $.connection.hub.start().done(function () {
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
      $('#startBuy').click(function () {
        chat.server.startTrades(pair, true);
        resetPlotter();
      });
      $('#startSell').click(function () {
        chat.server.startTrades(pair, false);
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
      $('#buyUp').click(function () {
        moveTradeLeve(true, 1);
      });
      $('#buyDown').click(function () {
        moveTradeLeve(true, -1);
      });
      $('#sellUp').click(function () {
        moveTradeLeve(false, 1);
      });
      $('#sellDown').click(function () {
        moveTradeLeve(false, -1);
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
    function resetPlotter() {
      setTimeout(function () {
        $('#plotter').attr("src", plotterSrc());
      }, 1000);

    }
    ko.applyBindings(dataViewModel);
    //setInterval(updateLineChartData, 10);
  }
}());