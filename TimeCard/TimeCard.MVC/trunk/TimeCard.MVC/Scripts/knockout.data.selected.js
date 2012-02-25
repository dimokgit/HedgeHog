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
      function selectRowByProp(data, propName, find) {
        propName = $.D.name(this, propName);
        var item = ko.utils.arrayFirst(ko.utils.unwrapObservable(this[propName]), find);
        if ((this.isSelected(item, propName) || {}).data != item || !item)
          this.selectRow(item, propName);
      }
      this.onRowSelected = ko.observable();
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
