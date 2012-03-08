/// <reference path="knockout.js" />
/// <reference path="jquery.js" />
/// <reference path="StopWatch.js" />
(function () { } (
      function makeComputed(name, context) {
        return function () {
          return ko.computed({
            read: function () {
              var items = this();
              if (!ko.isObservable(items[name]))
                items[name] = ko.observable();
              return items[name]();
            },
            write: function (item) {
              var items = this();
              items[name](item)
            },
            owner: this
          });
        }
      }
//      ko.observableArray.fn.firstOnPage = makeComputed("_first");
//      ko.observableArray.fn.lastOnPage = makeComputed("_last");

));
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
var __f__ = function () {
  function __true() { return true };
  function __false() { return false };

  ko.bindingHandlers['ifvisible'] = {
    makeTemplateValueAccessor: function (valueAccessor) {
      return function () { return { 'if': valueAccessor(), 'templateEngine': ko.nativeTemplateEngine.instance} };
    },
    'init': function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
      bindingContext.$root.stopWatch.start();
      try {
        if (!ko.isObservable(viewModel.__visible))
          viewModel.__visible = ko.observable();
        viewModel.__visible();
        return ko.bindingHandlers['template']['init'](element, ko.bindingHandlers['if'].makeTemplateValueAccessor(function () { true; }));
      } finally {
        bindingContext.$root.stopWatch.stop();
      }
    },
    'update': function (element, valueAccessor, allBindingsAccessor, viewModel, bindingContext) {
      try {
        // must be called to make dependency on scrolling
        var options = allBindingsAccessor();
        var items = bindingContext.$root[options.ifvisible.array];
        var scrolTop = items.scrollPosition();
        bindingContext.$root.stopWatch.start();
        var item = viewModel;
        var firstPass = false;
        if (item.__visible()) {
          setTimeout(function () { ko.cleanNode(element); }, 0);
          return;
        }
        if (items.stopProcess) { setTimeout(function () { items.stopProcess = false }, 100); return; }
        if (dbc) {
          dbc = false;
        }
        var parent = $(element).parent();
        var parentTop = parent.position().top;
        var scrollParent = bindingContext.$root.scrollParent || parent.scrollParent();
        var scrollParentHeight = scrollParent.height();
        var va = scrollParent && parentTop > scrollParentHeight * 2 ? __false : __true;
        if (!va()) {
          va = __true;
          items.stopProcess = true;
        }
        item.__visible(va());
        return ko.bindingHandlers['template']['update'](element, ko.bindingHandlers['if'].makeTemplateValueAccessor(va), allBindingsAccessor, viewModel, bindingContext);
      } finally {
        bindingContext.$root.stopWatch.stop();
      }
    }
  }
} ();
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
