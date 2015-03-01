/*global ko, d3*/

ko.bindingHandlers.barChart = {
    init: function(element, valueAccessor) {
        "use strict";

        var margin = {top: 20, right: 20, bottom: 30, left: 40},
            elementWidth = parseInt(d3.select(element).style("width"), 10),
            elementHeight = parseInt(d3.select(element).style("height"), 10),
            width = elementWidth - margin.left - margin.right,
            height = elementHeight - margin.top - margin.bottom,

            data = ko.unwrap(valueAccessor()),

            svg = d3.select(element)
                .append("svg")
                .attr("width", width + margin.left + margin.right)
                .attr("height", height + margin.top + margin.bottom)
                .append("g")
                .attr("transform", "translate(" + margin.left + "," + margin.top + ")");

        svg.append("g")
            .attr("class", "x axis")
            .attr("transform", "translate(0," + height + ")");

        svg.append("g")
            .attr("class", "y axis");

        svg.selectAll(".bar")
            .data(data)
            .enter().append("rect")
            .attr("class", "bar");
    },
    update: function(element, valueAccessor) {
        "use strict";

        var margin = {top: 20, right: 20, bottom: 30, left: 40},

            elementWidth = parseInt(d3.select(element).style("width"), 10),
            elementHeight = parseInt(d3.select(element).style("height"), 10),

            width = elementWidth - margin.left - margin.right,
            height = elementHeight - margin.top - margin.bottom,
            animationDuration = 750,

            // parsin the data from the data-view-model
            data = ko.unwrap(valueAccessor()),

            svg = d3.select(element).select("svg g"),

        // set the range of the x axis.
            x = d3.scale.ordinal()
                .rangeRoundBands([0, width], 0.1),

        // set the range of the y axis.
            y = d3.scale.linear()
                .range([height, 0]),

            // scale the x axis with the width of the canvas
            xAxis = d3.svg.axis()
                .scale(x)
                .orient("bottom"),

            // scale the y axis with the height of the canvas
            yAxis = d3.svg.axis()
                .scale(y)
                .orient("left")
                .ticks(10, "%");

        // setting the domain
        x.domain(data.map(function(d) { return d.name; }));
        y.domain([0, d3.max(data, function(d) { return d.value; })]);

        // drawing the x axis to the canvas
        svg.select("g.x.axis")
            .transition()
            .duration(animationDuration)
            .call(xAxis);

        // drawing the y axis to the canvas
        svg.select("g.y.axis")
            .transition()
            .duration(animationDuration)
            .call(yAxis);

        // drawing the bar rectangles to the canvas.
        svg.selectAll("rect.bar")
            .data(data)
            .transition()
            .duration(animationDuration)
            .attr("x", function(d) { return x(d.name); })
            .attr("width", x.rangeBand())
            .attr("y", function(d) { return y(d.value); })
            .attr("height", function(d) { return height - y(d.value); });
    }
};