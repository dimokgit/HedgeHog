/// <reference path="knockout.js" />
$.extend(ko.utils, {
  setObservableOrNotValue: function (data, property, value) {
    if (ko.isObservable(data[property]))
      data[property](value);
    else
      data[property] = value;
  }
});

ko.bindingHandlers.toggle = {
  init: function (element, valueAccessor, allBindingsAccessor) {
    $("CAPTION", element).click(function () {
      $(this).parent().children().not("CAPTION").toggle();
    });
  }
};

ko.bindingHandlers.datepicker = {
  init: function (element, valueAccessor, allBindingsAccessor) {
    //initialize datepicker with some optional options 
    var options = allBindingsAccessor().datepickerOptions || {};
    $(element).datepicker(options);

    //handle the field changing 
    ko.utils.registerEventHandler(element, "change", function () {
      var observable = valueAccessor();
      observable($(element).datepicker("getDate"));
    });

    //handle disposal (if KO removes by the template binding) 
    ko.utils.domNodeDisposal.addDisposeCallback(element, function () {
      $(element).datepicker("destroy");
    });

  },
  update: function (element, valueAccessor) {
    var value = ko.utils.unwrapObservable(valueAccessor()),
            current = $(element).datepicker("getDate");

    if (value - current !== 0) {
      $(element).datepicker("setDate", value);
    }
  }
};

ko.bindingHandlers.datetimepicker = {
  init: function (element, valueAccessor, allBindingsAccessor) {
    //initialize datepicker with some optional options 
    var options = allBindingsAccessor().datetimepickerOptions || {};
    $(element).datetimepicker(options);

    //handle the field changing 
    ko.utils.registerEventHandler(element, "change", function () {
      var observable = valueAccessor();
      observable($(element).datetimepicker("getDate"));
    });

    //handle disposal (if KO removes by the template binding) 
    ko.utils.domNodeDisposal.addDisposeCallback(element, function () {
      $(element).datetimepicker("destroy");
    });

  },
  update: function (element, valueAccessor) {
    var value = ko.utils.unwrapObservable(valueAccessor()),
            current = $(element).datetimepicker("getDate");
    if ($.isFunction(value)) value = value();
    if (value - current !== 0) {
      $(element).datetimepicker("setDate", value);
    }
  }
}; 
