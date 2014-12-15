using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Shared.Messages {
  public class CloseAllTradesMessage<T> {
    public T Sender { get; private set; }
    public Action<T> OnClose { get; private set; }
    public CloseAllTradesMessage(T sender, Action<T> onClose) {
      this.Sender = sender;
      this.OnClose = onClose;
    }
  }
  public class RepayPauseMessage { }
  public class RepayBackMessage { }
  public class RepayForwardMessage { }
  public class RequestPairForHistoryMessage {
    List<Tuple<string, int>> _pairs = new List<Tuple<string, int>>();
    public List<Tuple<string, int>> Pairs { get { return _pairs; } }
  }
}
