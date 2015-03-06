<!DOCTYPE html>
<html>
<head>
  <title>d3-knockout-demo</title>
  <meta http-equiv="content-type" content="text/html;charset=utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.2/css/bootstrap.min.css">
  <link rel="stylesheet" href="d3-knockout/style/bar-chart-demo.css">

  <script src="https://code.jquery.com/jquery-2.1.3.min.js"></script>
  <script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.2/js/bootstrap.min.js"></script>
  <script type='text/javascript' src="http://d3js.org/d3.v3.min.js" charset="utf-8"></script>
  <script src="https://cdnjs.cloudflare.com/ajax/libs/knockout/3.3.0/knockout-min.js"></script>
  <script src="Scripts/jquery.signalR-2.1.2.min.js"></script>
  <style type="text/css">
    body, html {
      padding: 0;
      margin: 0;
      height: 100%;
      overflow: hidden;
    }

    .container {
      /*background-color: #99CCFF;*/
      padding: 0;
      margin: 0;
    }

    ul, li {
      padding: 0;
      margin: 0;
    }

    .chart {
      position: absolute;
      top: 3.5em;
      left: 0;
      bottom: 0;
      right: 0;
    }

    #profit {
      display: inline-block;
    }

    .profit {
      background-color: chartreuse;
      font-weight: bold;
    }

    .loss {
      background-color: lightpink;
      font-weight: bold;
    }

    /* SQUARED FOUR */
    .squaredFour {
      width: 20px;
      margin: 20px auto;
      position: relative;
    }

      .squaredFour label {
        cursor: pointer;
        position: absolute;
        width: 20px;
        height: 20px;
        top: 0;
        border-radius: 4px;
        -webkit-box-shadow: inset 0px 1px 1px white, 0px 1px 3px rgba(0,0,0,0.5);
        -moz-box-shadow: inset 0px 1px 1px white, 0px 1px 3px rgba(0,0,0,0.5);
        box-shadow: inset 0px 1px 1px white, 0px 1px 3px rgba(0,0,0,0.5);
        background: #fcfff4;
        background: -webkit-linear-gradient(top, #fcfff4 0%, #dfe5d7 40%, #b3bead 100%);
        background: -moz-linear-gradient(top, #fcfff4 0%, #dfe5d7 40%, #b3bead 100%);
        background: -o-linear-gradient(top, #fcfff4 0%, #dfe5d7 40%, #b3bead 100%);
        background: -ms-linear-gradient(top, #fcfff4 0%, #dfe5d7 40%, #b3bead 100%);
        background: linear-gradient(top, #fcfff4 0%, #dfe5d7 40%, #b3bead 100%);
        filter: progid:DXImageTransform.Microsoft.gradient( startColorstr='#fcfff4', endColorstr='#b3bead',GradientType=0 );
      }

        .squaredFour label:after {
          -ms-filter: "progid:DXImageTransform.Microsoft.Alpha(Opacity=0)";
          filter: alpha(opacity=0);
          opacity: 0;
          content: '';
          position: absolute;
          width: 9px;
          height: 5px;
          background: transparent;
          top: 4px;
          left: 4px;
          border: 3px solid #333;
          border-top: none;
          border-right: none;
          -webkit-transform: rotate(-45deg);
          -moz-transform: rotate(-45deg);
          -o-transform: rotate(-45deg);
          -ms-transform: rotate(-45deg);
          transform: rotate(-45deg);
        }

        .squaredFour label:hover::after {
          -ms-filter: "progid:DXImageTransform.Microsoft.Alpha(Opacity=30)";
          filter: alpha(opacity=30);
          opacity: 0.5;
        }

      .squaredFour input[type=checkbox]:checked + label:after {
        -ms-filter: "progid:DXImageTransform.Microsoft.Alpha(Opacity=100)";
        filter: alpha(opacity=100);
        opacity: 1;
      }
  </style>
</head>

<body>
  <div class="masthead">
    <nav>
      <ul class="nav nav-tabs">
        <li><input type="button" value="Stop" id="stopTrades" /></li>
        <li><button id="startBuy"><span class="glyphicon glyphicon-circle-arrow-up"></span>Start</button></li>
        <li><button id="startSell"><span class="glyphicon glyphicon-circle-arrow-down"></span>Start</button></li>
        <li><input type="button" value=" Close " id="closeTrades" /></li>
        <li><button id="buyDown">B<span class="glyphicon glyphicon-save"></span></button></li>
        <li><button id="buyUp">B<span class="glyphicon glyphicon-open"></span></button></li>
        <li><button id="sellDown">S<span class="glyphicon glyphicon-save"></span></button></li>
        <li><button id="sellUp">S<span class="glyphicon glyphicon-open"></span></button></li>
        <!--<li>
          <select id="tradeCounts">
            <option value="0" selected="selected">0</option>
            <option value="1">1</option>
            <option value="2">2</option>
          </select>
          <input type="button" value="TC" title="Set Trade Count" id="tradeCount" />
        </li>-->
        <li>
          <select data-bind="value:chartNum">
            <option selected="selected" value="0">M</option>
            <option value="1">S</option>
          </select>
        </li>
        <li role="presentation" class="dropdown">
          <button class="dropdown-toggle" data-toggle="dropdown" role="button" aria-expanded="false">...</button>
          <ul class="dropdown-menu" role="menu">
            <li><input type="button" value="Manual" id="manualToggle" /></li>
            <li><button id="toggleStartDate">StartDate</button></li>
            <li><button id="toggleIsActive">Active</button></li>
            <li><button id="flipTradeLevels">Flip</button></li>
            <li><button id="setDefaultTradeLevels">Default</button></li>
            <li><button id="alwaysOn">AlwaysOn</button></li>
            <li><button id="buySellInit">WrapTrade</button></li>
            <li>
              <input type="button" value=" Buy " id="buy" />
              <input type="button" value=" Sell " id="sell" />
            </li>
          </ul>
        </li>
      </ul>
    </nav>
  </div>
  <div class="container container-fluid">
    <span id="profit" data-bind="text:profit,css:{profit:profit()>0,loss:profit()<0}"></span>
    <span id="discussion"></span>
    <input value="" id="rsdMin" style="width:1.5em" />
  </div>
  <div class="chart" style="" data-bind="lineChart: chartData"></div>
  <script type='text/javascript' src='d3-knockout/view-models/line-data-view-model.js'></script>
  <script type='text/javascript' src='d3-knockout/custom-binding-handlers/trend-line-chart-binding.js'></script>
  <script type='text/javascript' src='d3-knockout/app.js'></script>
</body>
</html>