using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Interactivity;
using System.Windows.Controls;
using System.Windows;
using System.Reflection;

namespace HedgeHog.UI {
  public class BindableSelectedItemBehaviour : Behavior<TreeView> {
    #region SelectedItem Property

    public object SelectedItem {
      get { return (object)GetValue(SelectedItemProperty); }
      set { SetValue(SelectedItemProperty, value); }
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register("SelectedItem", typeof(object), typeof(BindableSelectedItemBehaviour), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    private static void OnSelectedItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
      if (e.NewValue == null) {
        var tv = ((HedgeHog.UI.BindableSelectedItemBehaviour)(sender)).AssociatedObject;
        tv.Dispatcher.BeginInvoke(new Action(() => SelectItem(tv, false)));
      }
      var item = e.NewValue as TreeViewItem;
      if (item != null) {
        item.SetValue(TreeViewItem.IsSelectedProperty, true);
      }
    }

    private static void SelectItem(TreeView tv,bool doSelect) {
      var tvip = typeof(TreeView).GetProperty("SelectedContainer", BindingFlags.NonPublic | BindingFlags.Instance);
      var select = typeof(TreeViewItem).GetMethod("Select", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      var tvi = tvip.GetValue(tv, null);
      if (tvi != null)
        select.Invoke(tvi, new object[] { doSelect });
    }

    #endregion

    protected override void OnAttached() {
      base.OnAttached();

      this.AssociatedObject.SelectedItemChanged += OnTreeViewSelectedItemChanged;
      this.AssociatedObject.MouseDoubleClick += new System.Windows.Input.MouseButtonEventHandler(AssociatedObject_MouseDoubleClick);
    }

    void AssociatedObject_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
      
    }

    protected override void OnDetaching() {
      base.OnDetaching();

      if (this.AssociatedObject != null) {
        this.AssociatedObject.SelectedItemChanged -= OnTreeViewSelectedItemChanged;
      }
    }

    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
      this.SelectedItem = e.NewValue;
    }
  }
}
