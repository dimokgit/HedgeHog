/// <reference path="jquery.js" />
/// <reference path="knockout.js" />
Namespace("ko.data", {
  dataBind: function (vm, node, rows, cols) {
    var DATA_BIND = "data-bind";
    cols = cols || vm[rows + "Columns"];
    if (!cols)
      vm[cols = rows + "Cols"] = $.map(ko.utils.unwrapObservable(vm[rows])[0], function (v, n) { return n; });
    node = $(node);
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
