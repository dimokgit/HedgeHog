/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
/// <reference path="MicrosoftAjax.js" />

jQuery.extend({
  D: {
    inRange: function (f, min, max) { return f < min ? min : f > max ? max : f; },
    round: function (f, d) { return d >= 0 ? parseFloat(f.toFixed(d)) : f; },
    gradient: function (reds, greens, blues, min, max, steps, decimals) {
      var r = [reds[0], reds[1]];
      var g = [greens[0], greens[1]];
      var b = [blues[0], blues[1]];
      var r1 = [r[1], reds[2]];
      var g1 = [g[1], greens[2]];
      var b1 = [b[1], blues[2]];
      var step = (max - min) / (steps-1);
      function RGBs(r, g, b, min, max, steps) {
        var rgb = [r, g, b];
        var rgbs = [];
        for (var i = 0; i <= steps; i++) {
          var v = min + step * i;
          var p = (v - min) / (max - min);
          var _rgb = [];
          $.each(rgb, function (i, v) {
            var c = v[1] == 0 && v[0] == 0 ? 0 : p * (v[1] - v[0]) + v[0];
            _rgb.push(Math.round(c));
          });
          rgbs.push({ value: $.D.round(v, decimals), rgb: _rgb });
        }
        return rgbs;
      }
      var middle = min + (max - min) / 2;
      var rgbs = RGBs(r, g, b, min, middle, steps / 2);
      var rgbs1 = RGBs(r1, g1, b1, middle, max, steps / 2);
      return rgbs.concat(rgbs1);
    },
    props: function (o) {
      $.each($.makeArray(arguments).slice(1), function (i, p) {
        p = p.toLowerCase();
        for (var n in o)
          if (n.toLowerCase() == p)
            o = ko.utils.unwrapObservable(o[n]);
        if (o === undefined) return;
      });
      return o;
    },
    prop: function (o, p) {
      p = p.toLowerCase();
      for (var n in o)
        if (n.toLowerCase() == p)
          return o[n];
    },
    sure: function (o, p) {
      return o[$.D.name(o, p)];
    },
    name: function (o, p) {
      if (!o)
        throw new Error("Object is empty");
      p = p.toLowerCase();
      for (var n in o)
        if (n.toLowerCase() == p)
          return n;
      throw new Error("Property [" + p + "] not found in " + (JSON.stringify ? JSON.stringify(o) : o));
    }
  },
  propsToArray: function (o) {
    return jQuery.map(o, function (v, n) { return n; });
  }
});