using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared {
  public class NewOldEventArgs<T> :EventArgs{
    public T New { get; set; }
    public T Old { get; set; }
    public NewOldEventArgs(T New,T Old) {
      this.New = New;
      this.Old = Old;
    }
  }
}
