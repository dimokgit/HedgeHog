// jscs:disable
/*global ko, d3*/
/*ignore jscs*/
(function () {
  function tradeLevelUIFactory(x, y, on, manual, tradeCount) { return { x: x, y: y, on: on ? true : false, manual: manual ? true : false, tradeCount: tradeCount }; }
  var margin = { top: 0, right: 10, bottom: 20, left: 0 };
  function calcElementHeight(width){
    return width * 9 / 16.0 - 15;
  }
  ko.bindingHandlers.lineChart = {
    init: function (element,valueAccessor) {
      "use strict";

      var chartData = ko.unwrap(valueAccessor());
      var
          elementWidth = parseInt(d3.select(element).style("width"), 10),
          elementHeight = calcElementHeight(elementWidth),// parseInt(d3.select(element).style("height"), 10),
          width = elementWidth - margin.left - margin.right,
          height = elementHeight - margin.top - margin.bottom,

      svg = d3.select(element).append("svg")
        .attr("width", width + margin.left + margin.right)
        .attr("height", height + margin.top + margin.bottom)
        .on("dblclick", function () {
          chartData.toggleIsActive();
        })
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
      // Trend Lines
      addLine(1); addLine(2); addLine(3); addLine(21); addLine(31);
      addLine("2_2"); addLine("3_2");
      // Other lines
      addLine("buyEnter"); addLine("sellEnter"); addLine("buyClose"); addLine("sellClose");
      addLine("corridorStart");
      try {
        svg
          .append("path")
          .attr("id", "clearStartDate")
          .attr("d", d3.svg.symbol().type("cross"))
          .on("click", chartData.toggleStartDate)
        ;
      } catch (e) {
        debugger;
        throw e;
      }
      svg.selectAll("circle")
          .data([10, 20])
          .enter()
          .append("circle")
          .attr("class", "nextWave")
          .attr("r", 5)
          .attr("cx", function (d) { return d; })
          .attr("cy", 10)
          .on("click", function (d, i) {
            chartData.setCorridorStartDate(chartData.chartNum, i);
          })
        ;
      addLine("ask"); addLine("bid"); addLine("trade");
      // #endregion
      // #region Set trade levels controls
      var chkBoxData = [tradeLevelUIFactory(10, 20, true, 0), tradeLevelUIFactory(10, 20, true, 0)];
      var checkBoxTemplate = '<input type="checkbox" style="margin:1px"></input><span id="tradeCount"></span>';
      svg
        .selectAll("foreignObject")
        .data(chkBoxData)
        .enter()
        .append("foreignObject")
        .attr("class", function (d, i) { return "tradeLineUI isActive" + i; })
        .attr("width", 36)
        .attr("height", 16)
        .attr('x', function (d) { return d.x; })
        .attr('y', function (d) { return d.y; })
        .append("xhtml:body")
        .html("<div style='background-color: lightpink;text-align:center'>" + checkBoxTemplate + "</div>")
        .style("background-color", "Transparent");
      svg.selectAll("*.tradeLineUI input")
        .data(chkBoxData)
        .on('click', function (d, i) {
          chartData.setTradeLevelActive(i);
        });
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

      var chartData = ko.unwrap(valueAccessor());
      var data = chartData.data;
      if (data.length === 0) {
        $(element).hide();
        return;
      }
      $(element).show();
      var
          elementWidth = parseInt(d3.select(element).style("width"), 10),
          elementHeight = calcElementHeight(elementWidth),// parseInt(d3.select(element).style("height"), 10),
          width = elementWidth - margin.left - margin.right,
          height = elementHeight - margin.top - margin.bottom,
          animationDuration = 0,
          x = d3.time.scale().range([5, width-5]),
          y = d3.scale.linear().range([height, 0]),
          xAxis = d3.svg.axis().scale(x).orient("bottom"),
          yAxis = d3.svg.axis().scale(y).orient("left"),
          // define the graph line
          line = d3.svg.line()
              .x(function (d) { return x(d.d); })
              .y(function (d) { return y(d.c); });
      var svgW = width + margin.left + margin.right;
      var svgH = height + margin.top + margin.bottom;
      var svg0 = d3.select(element)
        .select("svg");
      var xChanged = parseInt(svg0.attr("width")) !== svgW;
      var yChanged = parseInt(svg0.attr("height")) !== svgH;
      var svgChanged = xChanged && yChanged;
      svg0
        .attr("width", svgW)
        .attr("height", svgH);
      var svg = svg0.select("g");

      // #region parse data from the data-view-model
      var tradeLevels = chartData.tradeLevels || {};
      var trendLines = chartData.trendLines;
      var trendLines2 = chartData.trendLines2;
      var askBid = chartData.askBid;
      var trades = chartData.trades;
      var isTradingActive = chartData.isTradingActive;
      var shouldUpdateData = chartData.shouldUpdateData || svgChanged;
      // #endregion
      // #region Set chart range
        var yDomain = d3.extent(data.map(function (d) { return d.c; }));
        yDomain = d3.extent([yDomain[0], yDomain[1], tradeLevels.buy, tradeLevels.sell
            , trades.buy ? tradeLevels.buyClose : trades.sell ? tradeLevels.sellClose : tradeLevels.buy]);
        x.domain(d3.extent(data, function (d) { return d.d; }));
        var vOffset = (yDomain[1] - yDomain[0]) / 20;
        yDomain = [yDomain[0] - vOffset, yDomain[1] + vOffset];
        y.domain(yDomain);
      // #endregion
      // #region transition axises
      svg.select("g.x.axis")
        .attr("transform", "translate(0," + height + ")")
        //.transition()
        //.duration(animationDuration)
        .call(xAxis);
      svg.select("g.y.axis")
        //.transition()
        .attr("transform", "translate(" + (width) + ",0)")
        //.duration(animationDuration)
        .call(yAxis);
      // #endregion
      // #region add the price to the canvas
      var dataLine = svg.select("path.line.data");
      if (shouldUpdateData) {
        dataLine
          .datum(data)
          .transition()
          .duration(animationDuration)
          .attr("d", line);
        // #endregion
        // #region add trend corridor
        setTrendLine(trendLines, 1, "lightgrey");
        setTrendLine(trendLines, 2, "darkred");
        setTrendLine(trendLines, 3, "darkred");
        setTrendLine(trendLines, 21, "darkred");
        setTrendLine(trendLines, 31, "darkred");

        setTrendLine2(trendLines2, 2, "steelblue");
        setTrendLine2(trendLines2, 3, "steelblue");
      }

      // #region Corridor start date line
      var corridorStartTime = trendLines.dates[0];
      setTimeLine(corridorStartTime, "corridorStart", chartData.hasStartDate ? "darkred" : "darkorange", chartData.hasStartDate ? 2 : 1);
      if (corridorStartTime) {
        svg.select("path#clearStartDate")
        .attr("transform", function (d) { return "translate(" + x(corridorStartTime) + ",15)"; });
        svg.selectAll("circle.nextWave")
          .data([-15, 15])
          .attr("r", 5)
          .attr("cx", function (d) { return x(corridorStartTime) + d; })
        ;
      }
      // #endregion

      dataLine
        .style("stroke", trades.buy ? "darkgreen" : trades.sell ? "darkred" : "steelblue");  // colour the line
      // #endregion

      setHLine(askBid.ask, "ask", "steelblue", 1, "2,2");
      setHLine(askBid.bid, "bid", "steelblue", 1, "2,2");
      setHLine(trades.buy || trades.sell, "trade", trades.buy ? "darkgreen" : "red", 1, "2,2,5,2");

      // #region trade levels
      setTradeLevel(tradeLevels.buy, "buyEnter", "darkred",2);
      setTradeLevel(tradeLevels.buyClose, "buyClose", "darkblue", 1);
      setTradeLevel(tradeLevels.sell, "sellEnter", "darkblue",2);
      setTradeLevel(tradeLevels.sellClose, "sellClose", "darkred", 1);

      var chkBoxData = [
        tradeLevelUIFactory(x(data[0].d), y(tradeLevels.buy) - 16, tradeLevels.canBuy, tradeLevels.manualBuy, tradeLevels.buyCount),
        tradeLevelUIFactory(x(data[0].d), y(tradeLevels.sell), tradeLevels.canSell, tradeLevels.manualSell, tradeLevels.sellCount)];
      svg.selectAll("*.tradeLineUI")
        .data(chkBoxData)
        .attr('x', function (d) { return d.x; })
        .attr('y', function (d) { return isNaN(d.y) ? 0 : d.y; })
      ;
      svg.selectAll("*.tradeLineUI input")
        .data(chkBoxData)
        .property('checked', function (d) { return d.on ? true : false; });

      svg.selectAll("*.tradeLineUI span")
        .data(chkBoxData)
        .html(function (d) {
          return d.tradeCount;
        });

      svg.selectAll("*.tradeLineUI >body >div")
        .data(chkBoxData)
        .style('background-color', function (d) { return d.manual ? "#ffd3d9" : "transparent"; })
        .style('font-weight', function (d) { return d.manual ? "bold" : ""; });
      // #endregion

      d3.select(element).select("svg")
        .style('background-color', isTradingActive ? "whitesmoke" : "peachpuff");

      // #region Locals
      function setTimeLine(time, name, lineColour, width) {
        if (time)
          svg.select("line.line" + name)
            .style("stroke", lineColour)  // colour the line
            .style("stroke-width", width || 1)  // colour the line
            .style("stroke-dasharray", "2,2")  // colour the line
            .attr("x1", x(time)) // x position of the first end of the line
            .attr("y1", y(yDomain[0])) // y position of the first end of the line
            .attr("x2", x(time)) // x position of the second end of the line
            .attr("y2", y(yDomain[1]));// y position of the second end of the line
        //.duration(animationDuration);    
      }
      function setHLine(level, levelName, levelColour, width, dasharray) {
        var line =           svg.select("line.line" + levelName);
        if (level)
          line
            .style("stroke", levelColour)  // colour the line
            .style("stroke-width", width)  // colour the line
            .style("stroke-dasharray", dasharray)  // colour the line
            .attr("x1", x(data[0].d)) // x position of the first end of the line
            .attr("y1", y(level)) // y position of the first end of the line
            .attr("x2", x(data[data.length - 1].d)) // x position of the second end of the line
            .attr("y2", y(level))// y position of the second end of the line
            .transition().duration(0.25);

          //.duration(animationDuration);    
        else
          line
            .style("stroke-width", 0);
        }
      function setTradeLevel(level, levelName, lineColour,strokeWidth) {
        var dates = [data[0].d, data[data.length - 1].d];
        if (dates) {
          if (level)
            svg.select("line.line" + levelName)
              .style("stroke", lineColour)  // colour the line
              .style("stroke-width", strokeWidth || 2)  // colour the line
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
      function setTrendLine2(trendLines, lineNumber, lineColour) {
        var dates = (trendLines || {}).dates;
        if (dates && dates.length) {
          var line = trendLines["close" + lineNumber];
          if (line)
            svg.select("line.line" + lineNumber + "_2")
              .style("visibility", "visible")
              .style("stroke", lineColour)  // colour the line
              .attr("x1", x(dates[0])) // x position of the first end of the line
              .attr("y1", y(line[0])) // y position of the first end of the line
              .attr("x2", x(dates[1])) // x position of the second end of the line
              .attr("y2", y(line[1]));// y position of the second end of the line
          //.duration(animationDuration);    
        } else
          svg.select("line.line" + lineNumber + "_2").style("visibility", "hidden");
      }
      // #endregion
    }
  };
})();