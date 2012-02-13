/// <reference path="jquery.js" />
jQuery.extend({
  propsToArray: function (o) {
    return jQuery.map(o, function (v, n) { return n; });
  }
});
jQuery.extend({
  Linq: {
    select: function (array, prop) {
      return jQuery.map(array, function (v, n) { return v[prop]; });
    }
  }
});
jQuery.extend({
  AJAX: {
    isMSDate: function (v) { return typeof v == "string" && v.indexOf("/Date(") == 0; },
    convertMSDate: function (v, doTest) {
      if (doTest && !jQuery.AJAX.isMSDate(v))
        return v;
      return eval("new " + v.slice(1, -1));
    },
    processResult: function (result) {
      $.each(result, function (n, v) {
        if ($.AJAX.isMSDate(v))
          result[n] = $.AJAX.convertMSDate(v);
      });
      return result;
    },
    processResults: function (results) {
      $.each(results, function (i, v) { $.AJAX.processResult(v); });
      return results;
    },
    processRequest: function (request) {
      $.each(request, function (n, v) {
        if (v instanceof Date)
          request[n] = v.toString("MM/dd/yyyy HH:mm:ss")
        if ($.isFunction(v) && v() instanceof Date)
          request[n] = function () { return v().toString("MM/dd/yyyy HH:mm:ss"); }
      });
      return request;
    }
  }
});
