/// <reference path="jquery.js" />
/// <reference path="jquery.extentions.js" />
/// <reference path="knockout.js" />
/// <reference path="knockout.mapping-latest.debug.js" />
/// <reference path="MicrosoftAjax.debug.js" />

Namespace("ko.data");
if (!ko.data.selected) {
  Namespace("ko.data", {
    selected: new (function () {
      this.debug = false;
      this.selectedRows = ko.observableArray();
      this.selectedRowsByParent = function (parent) {
        return ko.utils.arrayFilter(this.selectedRows(), function (a) { return a.rows == parent });
      }
      this.selectRow = selectRow;
      this.isSelected = isSelected;
      function selectRow(data, parentName) {
        if (this.debug) debugger;
        var selected = this.isSelected(data, parentName);
        if (selected)
          this.selectedRows.remove(selected);
        else {
          this.selectedRows.remove(function (a) { return a.rows == parentName });
          var newSelected = new SelectedRow(parentName, data);
          this.selectedRows.push(newSelected);
          this.onRowSelected(newSelected);
        }
      }
      this.selectRowByProp = selectRowByProp;
      function selectRowByProp(propName, find) {
        propName = $.D.name(this, propName);
        var item = ko.utils.arrayFirst(ko.utils.unwrapObservable(this[propName]), find);
        if ((this.isSelected(item, propName) || {}).data != item || !item)
          this.selectRow(item, propName);
      }
      this.onRowSelected = ko.observable();
      this.rowNavigation = function (data, e, rowsName) {
        var rows = $.D.sure(this, rowsName);
        var current = (this.selectedRowsByParent(rowsName)[0] || {}).data;
        var select;
        switch (e.key) {
          case "Up":
            select = current ? rows()[Math.max(0, rows.indexOf(current) - 1)] : rows()[rows().length - 1];
            break;
          case "Down":
            select = current ? rows()[Math.min(rows().length - 1, rows.indexOf(current)) + 1] : rows()[0];
            break;
        }
        if (select)
          this.selectRow(select, rowsName);
        else
          return true; // Allow default action
      }
      function isSelected(data, parentName) {
        if (this.debug) debugger;
        var search = new SelectedRow(parentName, data);
        return ko.utils.arrayFirst(this.selectedRows(), function (a) { return compare(a, search) });
      }
      function compare(a, b) { return a.rows == b.rows && a.data == b.data; }
      function SelectedRow(parentName, data) {
        this.rows = parentName;
        this.data = data;
      }
    })()

  });
}
