/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
/// <reference path="MicrosoftAjax.js" />

(
(function () {
  jQuery.extend({
    AJAX: {
      dateToString:function(d){
        return d instanceof Date ?d.toString("MM/dd/yyyy HH:mm:ss"):d;
      },
      stringToDate:function(s){
        var d = typeof s == "string" && s.match(/^\s*\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}/)?new Date(s):undefined;
        return isNaN(d)?s:d;
      },
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
}
))()