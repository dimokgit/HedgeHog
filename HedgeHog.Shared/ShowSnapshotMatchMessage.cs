using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class ShowSnapshotMatchMessage {
    public DateTime DateStart { get; set; }
    public DateTime DateEnd { get; set; }
    //public int BarCount { get; set; }
    public int BarPeriod { get; set; }
    public bool StopPropagation { get; set; }
    public double Correlation { get; set; }

    public ShowSnapshotMatchMessage(DateTime dateStart,DateTime dateEnd/*,int barCount*/,int barPeriod,double correlation) {
      this.DateStart = dateStart;
      this.DateEnd = dateEnd;
      //this.BarCount = barCount;
      this.BarPeriod = barPeriod;
      this.Correlation = correlation;
    }
  }
}
