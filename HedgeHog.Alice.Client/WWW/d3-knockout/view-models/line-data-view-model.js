/// <reference path="http://knockoutjs.com/downloads/knockout-3.3.0.js" />
/*global ko, setInterval*/

var D3KD = this.D3KD || {};

(function (namespace) {
    "use strict";
    namespace.dataViewModel = function() {
        var self = this,

            stockChange = function (previousValue) {
              var volatility = 0.7,
                  bias = 1.5,
                  change = bias * volatility * Math.random();

              if (change > volatility || previousValue - change <= 0.1) {
                return previousValue + change;
              }

              return previousValue - change;
            },

            lineDataPoint = function (previousValue) {
              return {
                date: new Date(),
                close: stockChange(previousValue || Math.random() * 10)
              };
            },

            updateLineChartData = function () {
                var previousValue = self.lineChartData()[
                    self.lineChartData().length - 1
                ].close;
                self.lineChartData.push(lineDataPoint(previousValue));
                while (self.lineChartData().length > 800) self.lineChartData.shift();
            };

        self.lineChartData = ko.observableArray([lineDataPoint()]).extend({ rateLimit: 50 });
    };
}(D3KD));