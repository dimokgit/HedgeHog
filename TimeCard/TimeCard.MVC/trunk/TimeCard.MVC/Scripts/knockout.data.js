/// <reference path="jquery.js" />
/// <reference path="jquery.extentions.js" />
/// <reference path="knockout.js" />
/// <reference path="knockout.mapping-latest.debug.js" />
/// <reference path="MicrosoftAjax.debug.js" />

if (!ko.data) {
  Namespace("ko.data", {
    MvcCrud: {
      formatters: {
        text: function (data) { return data; },
        date: function (data, format) { return data instanceof Date ? data.toString(format || "MM/dd/yyyy HH:mm") : data; }
      },
      initCrud: function () {
        var vm = this;
        $.each(vm.crud, function () {
          makeAddHandler(this);
          makeDeleteHandler(this);
        });
        function makeAddHandler(property) {
          vm[property + "Add"] = $.proxy(function (data) { this.AddData(property) }, vm);
        }
        function makeDeleteHandler(property) {
          vm[property + "Delete"] = $.proxy(function (data) { this.DeleteData(data, property) }, vm);
        }
      },
      GetData: function (propertyName, table, success) {
        var vm = this;
        return $.ajax({
          url: this.homePath + propertyName + "Get",
          type: "GET",
          success: function (result) {
            if (table)
              vm.bindTable(table, $.AJAX.processResults(result), vm, propertyName);
            else {
              vm[propertyName].removeAll();
              $.each(result, function (i, v) { vm[propertyName].push(v); });
            }
            if (success) success(result);
          },
          error: function (result) { vm.showAjaxError(result); }
        });
      },
      DeleteData: function (data, property) {
        var vm = this;
        $.ajax({
          url: this.homePath + property + "Delete",
          type: "POST",
          data: $.AJAX.processRequest(data),
          success: function (result) {
            vm[property].remove(data);
          },
          error: function (result) { vm.showAjaxError(result); }
        });
      },
      AddData: function (property) {
        var vm = this;
        $.ajax({
          url: this.homePath + property + "Add",
          type: "POST",
          data: $.AJAX.processRequest(this[property + "New"]),
          success: function (result) {
            vm[property].push($.AJAX.processResult(result));
          },
          error: function (result) { vm.showAjaxError(result); }
        });
      },
      showAjaxError: function (response) {
        alert($(response.responseText)[1].text.replace(/<br>/gi, "\n"));
      },
      UpdateData: function (property, dataNew, onSuccess, onError, async) {
        var data = {};
        data[property] = dataNew;
        var json = JSON.stringify(ko.mapping.toJS(data));
        $.ajax({
          url: this.homePath + property + "Update",
          type: "POST",
          async: async !== undefined ? async : false,
          contentType: 'application/json; charset=utf-8',
          data: json,
          processData: false,
          success: function (result, status, response) {
            result = Sys.Serialization.JavaScriptSerializer.deserialize(response.responseText);
            if (onSuccess && onSuccess(result)) return;
            $.each(result, function (n, v) {
              if (dataNew.hasOwnProperty(n)) {
                if (ko.isObservable(dataNew[n]))
                  dataNew[n](v);
                else {
                  dataNew[n] = v;
                }
              }
            });
          },
          error: function (result) {
            if (onError && onError(result)) return;
            ko.data.MvcCrud.showAjaxError(result);
          }
        });
      },

      bindTable: function (table, data, vm, vmProperty, container, options) {
        table = $(table);
        if (!table.length) {
          alert(table.selector + " is not found.");
          return;
        }
        var dataBind = "data-bind";
        vm[vmProperty] = ko.observableArray(data);
        vm[vmProperty + "New"] = {};
        var propPrev = "", stop = false;
        vm[vmProperty + "Columns"] = ko.observableArray($.map(data[0], function (e, n) {
          if (n == "C_") stop = true;
          if (stop) return;
          if (propPrev + "Id" != n) {
            propPrev = n;
            vm[vmProperty + "New"][n] = ko.observable("");
            return n;
          } else if (propPrev) {
            var p = vm[vmProperty + "New"][propPrev];
            vm[vmProperty + "New"][n] = ko.computed(function () {
              return p().Id;
            }, vm);
          }
        }));
        ko.data.dataBind(vm, table, vmProperty, vmProperty + "Columns");
      }
    },
    dataBind: function (vm, node, rows, cols) {
      var DATA_BIND = "data-bind";
      cols = cols || vm[rows + "Columns"];
      if (!cols)
        vm[cols = rows + "Cols"] = $.map(ko.utils.unwrapObservable(vm[rows])[0], function (v, n) { return n; });
      node = $(node);
      var table = node.is("TABLE") ? node : $("TABLE", node);
      table.not(":has(CAPTION)").prepend('<caption>' + rows + '</caption>');
      var itemNew = rows + "New";
      var addItem = rows + "Add";
      var deleteItem = rows + "Delete";
      replaceAttr(node, "columns", cols);
      replaceAttr(node, "rows", rows);
      replaceAttr(node, "new", itemNew);
      replaceAttr(node, "add", addItem);
      replaceAttr(node, "delete", deleteItem);

      ko.applyBindings(vm, node[0]);
      ko.cleanNode(node[0]);

      mash("THEAD"); mash("TBODY"); mash("TFOOT");
      ko.applyBindings(vm, node[0]);

      function mash(parent) {
        var trs = $(parent + ' TR', node);
        trs.slice(1).each(function () {
          $(this).children().each(function () {
            trs.eq(0).append(this);
          });
          var dba = $(this).attr(DATA_BIND);
          if (dba)
            trs.eq(0).attr(DATA_BIND, trs.eq(0).attr(DATA_BIND) + "," + dba);
          $(this).remove();
        });
      }
      function replaceAttr(node, replace, value, attr) {
        attr = attr || DATA_BIND;
        replace = "{{" + replace + "}}";
        $('[' + attr + '*="' + replace + '"]', node).each(function () {
          $(this).attr(attr, $(this).attr(attr).replace(replace, value));
        });
      }
      function autoTemplate(container, rows, cols) {
        var table =
      $("<table/>")
        .append($("<caption/>").text(rows))
        .append($("<thead/>")
          .append($("<tr/>").attr(DATA_BIND, "foreach:" + cols + ",attr:{'" + DATA_BIND + "':''}")
            .append($("<th/>").attr(DATA_BIND, "text:$data"))))
        .append("<tbody/>")
          .append($("<tr/>").attr(DATA_BIND, "foreach:" + cols + ",attr:{'" + DATA_BIND + "':''}")
            .append($("<td/>").attr(DATA_BIND, 'attr:{"' + DATA_BIND + '":"text:$data[\\""+$data+"\\"]"}')))
        .append($("<tfoot/>")
          .append($("<tr/>").attr(DATA_BIND, "foreach:" + cols + ",attr:{'" + DATA_BIND + "':''}")
            .append($("<td/>")
              .append($("<input/>").attr(DATA_BIND, "attr:{name:$data,type:$data=='X'?'button':'text'},value:$data=='X'?'Add':$root." + rows + "New[$data],click:$data=='X'?$root." + rows + "Add:null")))))
      .appendTo("DIV#main");
        return table;
      }
    }
  });
  ko.data.MvcCrud.showAjaxError = function (response) {
    alert($(response.responseText)[1].text.replace(/<br>/gi, "\n"));
  }
}