/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
/// <reference path="MicrosoftAjax.js" />
(function () {
  var DATA_BIND = "data-bind";
  jQuery.fn.extend({
    dataBindAttr: function (attrs) {
      if (arguments.length == 0)
        return this.eq(0).attr(DATA_BIND);
      var attr = $.isArray(attrs) ? attrs : $.map($.makeArray(arguments), function (v) { return v ? v : null; }).join(",");
      this.eq(0).attr(DATA_BIND, attr);
      return this;
    },
    Enter2Tab: function () {
      if ($.browser.msie)
        $.each(this, function () {
          $(this).keydown(function (a, b) {
            if (event.keyCode == 13) { 
            event.keyCode = 9; }
          });
        });
    }
  });
}
)();