/// <reference path="knockout.js" />
/// <reference path="jquery.js" />
/// <reference path="StopWatch.js" />
$.extend(ko.utils, {
  setObservableOrNotValue: function (data, property, value) {
    if (ko.isObservable(data[property]))
      data[property](value);
    else
      data[property] = value;
  }
});
var dbc = true;
ko.bindingHandlers.textv = {
  update: function (element, valueAccessor, allBindingsAccessor, data, bindingContext) {
//    ko.utils.setTextContent(element, ko.toJSON(true));
//    return;
    var value = ko.utils.unwrapObservable(valueAccessor());
    var option = allBindingsAccessor();
    var parentArray = bindingContext.$parent[value.array];
    var scrollParent = $(element).scrollParent();
    var info = {
      divHeight: scrollParent.height(),
      divScrollTop: bindingContext.$root.scrollPosition(),
      tableHeight: $(element).parents("TABLE:first").height(),
      tdTop: $(element).position().top
    };
    if (!parentArray.fop() && info.tdTop > 0)
      parentArray.fop(data);
    if (!parentArray.lop() && info.tdTop > info.divHeight)
      parentArray.lop(data);
    if (info.tdTop > 800 && dbc) {
      debugger;
      dbc = false;
    }
    ko.utils.setTextContent(element, ko.toJSON(info));
  }
};

// "if: someExpression" is equivalent to "template: { if: someExpression }"
ko.bindingHandlers['ifvisible'] = {
  makeTemplateValueAccessor: function (valueAccessor) {
    return function () { return { 'if': valueAccessor(), 'templateEngine': ko.nativeTemplateEngine.instance} };
  },
  'init': function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
    bindingContext.$root.stopWatch.start();
    try {
      var parent = $(element).parent();
      var parentTop = parent.position().top;
      var scrollParent = bindingContext.$root.scrollParent || parent.scrollParent();
      var va = scrollParent && parentTop > 0 && parentTop < scrollParent.height() ? function () { return true } : function () { return false }
      return ko.bindingHandlers['template']['init'](element, ko.bindingHandlers['if'].makeTemplateValueAccessor(va));
    } finally {
      bindingContext.$root.stopWatch.stop();
    }
  },
  'update': function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
    bindingContext.$root.stopWatch.start();
    try {
      var parent = $(element).parent();
      var parentTop = parent.position().top;
      var scrollParent = bindingContext.$root.scrollParent || parent.scrollParent();
      var va = !scrollParent || parentTop < 0 || parentTop > scrollParent.height() ? function () { return false } : function () { return true }
      return ko.bindingHandlers['template']['update'](element, ko.bindingHandlers['if'].makeTemplateValueAccessor(va), allBindingsAccessor, viewModel, bindingContext);
    } finally {
      bindingContext.$root.stopWatch.stop();
    }
  }
};
ko.jsonExpressionRewriting.bindingRewriteValidators['ifvisible'] = false; // Can't rewrite control flow bindings
ko.virtualElements.allowedBindings['ifvisible'] = true;

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
