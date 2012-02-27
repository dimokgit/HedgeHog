/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
/// <reference path="MicrosoftAjax.js" />

jQuery.extend({
  Linq: {
    select: function (array, prop) {
      return jQuery.map(array, function (v, n) { return v[prop]; });
    }
  }
});
