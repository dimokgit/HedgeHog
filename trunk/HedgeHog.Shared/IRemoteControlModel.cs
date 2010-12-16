using System;
namespace HedgeHog.Shared {
  public interface IRemoteControlModel {
    event EventHandler<DrawChartEventArgs> DrawChart;
    void ShowChart(object tm);
  }
  public class DrawChartEventArgs : EventArgs {
    public object Parent { get; set; }
    public Action<object> ShowChart;
    public DrawChartEventArgs(object parent, Action<object> showChartDelegate) {
      this.Parent = parent;
      this.ShowChart = showChartDelegate;
    }
  }
}
