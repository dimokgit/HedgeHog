/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
/// <reference path="MicrosoftAjax.js" />

jQuery.extend({
  D: {
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