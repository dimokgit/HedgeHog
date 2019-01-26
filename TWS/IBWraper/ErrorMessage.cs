using System;

namespace IBApp {
  public struct ErrorMessage {
    public readonly int reqId;
    public readonly int errorCode;
    public readonly string errorMsg;
    public ErrorMessage(int id, int errorCode, string errorMsg, Exception exc) {
      this.reqId = id;
      this.errorCode = errorCode;
      this.errorMsg = errorMsg + (exc == null ? "" : exc.ToString());
    }
    public bool HasError => !(reqId == 0 || errorMsg.ToLower().Contains("warning"));
    public static explicit operator ErrorMessage((int id, int errorCode, string errorMsg, Exception exc) p) =>
      new ErrorMessage(p.id, p.errorCode, p.errorMsg, p.exc);
    public override string ToString() => HasError ? new { reqId, errorCode, errorMsg } + "" : "";
  }
}