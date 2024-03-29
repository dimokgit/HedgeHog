/// <reference path="../../bower_components/d3/d3.js" />
/// <reference path="../../Scripts/linq.js" />
/// <reference path="../../bower/bower_components/underscore/underscore.js" />

// jscs:disable
/*global ko, d3, Enumerable,_*/
/*ignore jscs*/
(function () {
  function tradeLevelUIFactory(x, y, on, manual, tradeCount, canTrade) {
    return {
      x: x, y: y,
      on: on ? true : false,
      manual: manual ? true : false,
      tradeCount: tradeCount,
      canTrade: canTrade
    };
  }
  //var margin = { top: 0, right: 10, bottom: 20, left: 0 };
  var margin = { top: 0, right: 0, bottom: 20, left: 0 };
  function calcElementHeight(width) {
    return width * 9 / 16.0 - 15;
  }
  var xAxisOffset = 5;
  function svgFrom(element) {
    return d3.select(element).select("svg g");
  }
  var heightOffset = 0.98;
  function calcChartArea(element, y2Scale) {
    var elementWidth = parseInt(d3.select(element).style("width"), 10),
      elementHeight = Math.round(calcElementHeight(elementWidth) * heightOffset),// parseInt(d3.select(element).style("height"), 10),
      width = elementWidth - margin.left - margin.right,
      height = elementHeight - margin.top - margin.bottom,
      x = d3.scaleTime().range([xAxisOffset, width - xAxisOffset]),
      //y = d3.scaleLinear().range([height, 0]),
      y = d3.scaleLinear().range([height, y2Scale ? height / 5 : 0]),

      //y2 = d3.scaleLinear().range([height, height * (y2Scale ? 4 : 0) / 5]);
      y2 = d3.scaleLinear().range([height, height * 4 / 5]);

    y3 = d3.scaleLinear().range([height / 5, 0]);
    return { width: width, height: height, x: x, y: y, y2: y2, y3: y3 };
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
      if (price.ask && !isNaN(x1) && !isNaN(x2)) {
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
  function LineChart() {
    this.data = Enumerable.from([]);
  }

  // #region locals
  var tpsOpacity = 0.35;
  var tickAreaBgColor = "lightgray";//lavender";
  var blueStrip = "blueStrip";
  var greenStrip = "greenStrip";
  var redStrip = "redStrip";
  var cyanStrip = "cyanStrip";
  var beforeTradeHoursRect = "beforeTradeHoursRect";
  var beforeCloseHoursRect = "beforeCloseHoursRect";
  var afterTradeHoursRect = "afterTradeHoursRect";
  var doCorridorStartDate = false;
  var showLineLog = false;
  var tpsChartNum = [0, 1];
  // #endregion

  ko.bindingHandlers.lineChart = {
    init: function (element, valueAccessor, allBindings, viewModel, bindingContext) {
      "use strict";

      viewModel = bindingContext.$root;
      var chartData = ko.unwrap(valueAccessor());
      var lineData = chartData.data;
      var chartNum = chartData.chartNum;
      var hasTps = tpsChartNum.indexOf(chartNum) >= 0;
      var chartArea = calcChartArea(element);
      var bisectDate = d3.bisector(function (d) { return d.d; }).left;


      // #region Chart/svg
      var
        width = chartArea.width,
        height = chartArea.height,
        svg = d3.select(element).append("svg")
          .attr("width", width + margin.left + margin.right)
          .attr("height", height + margin.top + margin.bottom)
          .on("dblclick", chartData.togglePause.bind(chartData, 0))
          .append("g")
          .attr("transform", "translate(" + margin.left + "," + margin.top + ")");
      // #endregion

      // coms
      addRect(greenStrip, "#E6FAE6", 0.6);
      addRect(cyanStrip, "lightcyan", 1);
      addRect(redStrip, "#FAE6E6", 1);
      addRect(blueStrip, "lavender", 1);
      [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10].forEach(function (i) {
        addRect(afterTradeHoursRect + i, "beige", 0.3);
        addRect(beforeTradeHoursRect + i, "lightcyan", 1);
        addRect(beforeCloseHoursRect + i, "lightcyan", 1);
      });

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
      if (chartNum === 1) {
        addRect("tickArea", tickAreaBgColor);
        addRect("dayTailRect", tickAreaBgColor);
      }
      if (hasTps) {
        svg.append("g")
          .attr("class", "y2 axis");
        addLine("tpsHigh", "silver").style("opacity", tpsOpacity);
        addLine("tpsLow", "silver").style("opacity", tpsOpacity);
        addLine("tpsCurr", "silver").style("opacity", tpsOpacity);
        addLine("tpsCurr2", "silver").style("opacity", tpsOpacity);
        addLine("tpsMin2", "silver").style("opacity", tpsOpacity);
        addLine("tpsMax2", "silver").style("opacity", tpsOpacity);
        svg.append("g")
          .attr("class", "y3 axis");

      }
      // #endregion

      if (hasTps) {
        svg.append("path").attr("class", "line dataTps").style("stroke", "black").style("opacity", tpsOpacity);
        svg.append("path").attr("class", "line dataTps01").style("stroke", "black").style("opacity", tpsOpacity);
        svg.append("path")
          .attr("class", "line dataTps2")
          .style("stroke", "black")
          .style("opacity", tpsOpacity)
          .attr("transform", "translate(0,0)");
        svg.append("path")
          .attr("class", "line dataTps21")
          .style("stroke", "black")
          .style("opacity", tpsOpacity)
          .attr("transform", "translate(0,0)");
      }
      svg.append("path").attr("class", "line dataBid");
      svg.append("path").attr("class", "line data");
      // #region create chart elements

      // create crosshairs
      var crosshair = svg.append("g")
        .attr("class", "line");
      // create horizontal line
      crosshair.append("line")
        .attr("id", "crosshairX")
        .attr("class", "crosshair");
      // create vertical line
      crosshair.append("line")
        .attr("id", "crosshairY")
        .attr("class", "crosshair");
      svg.append("rect")
        .attr("class", "overlay")
        .attr("width", width)
        .attr("height", height)
        .on("mouseover", function () {
          crosshair.style("display", null);
        })
        .on("mouseout", function () {
          crosshair.style("display", "none");
        })
        .on("mousemove", function () {
          var mouse = d3.mouse(this);
          var x = mouse[0];
          var y = mouse[1];
          {
            var cha = viewModel.chartArea[chartNum];
            var fd = lineData()[0].d;
            var mouseData = dataByMouse.bind(null, lineData)(cha, x);
            if (mouseData)
              cha.mouseData(mouseData);
            if (fd !== lineData()[0].d)
              console.log({ fd: fd, fd2: lineData()[0].d });
          }
          crosshair.select("#crosshairX")
            .attr("x1", x)
            .attr("y1", 0)
            .attr("x2", x)
            .attr("y2", calcChartArea(element).height);
          crosshair.select("#crosshairY")
            .attr("x1", 0)
            .attr("y1", y)
            .attr("x2", calcChartArea(element).width)
            .attr("y2", y);
        })
        .on("click", function () {
          console.log(d3.mouse(this));
          var cha = viewModel.chartArea[chartNum];
          var mouseData = dataByMouse.bind(null, lineData)(cha, d3.mouse(this)[0]);
          if (mouseData) {
            cha.mouseClickData(mouseData);
            viewModel.hedgeVirtualDate(viewModel.chartDateFormat(mouseData.do || mouseData.d));
          }
        });
      function dataByMouse(lineData, cha, x) {
        var chartArea = calcChartArea(element);
        var xScale = chartArea.x.domain(cha.xDomain);
        var mousDate = xScale.invert(x);
        var dateIndex = bisectDate(lineData(), mousDate);
        return lineData()[dateIndex];
      }
      $(window).resize(function () {
        svg.select("rect.overlay")
          .attr("width", calcChartArea(element).width)
          .attr("height", calcChartArea(element).height);
      });
      // Trend Lines
      /*addLine(1);*/ addLine("2_"); addLine("3_"); addLine(21); addLine(31);
      addLine("1_2 trend"); addLine("2_2"); addLine("3_2");
      addLine("2_1"); addLine("3_1");
      addLine("1_0 trend"); addLine("2_0"); addLine("3_0");
      addLine("2_3"); addLine("3_3");
      // Trade lines
      function isTradeDrag() {
        return viewModel.chartArea[chartNum].inDrag;
      }
      function setTradeDrag(inDrag) {
        viewModel.chartArea[chartNum].inDrag = inDrag;;
      }
      var drag = d3.drag()
        .on("drag", dragmove)
        .on("start", dragstart)
        .on("end", dragend);
      function dragmove(d) {
        var vm = viewModel.chartArea[chartNum].cha;
        var line = d3.select(this);
        line.attr("y1", d3.event.y).attr("y2", d3.event.y);
        setTradeRate.bind(this)();
      }
      function dragstart(d) {
        if (this.oldWidth) {
          var line = d3.select(this);
          line.style("stroke-width", this.oldWidth);
        }
      }
      function dragend(d) {
        setTradeDrag(false);
        setTradeRate.bind(this)();
      }
      function setTradeRate() {
        var y = viewModel.chartArea[chartNum].cha.y;
        var line = d3.select(this);
        var y1 = parseFloat(line.attr("y1"));
        var price = y.invert(y1);
        var isBuy = line.node().buySell;
        if (isBuy === undefined) return alert(JSON.stringify({ isBuy: isBuy }));
        viewModel.setTradeRate(isBuy === "buy", price);

      }
      function setTradeLine(name, color, key, drag) {
        addLine(name, color, 1, 5, drag).node().buySell = key;
      }
      setTradeLine("buyEnter", "darkred", "buy", drag); setTradeLine("sellEnter", "darkblue", "sell", drag);
      setTradeLine("buyClose", "darkblue"); setTradeLine("sellClose", "darkred");
      //#region Corridor StartDate
      //if (doCorridorStartDate) {
      //  addLine("corridorStart");
      //  svg
      //    .append("path")
      //    .attr("id", "clearStartDate")
      //    .attr("d", d3.symbolCircle.size(svg, 150))
      //    .on("click", chartData.toggleStartDate.bind(chartData, chartData.chartNum))
      //  ;
      //}
      ////#endregion
      //svg.selectAll("path.nextWave")
      //    .data([10, 20])
      //    .enter()
      //    .append("path")
      //    .attr("class", "nextWave")
      //    .attr("d", d3.symbolCircle.draw(svg, 150))
      //    .attr("transform", "rotate(-90)")
      //    .on("click", function (d, i) {
      //      chartData.moveCorridorWavesCount(chartData.chartNum, i === 0 ? 1 : -1);
      //    });
      addLine("ask", "steelblue", 1, "2,2");
      addLine("bid", "steelblue", 1, "2,2");
      addLine("trade");
      var breakEven = "breakEven";
      addLine(breakEven + "0");
      addLine(breakEven + "1");
      // #endregion

      // #region Set trade levels controls
      var chkBoxData = [tradeLevelUIFactory(10, 20, true, true, 0), tradeLevelUIFactory(10, 20, true, true, 0)];
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
      function addRect(name, color, opacity) {
        svg.append("rect")
          .attr("class", name)
          .style("fill", color)  // colour the line
          .style("opacity", opacity || 0.25)
          .attr("x", 0) // x position of the first end of the line
          .attr("y", 0) // y position of the first end of the line
          .attr("width", 10) // x position of the second end of the line
          .attr("height", 50);// y position of the second end of the line
      }
      function addLine(lineSuffix, color, width, dashArray, drag) {
        var line = svg.append("line")
          .attr("class", "line" + lineSuffix)
          .style("stroke", color)  // colour the line
          .style("stroke-width", width)  // colour the line
          .style("stroke-dasharray", dashArray)  // colour the line
          .attr("x1", 0).attr("y1", 0).attr("x2", 0).attr("y2", 0);
        if (drag)
          line
            .call(drag)
            .on("mouseover", onLineMouseOver)
            .on("mouseout", onLineMouseOut);

        return line;

        function onLineMouseOver(d, i) {
          if (isTradeDrag()) return;
          var line = d3.select(this);
          if (!this.oldWidth) this.oldWidth = parseInt(line.style("stroke-width"));
          line.style("stroke-width", this.oldWidth + 5);
        }
        function onLineMouseOut(d, i) {
          d3.select(this).style("stroke-width", this.oldWidth);
        }

      }
      // #endregion
    },
    update: function (element, valueAccessor, allBindings, viewModel, bindingContext) {
      "use strict";
      viewModel = bindingContext.$root;
      //var lineChart = viewModel.lineChart || (viewModel.lineChart = new LineChart());
      var chartData = ko.unwrap(valueAccessor());
      function avgerage(a, key) {// jshint ignore: line
        return _.reduce(a, function (sum, e) {
          return sum + e[key];
        }, 0) / a.length;
      }
      var data = ko.unwrap(chartData.data);
      //function roundDate(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate(), d.getHours(), d.getMinutes()); }
      var bufferCount = 1;//Math.floor(chartData.data.length / $(element).width());
      //var startDate = roundDate(chartData.data[0].d);
      //var leftTail = Enumerable
      //  .from(lineChart.data.toArray())
      //  .skipWhile(function (x) { return x.d < startDate; })
      //  .takeWhile(function (x) { return x.d < chartData.data[0].d; });
      //var dataBuffer = lineChart.data = leftTail
      //  .concat(chartData.data)
      //  .buffer(bufferCount);
      //var __data__ = dataBuffer
      //  .select(function (a) {
      //    //var max = _.max(a, 'c'),
      //    //  min = _.min(a, 'c'),
      //    //  avg = avgerage(a, "m");
      //    //var f = { c: max.c - avg > avg - min.c ? max.c : min.c, d: a[a.length - 1].d, v: _.max(a, 'v').v };
      //    var max = _.max(a, function (e) { return Math.abs(e.c - e.m); });
      //    var f = { c: max.c, m: max.m, d: max.d, v: _.max(a, 'v').v };
      //    return f;
      //  })
      //  .toArray();
      //var data = leftTail.toArray().concat(chartData.data);
      var __data__ = bufferCount === 1
        ? data
        : Enumerable
          .from(data)
          .buffer(bufferCount)
          .select(function (a) {
            var max = _.max(a, function (e) { return Math.abs(e.c - e.m); });
            var f = { c: max.c, m: max.m, d: max.d, v: _.max(a, 'v').v };
            return f;
          });

      if (data.length <= 1) {
        $(element).hide();
        return;
      }
      $(element).show();
      //#region parse data from the data-view-model
      var tradeLevels = chartData.tradeLevels;
      var trends = chartData.trends;
      var trendLines = trends[2];
      var trendLines2 = trends[3];
      var trendLines1 = trends[1];
      var trendLines0 = trends[0];
      var trendLines3 = trends[4];
      var openTrades = chartData.trades;
      var openBuy = openTrades.buy, openSell = openTrades.sell;
      var mustShowClosedTrades = viewModel.mustShowClosedTrades();
      var closedTrades = chartData.closedTrades;
      var isTradingActive = chartData.isTradingActive;
      var shouldUpdateData = chartData.shouldUpdateData || svgChanged;
      var openTradeGross = ko.unwrap(chartData.openTradeGross);
      var tpsHigh = chartData.tpsHigh;
      var tpsLow = chartData.tpsLow;
      var tps2High = chartData.tps2High || [];
      var tps2Low = chartData.tps2Low || [];
      var tpsCurr2 = chartData.tpsCurr2 || NaN;
      var tpsCurr = chartData.tpsCurr || NaN;
      var chartNum = chartData.chartNum;
      var hasTps = tpsChartNum.indexOf(chartNum) >= 0;
      var canBuy = chartData.canBuy;
      var canSell = chartData.canSell;
      var com = chartData.com;
      var com2 = chartData.com2;
      var com3 = chartData.com3;
      var com4 = chartData.com4;
      var bth = chartData.bth || [];
      var bcl = chartData.bcl || [];
      var afh = chartData.afh || [];
      var showNegativeVolts = viewModel.showNegativeVoltsParsed();
      var showNegativeVolts2 = viewModel.showNegativeVolts2Parsed();
      var doShowChartBid = viewModel.doShowChartBid();
      var y2Scale = !chartData.vfs;
      var yScale = parseInt(viewModel.y2Scale());
      var y2ScaleShift = chartData.vfss || [0, 0];
      var isHedged = chartData.isHedged;
      var breakEven = chartData.breakEven || [];
      var histVol = chartData.histVol || { up: 0, down: 0 };
      // #endregion

      // #region adjust svg and axis'
      $(element).show();

      var chartArea = calcChartArea(element, yScale);
      viewModel.chartArea[chartNum].cha = chartArea;
      var
        width = chartArea.width,
        height = chartArea.height,
        x = chartArea.x,
        y = chartArea.y,
        y2 = chartArea.y2,
        y3 = chartArea.y3,
        xAxis = d3.axisBottom().scale(x),
        yAxis = d3.axisLeft().scale(y),
        yAxis2 = hasTps ? d3.axisRight().scale(y2) : null,
        yAxis3 = hasTps ? d3.axisRight().scale(y3) : null;
      // define the graph line
      function lineYc(d) {
        return y(d.c);
      }
      function lineYa(d) {
        return y(d.a);
      }
      function lineYb(key) {
        return function (d) {
          return y(d[key]);
        }
      }
      function lineXd(d) {
        return x(d.d);
      }
      var line = d3.line()
        .x(lineXd)
        .y(chartNum === -1 ? lineYa : lineYc);
      var lineBid = !doShowChartBid ? null : d3.line()
        .x(lineXd)
        .y(lineYb(doShowChartBid));

      //setCma(data, "c", "ma", cmaPeriod);
      //var _ma, line1 = d3.svg.line()
      //    .x(function (d) { return x(d.d); })
      //    .y(function (d, i) {
      //      return y(d.ma);
      //      //return y(_ma = Cma(_ma, 150, d.c));
      //    });

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

      // #region Set chart range
      var yDomain = d3.extent(data.map(function (d) { return d.c; }).concat(chartNum === 1 ? breakEven : []));
      function sbchnum(value) {
        return chartNum ? value : yDomain[1];
      }
      function tipValue(v) {
        return isNaN(v) ? 0 : Math.min(Math.max(v, showNegativeVolts[0] || -Number.MAX_SAFE_INTEGER), showNegativeVolts[1] || Number.MAX_SAFE_INTEGER);
      }
      function tipValue2(v) {
        return isNaN(v) ? 0 : Math.min(Math.max(v, showNegativeVolts2[0]), showNegativeVolts2[1] || 100000);
      }
      yDomain = !y2Scale || isHedged ? yDomain : d3.extent([yDomain[0], yDomain[1]
        , sbchnum(tradeLevels && canBuy && y2Scale && tradeLevels.buy ? tradeLevels.buy : yDomain[1])
        , sbchnum(tradeLevels && canSell && y2Scale && tradeLevels.sell ? tradeLevels.sell : yDomain[1])
        , sbchnum(
          mustShowClosedTrades && openBuy && tradeLevels && y2Scale && tradeLevels.buyClose
            ? tradeLevels.buyClose
            : mustShowClosedTrades && openSell && tradeLevels && y2Scale && tradeLevels.sellClose
              ? tradeLevels.sellClose
              : yDomain[1])]);
      var xDomain = viewModel.chartArea[chartNum].xDomain = d3.extent(data, function (d) { return d.d; });
      x.domain(xDomain);
      var vOffset = 0;//(yDomain[1] - yDomain[0]) / 20;
      viewModel.chartArea[chartNum].yDomain = yDomain = [yDomain[0] - vOffset, yDomain[1] + vOffset];
      y.domain(yDomain);
      var yDomain2 = d3.extent(data, function (d) { return tipValue(d.v); });
      y2.domain([Math.min(y2ScaleShift[0] || yDomain2[0], yDomain2[0]), Math.max(y2ScaleShift[1] || yDomain2[1], yDomain2[1])]);
      var yDomain3 = d3.extent(data, function (d) { return tipValue2(d.v2); });
      y3.domain([Math.min(tps2Low[0] || Number.MAX_VALUE, yDomain3[0]), Math.max(tps2High[0] || 0, yDomain3[1])]);
      if (isNaN(y3(0)))
        debugger;
      // #endregion

      // #region transform axises
      svg.select("g.x.axis")
        .attr("transform", "translate(0," + height + ")")
        .call(xAxis);
      svg.select("g.y.axis")
        .attr("transform", "translate(" + (width) + ",0)")
        .call(yAxis);
      if (yAxis2) {
        var y2Axis = svg.select("g.y2.axis");
        y2Axis
          .style("opacity", y2Scale ? 1 : 0)
          .call(yAxis2);
      }
      var y3Axis = svg.select("g.y3.axis");
      if (yAxis3)
        y3Axis.call(yAxis3);
      // #endregion

      // #region add the price line to the canvas
      if (shouldUpdateData) {
        if (chartNum === 1 && showLineLog)
          var lineLogDate = new Date();
        //svg.select("path.line.data").remove();
        //svg.append("path").attr("class", "line data")
        svg.select("path.line.data")
          .style("stroke", openBuy ? "darkgreen" : openSell ? "darkred" : "steelblue")
          //.datum(bufferCount >= 3 ? __data__.toArray() : data)
          .datum(data)
          .attr("d", line);

        // #region  lineBid
        var bidColor = doShowChartBid === "p" ? "Wisteria" : openBuy ? "darkgreen" : openSell ? "darkred" : "steelblue";
        svg.select("path.line.dataBid")
          .style("display", lineBid ? "" : "none")
          //.style("stroke", bidColor)
          .datum(lineBid ? data : [])
          .attr("d", lineBid);
        // #endregion

        if (lineLogDate) {
          var lineLogDate2 = new Date(new Date() - lineLogDate);
          console.log("lineLogDate[" + chartNum + "]: " + (lineLogDate2.getSeconds() * 1000 + lineLogDate2.getMilliseconds()) / 1000 + " sec");
        }
        //svg.select("path.line.dataMA")
        //  .datum(data)
        //  .attr("d", line1);
        if (hasTps) {
          var line2 = d3.line()
            .x(function (d) { return x(d.d); })
            .y(function (d) {
              return y2(tipValue(d.v));
            });
          var line21 = d3.line()
            .x(function (d) { return x(d.d); })
            .y(function (d) {
              return y2(tipValue(d.v01));
            });
          var isHotTps = _.last(data).v > tpsHigh || _.last(data).v < tpsLow;
          var colorTps = !y2Scale ? "Wisteria" : isHotTps ? "darksalmon" : "navy";
          var opacityTps = !y2Scale ? tpsOpacity : isHotTps ? tpsOpacity * 2 : tpsOpacity;
          svg.select("path.line.dataTps")
            .datum(data)
            .attr("d", line2).style("stroke", colorTps).style("opacity", opacityTps);
          svg.select("path.line.dataTps01")
            .datum(data)
            .attr("d", line21).style("stroke", colorTps).style("opacity", opacityTps);
          setHLine(tpsHigh, "tpsHigh", colorTps, 1, "", y2);
          setHLine(tpsLow, "tpsLow", colorTps, 1, "", y2);
          setHLine(tpsCurr, "tpsCurr", colorTps, 1, "", y2);

          var hasTps2 = data.some(function (d) { return d.v2 !== 0; });
          if (hasTps2) {
            var yMin = Number.MAX_SAFE_INTEGER, yMax = Number.MIN_SAFE_INTEGER;
            var line3 = d3.line()
              .x(function (d) { return x(d.d); })
              .y(function (d) {
                var y = isNaN(d.v2) ? 0 : tipValue2(d.v2);
                return y3(y);
              });
            var line31 = d3.line()
              .x(function (d) { return x(d.d); })
              .y(function (d) {
                var y = isNaN(d.v21) ? 0 : tipValue2(d.v21);
                return y3(y);
              });

            var color = "lime";
            try {
              svg.select("path.line.dataTps2")
                .datum(data)
                .attr("d", line3)
                .style("stroke", color).style("opacity", 0.7);
              y3Axis.style("display", "");
            } catch (e) {
              console.log = "Showing path.line.dataTps2:" + e;
            }
            try {
              svg.select("path.line.dataTps21")
                .datum(data)
                .attr("d", line31)
                .style("stroke", "brown").style("opacity", 0.7);
              y3Axis.style("display", "");
            } catch (e) {
              console.log = "Showing path.line.dataTps21:" + e;
            }
            //var minMax = [yMin, yMax].sort(function (a, b) { return Math.abs(a) - Math.abs(b); });
            //setHLine(minMax[0], "tpsMin2", "darkred", 1, "", y3);
            //setHLine(Math.abs(minMax[0]), "tpsMax2", "darkred", 1, "", y3);
            setHLine(tps2Low.concat(NaN).splice(0, 1)[0], "tpsMin2", "darkred", 1, "", y3);
            setHLine(tps2High.concat(NaN).splice(0, 1)[0], "tpsMax2", "darkred", 1, "", y3);
            setHLine(tpsCurr2, "tpsCurr2", color, 2, "", y3);
          } else {
            y3Axis.style("display", "none");
            setHLine(NaN, "tpsCurr2", colorTps, 1, "", y3);
          }

        }
        if (chartNum === 1) {
          setRectArea(chartData.tickDate, yDomain[1], chartData.tickDateEnd, yDomain[0], "tickArea");
          var tailStart = new Date(chartData.tickDateEnd);
          tailStart = new Date(tailStart.setHours(tailStart.getHours() - 24));
          var tailEnd = new Date(chartData.tickDate);
          tailEnd = new Date(tailEnd.setHours(tailEnd.getHours() - 24));
          setRectArea(tailEnd, yDomain[1], tailStart, yDomain[0], "dayTailRect");
        }
        try {
          if (histVol && histVol.up)
            setHorizontalStrip(histVol.up, histVol.down, greenStrip);
          else if (com && com.dates)
            setHorizontalStrip(com.b, com.s, greenStrip);
          if (com2 && com2.dates[0])
            setRectArea(com2.dates[0], com2.b, com2.dates[1], com2.s, blueStrip, "black");
          if (com3 && com3.dates[0])
            setRectArea(com3.dates[0], com3.b, com3.dates[1], com3.s, redStrip, "black");
          if (com4 && com4.dates[0])
            setRectArea(com4.dates[0], com4.b, com4.dates[1], com4.s, cyanStrip, "black");
        } catch (e) {
          //log = e;
        }
        bth.forEach(function (x, i) {
          if (x.dates[0])
            setRectArea(x.dates[0], x.upDown[1], x.dates[1], x.upDown[0], beforeTradeHoursRect + i, "lightgrey");
        });
        bcl.forEach(function (x, i) {
          if (x.dates[0])
            setRectArea(x.dates[0], x.upDown[1], x.dates[1], x.upDown[0], beforeCloseHoursRect + i, "lightgrey");
        });
        afh.forEach(function (x, i) {
          if (x.dates[0])
            setRectArea(x.dates[0], yDomain[1], x.dates[1], yDomain[0], afterTradeHoursRect + i, "black", "beige");
        });
        // #region add trend corridor
        //setTrendLine(trendLines, 1, "lightgrey");
        setTrendLine2(trendLines, 2, "", "darkred", trendLines.sel);
        setTrendLine2(trendLines, 3, "", "darkred", trendLines.sel);
        //setTrendLine(trendLines, 21, "darkred");
        //setTrendLine(trendLines, 31, "darkred");

        setTrendLine2(trendLines2, 1, 2, "lightgrey");
        setTrendLine2(trendLines2, 2, 2, "navy", trendLines2.sel);
        setTrendLine2(trendLines2, 3, 2, "navy", trendLines2.sel);

        setTrendLine2(trendLines1, 2, 1, "green", trendLines1.sel);
        setTrendLine2(trendLines1, 3, 1, "green", trendLines1.sel);

        //debugger;
        setTrendLine2(trendLines0, 1, 0, "lightgrey", trendLines0.sel);
        setTrendLine2(trendLines0, 2, 0, "olive", trendLines0.sel);
        setTrendLine2(trendLines0, 3, 0, "olive", trendLines0.sel);

        var colorPlum = "darkorchid";
        setTrendLine2(trendLines3, 2, 3, colorPlum, trendLines3.sel);
        setTrendLine2(trendLines3, 3, 3, colorPlum, trendLines3.sel);
        // #endregion
      }
      // #endregion

      breakEven.concat([NaN, NaN]).forEach((be, i) => setHLine(be, "breakEven" + i, "black", 1, "2,2,5,2"));

      // #region Corridor start date line
      if (doCorridorStartDate) {
        var corridorStartTime = (trendLines.dates || [])[0];
        if (corridorStartTime) {
          setTimeLine(corridorStartTime, "corridorStart", chartData.hasStartDate ? "darkred" : "darkorange", chartData.hasStartDate ? 2 : 1);
          svg.select("path#clearStartDate")
            .attr("transform", "translate(" + x(corridorStartTime) + "," + (height - 7) + ")");
          svg.selectAll("path.nextWave")
            .data([-18, 18])
            .attr("transform", function (d) {
              return "translate(" + (x(corridorStartTime) + d) + ",7) rotate(-90)";
            })
            ;
        }
      } else
        svg.selectAll("path.nextWave").style("display", "none");
      // #endregion
      // #endregion

      setHLine((openBuy || {}).o || (openSell || {}).o, "trade", openBuy ? "darkgreen" : "red", 1, "2,2,5,2");

      // #region trade levels
      function isTradeDrag() {
        return viewModel.chartArea[chartNum].inDrag;
      }
      if (tradeLevels && !isTradeDrag()) {
        setTradeLevel(tradeLevels.buy, "buyEnter", "darkred", 1);
        setTradeLevel(tradeLevels.buyClose, "buyClose", "darkblue", openBuy ? 1 : 0, 3);
        setTradeLevel(tradeLevels.sell, "sellEnter", "darkblue", 1);
        setTradeLevel(tradeLevels.sellClose, "sellClose", "darkred", openSell ? 1 : 0, 3);

        var chkBoxData = [
          tradeLevelUIFactory(x(data[0].d) + 8, y(tradeLevels.buy) - 16, tradeLevels.canBuy, tradeLevels.manualBuy, tradeLevels.buyCount, canBuy),
          tradeLevelUIFactory(x(data[0].d) + 8, y(tradeLevels.sell), tradeLevels.canSell, tradeLevels.manualSell, tradeLevels.sellCount, canSell)];
        svg.selectAll("*.tradeLineUI")
          .data(chkBoxData)
          .attr('x', function (d) { return d.x; })
          .attr('y', function (d) { return isNaN(d.y) ? 0 : d.y; })
          ;
        svg.selectAll("*.tradeLineUI input")
          .data(chkBoxData)
          .style("display", function (d) { return d.canTrade ? "" : "none"; })
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
      }
      // #endregion

      // #region CLosed Trades
      var closedTradesPath = "closedTrades";
      function getClosedTradesDelta(data) {
        return svg.selectAll("path." + closedTradesPath).data(data, function (d) {
          return d.x;
        });
      }
      function altitudeByArea(area) { return Math.sqrt(Math.sqrt(3) * area); }
      if (closedTrades && closedTrades.length) {
        (function () {
          var ud = ["up", "down"],
            openSize = 100,
            closedSize = 60;

          function ctf(ct, time, level, u, d) {
            var isOpen = time === 0,//.match(/open$/i),
              isKindOpen = !ct.isClosed,
              paint = (isKindOpen ? openTradeGross : ct.grossPL) >= 0 ? "green" : "red";
            if (!isOpen && isKindOpen) return null;
            var upDown = (ct.isBuy ? ud[u] : ud[d]);
            return {
              x: ct.dates[time], y: ct[level],
              shape: d3.symbolTriangle,// "triangle-" + upDown,
              rotate: upDown === "up" ? "0" : "180",
              fill: !isOpen ? "white" : "white",
              stroke: paint,
              strokeWidth: !isOpen ? 3 : 2,
              size: isOpen ? openSize : closedSize,
              dateMin: data[0].d,
              offsetSign: upDown === "up" ? 1 : -1
            };
          }
          function map(ct) {
            var timeOpen = 0, timeClose = 1;
            return [ctf(ct, timeOpen, "open", 0, 1), ctf(ct, timeClose, "close", 1, 0)];
          }
          var cts = Enumerable.from(closedTrades)
            //.where("ct => !ct.isClosed")
            .selectMany(map)
            .where("ct => ct != null && ct.x > ct.dateMin")
            .toArray();
          var closedTradesDelta = getClosedTradesDelta(cts);
          closedTradesDelta.exit().remove();
          var openAltitude = altitudeByArea(openSize);
          closedTradesDelta
            .enter()
            .append("path")
            .attr("class", closedTradesPath)
            .attr("d", function (d) {
              return d3.symbol().type(d.shape).size(d.size)();
            })
            .style("fill", function (ct) { return ct.fill; })
            .style("stroke", function (ct) { return ct.stroke; })
            .style("stroke-width", function (ct) { return ct.strokeWidth; })
            .attr("transform", function (d) {
              var t = "translate(" + x(d.x) + "," + (y(d.y) + d.offsetSign * (openAltitude / 2 + d.strokeWidth)) + ") rotate(" + d.rotate + ")";
              //console.log(t);
              return t;
            })
            ;
          closedTradesDelta
            .style("stroke", function (ct) {
              return ct.stroke;
            })
            .attr("transform", function (d) {
              var t = "translate(" + x(d.x) + "," + (y(d.y) + d.offsetSign * (openAltitude / 2 + d.strokeWidth)) + ") rotate(" + d.rotate + ")";
              //console.log(t);
              return t;
            });

        })()
      }
      else getClosedTradesDelta([]).exit().remove();
      // #endregion

      //#region waveLines
      var waveLines = chartData.waveLines || [];
      var waveLineColor = "gray";
      var waveLineColorOk = "limegreen";
      var waveLineWidth = 1;
      var wlDelta = svg.selectAll("line.waveLine").data(waveLines);
      function waveLineDate(i) {
        return function (d) {
          return x(d.dates[i]);
        };
      }
      function waveLineISept(i) {
        return function (d) {
          return y(d.isept[i]);
        };
      }
      wlDelta.enter()
        .append("line")
        .attr("class", "waveLine");
      wlDelta
        .style("stroke-width", function (d) {
          return d.bold ? 2 : 1;
        })
        .attr("x1", waveLineDate(0))
        .attr("y1", waveLineISept(0))
        .attr("x2", waveLineDate(1))
        .attr("y2", waveLineISept(1))
        .style("stroke", function (w) {
          return w.color ? waveLineColorOk : waveLineColor;
        });
      wlDelta.exit().remove();
      //#endregion

      //d3.select(element).select("svg")
      //  .style('background-color', isTradingActive ? "whitesmoke" : "peachpuff");

      // #region Locals
      function setHLine(level, levelName, levelColour, width, dasharray, yTrans) {
        var line = svg.select("line.line" + levelName);
        if (level !== null && !isNaN(level))
          return line
            .style("stroke", levelColour)  // colour the line
            .style("stroke-width", width)  // colour the line
            .style("stroke-dasharray", dasharray)  // colour the line
            .attr("x1", x(data[0].d) - xAxisOffset) // x position of the first end of the line
            .attr("y1", (yTrans || y)(level)) // y position of the first end of the line
            .attr("x2", x(data[data.length - 1].d) + xAxisOffset) // x position of the second end of the line
            .attr("y2", (yTrans || y)(level))// y position of the second end of the line
            ;

        //.duration(animationDuration);    
        else
          return line
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
        var line = svg.select("line.line" + levelName);
        if (dates && level) {
          line
            .style("display", "")
            //.style("stroke", lineColour)  // colour the line
            //.style("stroke-width", strokeWidth)  // colour the line
            //.style("stroke-dasharray", strokeDashArray)  // colour the line
            .attr("x1", x(dates[0]) - xAxisOffset) // x position of the first end of the line
            .attr("y1", y(level)) // y position of the first end of the line
            .attr("x2", x(dates[1]) + xAxisOffset) // x position of the second end of the line
            .attr("y2", y(level));// y position of the second end of the line
          if (strokeWidth !== undefined)
            line.style("stroke-width", strokeWidth);
          //.duration(animationDuration);    
        } else
          line.style("display", "none");
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
      function selectRect(rectName) {
        return svg.select("rect." + rectName);
      }
      function setRectArea(date1, level1, date2, level2, rectName, borderColour, backgoundColor) {
        var rect = svg.select("rect." + rectName)
        if (rect.empty()) rect = addRectGlobal(svg, rectName, backgoundColor || "cyan");
        rect
          .style("stroke", borderColour)  // colour the line
          .attr("x", x(date1)) // x position of the first end of the line
          .attr("y", y(level1)) // y position of the first end of the line
          .attr("width", Math.max(0, x(date2) - x(date1))) // x position of the second end of the line
          .attr("height", y(level2) - y(level1));// y position of the second end of the line
      }
      function setHorizontalStrip(level1, level2, rectName) {
        var dates = [data[0].d, data[data.length - 1].d];
        var bottom = Math.min(y(level1), y(level2));
        var height = Math.abs(y(level1) - y(level2));
        if (isNaN(bottom) || isNaN(height)) return selectRect(rectName).style("display", "none");
        selectRect(rectName)
          //.style("stroke", rectColour)  // colour the line
          .style("display", "")
          .attr("x", x(dates[0])) // x position of the first end of the line
          .attr("y", bottom) // y position of the first end of the line
          .attr("width", x(dates[1]) - x(dates[0])) // x position of the second end of the line
          .attr("height", height);// y position of the second end of the line
      }
      function setTrendLine2(trendLines, lineNumber, trendIndex, lineColour, isBold) {
        var svgLine = svg.select("line.line" + lineNumber + "_" + trendIndex);
        var dates = (trendLines || {}).dates;
        var line = trendLines ? trendLines["close" + lineNumber] : null;
        if (dates && dates.length && line && line.length && !line.some(function (v) { return isNaN(v); })) {
          svgLine
            .style("visibility", "visible")
            .style("stroke-width", isBold ? 3 : 1.5)
            .style("stroke", lineColour)  // colour the line
            .attr("x1", x(dates[0])) // x position of the first end of the line
            .attr("y1", y(line[0])) // y position of the first end of the line
            .attr("x2", x(dates[1])) // x position of the second end of the line
            .attr("y2", y(line[1]));// y position of the second end of the line
          //.duration(animationDuration);    
        } else
          svgLine.style("visibility", "hidden");
      }
      // #endregion
    }
  };
  /// #region Global locals
  function addRectGlobal(svg, name, color, opacity) {
    return svg.insert("rect", ":first-child")
      .attr("class", name)
      .style("fill", color)  // colour the line
      .style("opacity", opacity || 0.25)
      .attr("x", 0) // x position of the first end of the line
      .attr("y", 0) // y position of the first end of the line
      .attr("width", 10) // x position of the second end of the line
      .attr("height", 50);// y position of the second end of the line
  }

  /*jshint unused:false*/
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