/*global ko, d3*/

ko.bindingHandlers.lineChart = {
  init: function (element) {
    "use strict";

    var margin = { top: 0, right: 10, bottom: 20, left: 0 },
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
    svg.append("path")
        .attr("class", "line data");

    svg.append("line")
      .style("stroke", "black")  // colour the line
        .attr("class", "line1")
    .attr("x1", 0)     // x position of the first end of the line
      .attr("y1", 0)      // y position of the first end of the line
      .attr("x2", 10)     // x position of the second end of the line
      .attr("y2", 1);    // y position of the second end of the line
  },
  update: function (element, valueAccessor) {
    "use strict";

    var margin = { top: 0, right: 10, bottom: 20, left: 0 },
        elementWidth = parseInt(d3.select(element).style("width"), 10),
        elementHeight = parseInt(d3.select(element).style("height"), 10),
        width = elementWidth - margin.left - margin.right,
        height = elementHeight - margin.top - margin.bottom,

    // set the time it takes for the animation to take.
        animationDuration = 0,

        x = d3.time.scale()
            .range([0, width]),

        y = d3.scale.linear()
            .range([height, 0]),

        xAxis = d3.svg.axis()
            .scale(x)
            .orient("bottom"),
        yAxis = d3.svg.axis()
            .scale(y)
            .orient("left"),
        // define the graph line
        line = d3.svg.line()
            .x(function (d) { return x(d.date); })
            .y(function (d) { return y(d.close); }),

        svg = d3.select(element).select("svg g"),

        // parse data from the data-view-model
        data = ko.unwrap(valueAccessor());

    // define the domain of the graph. max and min of the dimensions
    var dateLeft = d3.min(data, function (d) { return d.date; });
    var valueLeft = d3.min(data, function (d) { return d.close; });
    var dateRight = d3.max(data, function (d) { return d.date; });
    var valueRight = d3.max(data, function (d) { return d.close; });

    x.domain(d3.extent(data, function (d) { return d.date; }));
    y.domain(d3.extent(data, function (d) { return d.close; }));

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

    // add the line to the canvas
    svg.select("path.line.data")
        .datum(data)
        .transition()
        .duration(animationDuration)
        .attr("d", line);
    // add line

    //if(data.length == 10)
    svg.select("line.line1")
      .style("stroke", "black")  // colour the line
      .attr("x1", x(data[0].date))     // x position of the first end of the line
      .attr("y1", y(data[0].close))      // y position of the first end of the line
      .attr("x2", x(data[data.length - 1].date))     // x position of the second end of the line
      .attr("y2", y(data[data.length - 1].close));
      //.duration(animationDuration);    // y position of the second end of the line
  }
};
