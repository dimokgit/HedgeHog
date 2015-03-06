/*global ko, d3*/
(function () {
  function tradeLevelUIFactory(x, y, on, manual) { return { x: x, y: y, on: on ? true : false, manual: manual ? true : false }; }
  var margin = { top: 0, right: 10, bottom: 20, left: 0 };
  ko.bindingHandlers.lineChart = {
    init: function (element) {
      "use strict";

      var 
          elementWidth = parseInt(d3.select(element).style("width"), 10),
          elementHeight = parseInt(d3.select(element).style("height"), 10),
          width = elementWidth - margin.left - margin.right,
          height = elementHeight - margin.top - margin.bottom,

      svg = d3.select(element).append("svg")
          .attr("width", width + margin.left + margin.right)
          .attr("height", height + margin.top + margin.bottom)
          .append("g")
          .attr("transform", "translate(" + margin.left + "," + margin.top + ")");

      svg.append("g")
          .attr("class", "x axis")
          .attr("transform", "translate(0," + height + ")");

      svg.append("g")
          .attr("transform", "translate(" + (width) + ",0)")
          .attr("class", "y axis")
          .append("text")
          .attr("transform", "rotate(-90)")
          .attr("y", 6)
          .attr("dy", ".71em")
      //.style("text-anchor", "end")
      //.text("Price ($)");
      ;
      svg.append("path").attr("class", "line data");
      // #region create chart elements
      addLine(1); addLine(2); addLine(3); addLine(21); addLine(31);
      addLine("buyEnter"); addLine("sellEnter"); addLine("buyClose"); addLine("sellClose");
      addLine("corridorStart");
      addLine("ask"); addLine("bid"); addLine("trade");
      // #endregion
      // #region Set trade levels controls
      var chkBoxData = [tradeLevelUIFactory(10, 20, true), tradeLevelUIFactory(10, 20, true)];
      var checkBoxTemplate = '<input type="checkbox" style="margin:1px"></input>';
      svg
        .selectAll("foreignObject")
        .data(chkBoxData)
        .enter()
        .append("foreignObject")
        .attr("class", function (d, i) { return "tradeLineUI isActive" + i; })
        .attr("width", 16)
        .attr("height", 16)
        .attr('x', function (d, i) { return d.x; })
        .attr('y', function (d, i) { return d.y; })
        .append("xhtml:body")
        .html("<div style='background-color: red;text-align:center'>"+checkBoxTemplate+"</div>")
        .style("background-color", "Transparent")

      ;
      // #endregion
      /// Locals
      function addLine(lineSuffix) {
        svg.append("line")
          .attr("class", "line" + lineSuffix)
          .attr("x1", 0).attr("y1", 0).attr("x2", 0).attr("y2", 0);
      }
    },
    update: function (element, valueAccessor) {
      "use strict";

      var 
          elementWidth = parseInt(d3.select(element).style("width"), 10),
          elementHeight = parseInt(d3.select(element).style("height"), 10),
          width = elementWidth - margin.left - margin.right,
          height = elementHeight - margin.top - margin.bottom,
          animationDuration = 0,
          x = d3.time.scale().range([0, width]),
          y = d3.scale.linear().range([height, 0]),
          xAxis = d3.svg.axis().scale(x).orient("bottom"),
          yAxis = d3.svg.axis().scale(y).orient("left"),
          // define the graph line
          line = d3.svg.line()
              .x(function (d) { return x(d.d); })
              .y(function (d) { return y(d.c); }),
          svg = d3.select(element).select("svg g");

      // #region parse data from the data-view-model
      var chartData = ko.unwrap(valueAccessor());
      var data = chartData.data;
      var tradeLevels = chartData.tradeLevels || {};
      var trendLines = chartData.trendLines;
      var askBid = chartData.askBid;
      var trades = chartData.trades;
      var isTradingActive = chartData.isTradingActive;
      // #endregion
      // #region Set chart range
      var yDomain = d3.extent(data.map(function (d) { return d.c; }).concat([tradeLevels.buy, tradeLevels.sell]));
      x.domain(d3.extent(data, function (d) { return d.d; }));
      var vOffset = (yDomain[1] - yDomain[0]) / 20;
      yDomain = [yDomain[0] - vOffset, yDomain[1] + vOffset];
      y.domain(yDomain);
      // #endregion
      // #region transition axises
      svg.select("g.x.axis")
        .attr("transform", "translate(0," + height + ")")
        .transition()
        .duration(animationDuration)
        .call(xAxis);
      svg.select("g.y.axis")
        .transition()
        .attr("transform", "translate(" + (width) + ",0)")
        .duration(animationDuration)
        .call(yAxis);
      // #endregion
      // #region add the price to the canvas
      svg.select("path.line.data")
        .style("stroke", trades.buy ? "darkgreen" : trades.sell ? "darkred" : "steelblue")  // colour the line
        .datum(data)
        //.transition()
        .duration(animationDuration)
        .attr("d", line);
      // #endregion
      // #region add trend corridor
      setTrendLine(trendLines, 1, "lightgrey");
      setTrendLine(trendLines, 2, "darkred");
      setTrendLine(trendLines, 3, "darkred");
      setTrendLine(trendLines, 21, "darkred");
      setTrendLine(trendLines, 31, "darkred");
      setTimeLine(trendLines.dates[0], "corridorStart", "darkorange");
      // #endregion

      setTradeLevel(tradeLevels.buy, "buyEnter", "darkred");
      setTradeLevel(tradeLevels.sell, "sellEnter", "darkblue");

      setHLine(askBid.ask, "ask", "steelblue", 1, "2,2");
      setHLine(askBid.bid, "bid", "steelblue", 1, "2,2");
      setHLine(trades.buy || trades.sell, "trade", trades.buy ? "darkgreen" : "red", 1, "2,2,5,2");

      var chkBoxData = [
        tradeLevelUIFactory(x(data[0].d), y(tradeLevels.buy) - 20, tradeLevels.canBuy,tradeLevels.manualBuy),
        tradeLevelUIFactory(x(data[0].d), y(tradeLevels.sell), tradeLevels.canSell,tradeLevels.manualSell)] ;
      svg.selectAll("*.tradeLineUI")
        .data(chkBoxData)
        .attr('x', function (d, i) { return d.x; })
        .attr('y', function (d, i) { return isNaN(d.y) ? 0 : d.y; })
      ;
      svg.selectAll("*.tradeLineUI input")
        .data(chkBoxData)
        .property('checked', function (d, i) { return d.on ? true : false; });

      svg.selectAll("*.tradeLineUI >body >div")
        .data(chkBoxData)
        .style('background-color', function (d, i) { return d.manual ? "red" : "transparent"; });

      d3.select(element).select("svg")
        .style('background-color', isTradingActive ? "whitesmoke" : "peachpuff");


      // #region Locals
      function setTimeLine(time, name, lineColour) {
        if (time)
          svg.select("line.line" + name)
            .style("stroke", lineColour)  // colour the line
            .style("stroke-width", 1)  // colour the line
            .style("stroke-dasharray", "2,2")  // colour the line
            .attr("x1", x(time)) // x position of the first end of the line
            .attr("y1", y(yDomain[0])) // y position of the first end of the line
            .attr("x2", x(time)) // x position of the second end of the line
            .attr("y2", y(yDomain[1]));// y position of the second end of the line
        //.duration(animationDuration);    
      }
      function setHLine(level, levelName, levelColour, width, dasharray) {
        if (level)
          svg.select("line.line" + levelName)
            .style("stroke", levelColour)  // colour the line
            .style("stroke-width", width)  // colour the line
            .style("stroke-dasharray", dasharray)  // colour the line
            .attr("x1", x(data[0].d)) // x position of the first end of the line
            .attr("y1", y(level)) // y position of the first end of the line
            .attr("x2", x(data[data.length - 1].d)) // x position of the second end of the line
            .attr("y2", y(level));// y position of the second end of the line
        //.duration(animationDuration);    
      }
      function setTradeLevel(level, levelName, lineColour) {
        var dates = [data[0].d, data[data.length - 1].d];
        if (dates) {
          if (level)
            svg.select("line.line" + levelName)
              .style("stroke", lineColour)  // colour the line
              .style("stroke-width", 2)  // colour the line
              .style("stroke-dasharray", "5,5")  // colour the line
              .attr("x1", x(dates[0])) // x position of the first end of the line
              .attr("y1", y(level)) // y position of the first end of the line
              .attr("x2", x(dates[1])) // x position of the second end of the line
              .attr("y2", y(level));// y position of the second end of the line
          //.duration(animationDuration);    
        }
      }
      function setTrendLine(trendLines, lineNumber, lineColour) {
        var dates = (trendLines || {}).dates;
        if (dates) {
          var line = trendLines["close" + lineNumber];
          if (line)
            svg.select("line.line" + lineNumber)
              .style("stroke", lineColour)  // colour the line
              .attr("x1", x(dates[0])) // x position of the first end of the line
              .attr("y1", y(line[0])) // y position of the first end of the line
              .attr("x2", x(dates[1])) // x position of the second end of the line
              .attr("y2", y(line[1]));// y position of the second end of the line
          //.duration(animationDuration);    
        }
      }
      // #endregion
    }
  };
})();