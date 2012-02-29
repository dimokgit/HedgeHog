/// <reference path="jquery.js" />
/// <reference path="jquery.extentions.js" />
/// <reference path="knockout.js" />
/// <reference path="knockout.mapping-latest.debug.js" />
/// <reference path="MicrosoftAjax.debug.js" />
/// <reference path="jquery.extentions.fn.js" />
/// <reference path="knockout.data.selected.js" />

Namespace("ko.data");
if (!ko.data.MvcCrud) {
  Namespace("ko.data", {

    MvcCrud: {
      test: function (name) {
        if (!this.hasOwnProperty(name)) throw new Error(name + " does not exists");
        return name;
      },
      formatters: {
        text: function (data) { return data; },
        date: function (data, format) { return data instanceof Date ? data.toString(format || "MM/dd/yyyy HH:mm") : data; }
      },
      initCrud: function () {
        var vm = this;
        $.each(vm.crud || [], function () {
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
      GetData: function (propertyName, table, data, success) {
        var vm = this;
        return $.ajax({
          url: this.homePath + propertyName + "Get",
          type: "GET",
          dataType: "text",
          data: data,
          success: function (result) {
            result = Sys.Serialization.JavaScriptSerializer.deserialize(result);
            result = result.d || result;
            if (table)
              vm.bindTable(table, result, vm, propertyName);
            else {
              vm[propertyName].removeAll();
              vm[propertyName].splice.apply(vm[propertyName], [0, 0].concat(result));
              //$.each(result, function (i, v) { vm[propertyName].push(v); });
            }
            if (success) success(result);
          },
          error: function (result) { vm.showAjaxError(result); }
        });
      },
      DeleteData: function (data, property) {
        if (!confirm("Delete:\n" + JSON.stringify(ko.toJS(data)).replace(/","/g, '",\n"'))) return;
        var vm = this;
        $.ajax({
          url: this.homePath + property + "Delete",
          type: "POST",
          data: $.AJAX.processRequest(data),
          success: function (result) {
            vm[property].remove(data);
            vm.GetData(property);
            vm.fireUpdated(property);
          },
          error: function (result) { vm.showAjaxError(result); }
        });
      },
      AddData: function (property) {
        var vm = this;
        var data = ko.mapping.toJS(this[property + "New"]);
        if (this.useUtc)
          $.each(data, function (n, v) {
            data[n] = $.AJAX.stringToDate(v);
          });
        var json = JSON.stringify(data);
        $.ajax({
          url: this.homePath + property + "Add",
          type: "POST",
          contentType: 'application/json; charset=utf-8',
          data: json,
          dataType: "text",
          success: function (result) {
            result = Sys.Serialization.JavaScriptSerializer.deserialize(result);
            result = result.d || result;
            vm[property].push(result);
            vm.GetData(property, '', null, function () {
              vm.selectRowByProp(property, function (a) { return a.Id == result.Id; });
            });
            vm.fireUpdated(property);
          },
          error: function (result) {
            vm.showAjaxError(result);
            vm.fireUpdated(property);
          }
        });
      },
      showAjaxError: function (response) {
        alert($(response.responseText)[1].text.replace(/<br>/gi, "\n"));
      },
      UpdateData: function (property, dataNew, onSuccess, onError, async) {
        var vm = this;
        var data = {};
        data[property] = ko.mapping.toJS(dataNew);
        if (!this.useUtc)
          $.each(data[property], function (n, v) {
            data[property][n] = $.AJAX.dateToString(v);
          });
        var json = JSON.stringify(data);
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
            vm.fireUpdated(property);
          },
          error: function (result) {
            vm.fireUpdated(property);
            if (onError && onError(result)) return;
            ko.data.MvcCrud.showAjaxError(result);
          }
        });
      },
      format: function (v, parentName, propName) {
        if (parentName) {
          var f = $.D.props(this, "headers", parentName, propName, "format");
          if ($.isFunction(f))
            return f(v);
        };
        if (typeof v == "string" && v.indexOf("/Date(") == 0)
          v = eval("new " + v.slice(1, -1));
        if (ko.isObservable(v))
          v = v();
        if (v instanceof Date) return v.toString("MM/dd/yyyy HH:mm");
        if (typeof v == 'number') {
          return v.format("n2");
        }
        if (typeof v == 'boolean') return v ? "<img src='/images/tick.png'/>" : "";
        return v;
      },
      fireUpdated: function (property) {
        var h = $.D.prop(this, "on" + property + "Updated");
        if (ko.isObservable(h))
          h.valueHasMutated();
      },
      update: function (data, ev, dataProperty, observables) {
        if (ev.ctrlKey || ev.altKey || ko.isObservable(this.selectedRows) && !this.isSelected(data, observables[0])) return;
        var model = this;
        var dataPropertyOriginal = dataProperty;
        if (data.hasOwnProperty(dataProperty + "Id")) {
          dataPropertyOriginal = dataProperty;
          dataProperty += "Id";
        }

        var el = ev.srcElement;
        if (!$(el).is("TD")) {
          el = $(el).parent()[0];
          if (!$(el).is("TD")) return;
        }
        var footTD = $(el).parents("TABLE:first").find("TFOOT TR TD:eq(" + el.cellIndex + ")");
        var clone = footTD.children().clone();
        if (!clone.is(":input")) return;
        clone.keydown(function (a, b) { event.cancelBubble = true; });
        $(el).empty();
        data.format = model.format;
        data.update = arguments.callee;
        var typeValue = { checkbox: "checked" };
        var dbAttrs = [(typeValue[clone.attr("type")] || "value") + ":bindTo"];
        clone.dataBindAttr(dbAttrs);
        $(el).append(clone);
        $(":input", el).Enter2Tab();
        var bindTo = ko.computed({
          read: function () {
            var v = this[dataProperty];
            return typeof v == 'boolean' ? v : model.format(ko.utils.unwrapObservable(this[dataProperty]));
          },
          write: function (value) {
            var ov = ko.utils.unwrapObservable(this[dataProperty]);
            if (ov instanceof Date)
              value = new Date(value);
            if (typeof value != "string" && isNaN(value))
              return alert(value + "");
            if (("" + ov) == ("" + value)) return;
            ko.utils.setObservableOrNotValue(this, dataProperty, value);
            model.UpdateData(observables[0], this, $.proxy(function (result) {
              if (dataProperty != dataPropertyOriginal)
                ko.utils.setObservableOrNotValue(this, dataPropertyOriginal, result[dataPropertyOriginal]);
              alert("Saved.");
            }, this));
          },
          owner: data
        });
        var cleanup = $.proxy(function _cleanup() {
          ko.cleanNode(clone[0]);
          clone.blur().remove();
          var $this = this;
          $.each(observables, function () {
            try {
              model.GetData(this);
              model.fireUpdated(this);
              var newRow = $.extend({}, $this);
              model[this].replace($this, newRow);
              model.selectRow(newRow, this);
            } catch (e) {
              alert("Cleanup Error:\n" + e.Message);
            }
          });
          //          ko.cleanNode(el);
          //          ko.applyBindings(this, el);
        }, data);
        clone.focus().blur(cleanup).keypress(function (a) { if (a.charCode == 27) cleanup() });
        ko.applyBindings({ bindTo: bindTo }, clone[0]);
      },
      bindTable: function (table, data, vm, vmProperty, container, options) {
        table = $(table);
        if (!table.length) {
          alert(table.selector + " is not found.");
          return;
        }
        var dataBind = "data-bind";
        if (ko.isObservable(vm[vmProperty]))
          vm[vmProperty].splice.apply(vm[vmProperty], [0, 0].concat(data));
        else
          vm[vmProperty] = ko.observableArray(data);
        vm[vmProperty + "New"] = {};
        var propPrev = "", stop = false;
        var headres = vm[vmProperty + "Headers"] = ko.observableArray();
        vm[vmProperty + "Columns"] = ko.observableArray($.map(data[0], function (e, n) {
          if (n == "C_") stop = true;
          if (stop) return;
          if (n.startsWith("__")) return undefined;
          if (propPrev + "Id" != n) {
            propPrev = n;
            vm[vmProperty + "New"][n] = ko.observable("");
            var header = ((vm.headers || {})[vmProperty] || {})[n];
            header = (header || {}).header || header;
            headres.push(header || n);
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
    dataBind: function (vm, node, rows, cols, headers) {
      var DATA_BIND = "data-bind";
      cols = cols || vm[rows + "Columns"];
      if (!cols)
        vm[cols = rows + "Cols"] = $.map(ko.utils.unwrapObservable(vm[rows])[0], function (v, n) { return n; });
      headers = headers || rows + "Headers";
      node = $(node);
      var table = node.is("TABLE") ? node : $("TABLE", node);
      table.not(":has(CAPTION)").prepend('<caption>' + rows + '</caption>');
      var itemNew = rows + "New";
      var addItem = rows + "Add";
      var deleteItem = rows + "Delete";
      replaceAttr(node, "columns", cols);
      replaceAttr(node, "headers", headers);
      replaceAttr(node, "rows", rows);
      replaceAttr(node, "new", itemNew);
      replaceAttr(node, "add", addItem);
      replaceAttr(node, "delete", deleteItem);

      ko.applyBindings(vm, node[0]);
      ko.cleanNode(node[0]);

      mash("THEAD"); mash("TBODY"); mash("TFOOT");
      var tr = $('TBODY TR', node);
      tr.dataBindAttr(tr.dataBindAttr(), "css:{selected:$root.isSelected($data,'" + rows + "')},click:function(a,b){$root.selectRow(a,'" + rows + "')}");
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