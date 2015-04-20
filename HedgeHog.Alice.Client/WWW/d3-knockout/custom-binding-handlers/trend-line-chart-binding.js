// jscs:disable
/*global ko, d3*/
/*ignore jscs*/
(function () {
  function tradeLevelUIFactory(x, y, on, manual, tradeCount) { return { x: x, y: y, on: on ? true : false, manual: manual ? true : false, tradeCount: tradeCount }; }
  var margin = { top: 0, right: 10, bottom: 20, left: 0 };
  function calcElementHeight(width){
    return width * 9 / 16.0 - 15;
  }
  var xAxisOffset = 5;
  function svgFrom(element) {
    return d3.select(element).select("svg g");
  }
  function calcChartArea(element) {
    var elementWidth = parseInt(d3.select(element).style("width"), 10),
      elementHeight = calcElementHeight(elementWidth),// parseInt(d3.select(element).style("height"), 10),
      width = elementWidth - margin.left - margin.right,
      height = elementHeight - margin.top - margin.bottom,
      x = d3.time.scale().range([xAxisOffset, width - xAxisOffset]),
      y = d3.scale.linear().range([height, 0]),
      y2 = d3.scale.linear().range([height, height * 4 / 5]);
      return { width: width, height: height, x: x, y: y, y2: y2 };
  }
  ko.bindingHandlers.lineChartPrice = {
    update: function (element, valueAccessor, allBindings, viewModel, bindingContext) {
      "use strict";
      var price = ko.unwrap(valueAccessor());
      var chartNum = +allBindings.get("chartNum");
      viewModel = bindingContext.$root;
      var cha = viewModel.chartArea[chartNum];
      if (!cha.yDomain || !cha.yDomain.length || cha.yDomain[0] === cha.yDomain[1]) return;
      var chartArea = calcChartArea(element);
      chartArea.y.domain(cha.yDomain);
      chartArea.x.domain(cha.xDomain);
      var x1 = chartArea.x(cha.xDomain[0]);
      var x2 = chartArea.x(cha.xDomain[1]);
      if (price.ask) {
        setHLine(chartArea.y, x1, x2, price.ask, "ask");
        setHLine(chartArea.y, x1, x2, price.bid, "bid");
      }
      function setHLine(y, x1, x2, level, levelName) {
        svgFrom(element).select("line.line" + levelName)
          .attr("y1", y(level))
          .attr("x1", x1 - xAxisOffset) // x position of the first end of the line
          .attr("x2", x2 + xAxisOffset) // x position of the second end of the line
          .attr("y2", y(level));
      }
    }
  };
  ko.bindingHandlers.lineChart = {
    init: function (element,valueAccessor) {
      "use strict";

      var chartData = ko.unwrap(valueAccessor());
      var chartArea = calcChartArea(element);

      // #region Chart/svg
      var
        width = chartArea.width,
        height = chartArea.height,
        svg = d3.select(element).append("svg")
        .attr("width", width + margin.left + margin.right)
        .attr("height", height + margin.top + margin.bottom)
        .on("dblclick", chartData.toggleIsActive.bind(chartData, chartData.chartNum))
        .append("g")
        .attr("transform", "translate(" + margin.left + "," + margin.top + ")");
      // #endregion

      // #region axis'
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
      svg.append("g")
          .attr("transform", "translate(" + (width) + ",0)")
          .attr("class", "y2 axis")
          .append("text")
          .attr("transform", "rotate(-90)")
          .attr("y", 6)
          .attr("dy", ".71em");
      // #endregion

      svg.append("path").attr("class", "line data");
      svg.append("path").attr("class", "line dataMA").style("stroke", "black");
      svg.append("path").attr("class", "line dataTps").style("stroke", "black").style("opacity", 0.25);

      // #region create chart elements
      // Trend Lines
      addLine(1); addLine(2); addLine(3); addLine(21); addLine(31);
      addLine("2_2"); addLine("3_2");
      addLine("2_1"); addLine("3_1");
      // Other lines
      addLine("buyEnter"); addLine("sellEnter"); addLine("buyClose"); addLine("sellClose");
      addLine("corridorStart");
      svg
        .append("path")
        .attr("id", "clearStartDate")
        .attr("d", d3.svg.symbol().type("circle").size(150))
        .on("click", chartData.toggleStartDate.bind(chartData, chartData.chartNum))
      ;
      svg.selectAll("path.nextWave")
          .data([10, 20])
          .enter()
          .append("path")
          .attr("class", "nextWave")
          .attr("d", d3.svg.symbol().type("circle").size(150))
          .attr("transform", "rotate(-90)")
          .on("click", function (d, i) {
            chartData.moveCorridorWavesCount(chartData.chartNum, i == 0 ? 1 : -1);
          })
        ;
      addLine("ask", "steelblue", 1, "2,2");
      addLine("bid", "steelblue", 1, "2,2"); 
      addLine("trade");
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

      // #region Locals
      function addLine(lineSuffix, color, width, dashArray) {
        svg.append("line")
          .attr("class", "line" + lineSuffix)
          .style("stroke", color)  // colour the line
          .style("stroke-width", width)  // colour the line
          .style("stroke-dasharray", dashArray)  // colour the line
          .attr("x1", 0).attr("y1", 0).attr("x2", 0).attr("y2", 0);
      }
      // #endregion
    },
    update: function (element, valueAccessor,allBindings,viewModel,bindingContext) {
      "use strict";
      viewModel = bindingContext.$root;
      var chartData = ko.unwrap(valueAccessor());
      var data = chartData.data;
      var cmaPeriod = chartData.cmaPeriod;
      if (data.length === 0) {
        $(element).hide();
        return;
      }
      // #region adjust svg and axis'
      $(element).show();
      var chartArea = calcChartArea(element);
      var
          width = chartArea.width,
          height = chartArea.height,
          x = chartArea.x,
          y = chartArea.y,
          y2 = chartArea.y2,
          xAxis = d3.svg.axis().scale(x).orient("bottom"),
          yAxis = d3.svg.axis().scale(y).orient("left"),
          yAxis2 = d3.svg.axis().scale(y2).orient("right");
          // define the graph line
      var line = d3.svg.line()
          .x(function (d) { return x(d.d); })
          .y(function (d) { return y(d.c); });
      //setCma(data, "c", "ma", cmaPeriod);
      //var _ma, line1 = d3.svg.line()
      //    .x(function (d) { return x(d.d); })
      //    .y(function (d, i) {
      //      return y(d.ma);
      //      //return y(_ma = Cma(_ma, 150, d.c));
      //    });
      var line2 = d3.svg.line()
          .x(function (d) { return x(d.d); })
          .y(function (d) { return y2(d.v); });
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
      // #endregion
      var svg = svg0.select("g");

      // #region parse data from the data-view-model
      var tradeLevels = chartData.tradeLevels || {};
      var trendLines = chartData.trendLines;
      var trendLines2 = chartData.trendLines2;
      var trendLines1 = chartData.trendLines1;
      var trades = chartData.trades;
      var isTradingActive = chartData.isTradingActive;
      var shouldUpdateData = chartData.shouldUpdateData || svgChanged;
      var chartNum = chartData.chartNum;
      // #endregion

      // #region Set chart range
      var yDomain = d3.extent(data.map(function (d) { return d.c; }));
      function sbchnum(value) {
        return chartNum ? value : yDomain[1];
      }
      yDomain = d3.extent([yDomain[0], yDomain[1], tradeLevels.buy, tradeLevels.sell
            , sbchnum(trades.buy ? tradeLevels.buyClose : trades.sell ? tradeLevels.sellClose : yDomain[1])]);
        var xDomain = viewModel.chartArea[chartNum].xDomain = d3.extent(data, function (d) { return d.d; });
        x.domain(xDomain);
        var vOffset = (yDomain[1] - yDomain[0]) / 20;
        viewModel.chartArea[chartNum].yDomain = yDomain = [yDomain[0] - vOffset, yDomain[1] + vOffset];
        y.domain(yDomain);
        var yDomain2 = d3.extent(data, function (d) { return d.v; });
        y2.domain([yDomain2[0], yDomain2[1]]);
      // #endregion

      // #region transform axises
      svg.select("g.x.axis")
        .attr("transform", "translate(0," + height + ")")
        .call(xAxis);
      svg.select("g.y.axis")
        .attr("transform", "translate(" + (width) + ",0)")
        .call(yAxis);
      svg.select("g.y2.axis")
        .attr("transform", "translate(" + (0) + ",0)")
        .call(yAxis2);
      // #endregion

      // #region add the price line to the canvas
      var dataLine = svg.select("path.line.data");
      dataLine.style("stroke", trades.buy ? "darkgreen" : trades.sell ? "darkred" : "steelblue");
      if (shouldUpdateData) {
        dataLine
          .datum(data)
          .attr("d", line);
        //svg.select("path.line.dataMA")
        //  .datum(data)
        //  .attr("d", line1);
        svg.select("path.line.dataTps")
          .datum(data)
          .attr("d", line2);

        // #region add trend corridor
        setTrendLine(trendLines, 1, "lightgrey");
        setTrendLine(trendLines, 2, "darkred");
        setTrendLine(trendLines, 3, "darkred");
        setTrendLine(trendLines, 21, "darkred");
        setTrendLine(trendLines, 31, "darkred");

        setTrendLine2(trendLines2, 2, 2, "steelblue");
        setTrendLine2(trendLines2, 3, 2, "steelblue");

        setTrendLine2(trendLines1, 2, 1, "lightseagreen");
        setTrendLine2(trendLines1, 3, 1, "lightseagreen");
      // #endregion
      }
      // #endregion

      // #region Corridor start date line
      var corridorStartTime = trendLines.dates[0];
      setTimeLine(corridorStartTime, "corridorStart", chartData.hasStartDate ? "darkred" : "darkorange", chartData.hasStartDate ? 2 : 1);
      if (corridorStartTime) {
        svg.select("path#clearStartDate")
          .attr("transform", "translate(" + x(corridorStartTime) + "," + (height - 7) + ")");
        svg.selectAll("path.nextWave")
          .data([-18, 18])
          .attr("transform", function (d) {
            return "translate(" + (x(corridorStartTime) + d) + ",7) rotate(-90)";
          })
        ;
      }
      // #endregion
      // #endregion

      setHLine(trades.buy || trades.sell, "trade", trades.buy ? "darkgreen" : "red", 1, "2,2,5,2");

      // #region trade levels
      setTradeLevel(tradeLevels.buy, "buyEnter", "darkred",1);
      setTradeLevel(tradeLevels.buyClose, "buyClose", "darkblue", trades.buy ? 1 : 0, 3);
      setTradeLevel(tradeLevels.sell, "sellEnter", "darkblue",1);
      setTradeLevel(tradeLevels.sellClose, "sellClose", "darkred", trades.sell ? 1 : 0, 3);

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
      function setHLine(level, levelName, levelColour, width, dasharray) {
        var line = svg.select("line.line" + levelName);
        if (level)
          line
            .style("stroke", levelColour)  // colour the line
            .style("stroke-width", width)  // colour the line
            .style("stroke-dasharray", dasharray)  // colour the line
            .attr("x1", x(data[0].d) - xAxisOffset) // x position of the first end of the line
            .attr("y1", y(level)) // y position of the first end of the line
            .attr("x2", x(data[data.length - 1].d) + xAxisOffset) // x position of the second end of the line
            .attr("y2", y(level))// y position of the second end of the line
            .transition().duration(0.25);

          //.duration(animationDuration);    
        else
          line
            .style("stroke-width", 0);
      }
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
      function setTradeLevel(level, levelName, lineColour, strokeWidth, strokeDash) {
        strokeDash = strokeDash || 5;
        var strokeDashArray = strokeDash + "," + strokeDash;
        var dates = [data[0].d, data[data.length - 1].d];
        if (dates) {
          if (level)
            svg.select("line.line" + levelName)
              .style("stroke", lineColour)  // colour the line
              .style("stroke-width", strokeWidth)  // colour the line
              .style("stroke-dasharray", strokeDashArray)  // colour the line
              .attr("x1", x(dates[0]) - xAxisOffset) // x position of the first end of the line
              .attr("y1", y(level)) // y position of the first end of the line
              .attr("x2", x(dates[1]) + xAxisOffset) // x position of the second end of the line
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
      function setTrendLine2(trendLines, lineNumber,trendIndex, lineColour) {
        var dates = (trendLines || {}).dates;
        if (dates && dates.length) {
          var line = trendLines["close" + lineNumber];
          if (line)
            svg.select("line.line" + lineNumber + "_" + trendIndex)
              .style("visibility", "visible")
              .style("stroke", lineColour)  // colour the line
              .attr("x1", x(dates[0])) // x position of the first end of the line
              .attr("y1", y(line[0])) // y position of the first end of the line
              .attr("x2", x(dates[1])) // x position of the second end of the line
              .attr("y2", y(line[1]));// y position of the second end of the line
          //.duration(animationDuration);    
        } else
          svg.select("line.line" + lineNumber + "_" + trendIndex).style("visibility", "hidden");
      }
      // #endregion
    }
  };
  /// #region Global locals
  function setCma(data, valueName, maName, period) {
    var _ma;
    data.forEach(map(valueName, "ma"));
    _ma = undefined;
    data.reverse().forEach(map("ma", "ma"));
    data.reverse();

    function map(valueName, maName) {
      return function (d) {
        _ma = d[maName] = Cma(_ma, period, d[valueName]);
      };
    }
  }
  function Cma(MA, Periods, NewValue) {
    if (MA === undefined) return NewValue;
    return MA + (NewValue - MA) / (Periods + 1);
  }

  /// #endregion
})();