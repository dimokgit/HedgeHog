using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Charter {
  public class BuySellRateRemovedEventArgs : EventArgs {
    public Guid UID { get; set; }
    public BuySellRateRemovedEventArgs(Guid uid) {
      this.UID = uid;
    }
  }
  public class BuySellRateAddedEventArgs : EventArgs {
    public bool IsBuy { get; set; }
    public double Rate { get; set; }
    public BuySellRateAddedEventArgs(bool isBuy, double rate) {
      this.IsBuy = isBuy;
      this.Rate = rate;
    }
  }
  public class PlayEventArgs : EventArgs {
    public bool Play { get; set; }
    public DateTime StartDate { get; set; }
    public TimeSpan Delay { get; set; }
    public PlayEventArgs(bool play, DateTime startDate, double delayInSeconds) : this(play, startDate, TimeSpan.FromSeconds(delayInSeconds)) { }
    public PlayEventArgs(bool play, DateTime startDate, TimeSpan delay) {
      this.Play = play;
      this.StartDate = startDate;
      this.Delay = delay;
    }
  }

  public class GannAngleOffsetChangedEventArgs : EventArgs {
    public double Offset { get; set; }
    public GannAngleOffsetChangedEventArgs(double offset) {
      this.Offset = offset;
    }
  }
  public class SupportResistanceChangedEventArgs : PositionChangedBaseEventArgs<double> {
    public Guid UID { get; set; }
    public SupportResistanceChangedEventArgs(Guid uid, double newPosition, double oldPosition)
      : base(newPosition, oldPosition) {
      this.UID = uid;
    }
  }

  public class CorridorPositionChangedEventArgs : PositionChangedBaseEventArgs<DateTime> {
    public CorridorPositionChangedEventArgs(DateTime newPosition, DateTime oldPosition) : base(newPosition, oldPosition) { }
  }
  public class PositionChangedBaseEventArgs<T> : EventArgs {
    public T NewPosition { get; set; }
    public T OldPosition { get; set; }
    public PositionChangedBaseEventArgs(T newPosition, T oldPosition) {
      this.NewPosition = newPosition;
      this.OldPosition = oldPosition;
    }
  }
}
