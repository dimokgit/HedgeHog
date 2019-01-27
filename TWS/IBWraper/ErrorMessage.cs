using System;

namespace IBApp {
  public struct ErrorMessage<T> {
    public readonly T value;
    public readonly ErrorMessage error;

    public ErrorMessage(T value, ErrorMessage error = default) {
      this.value = value;
      this.error = error;
    }
  }
  public struct ErrorMessage {
    public readonly int reqId;
    public readonly int errorCode;
    public readonly Exception exc;
    public readonly string errorMsg;
    public ErrorMessage(int id, int errorCode, string errorMsg, Exception exc) {
      this.reqId = id;
      this.errorCode = errorCode;
      this.exc = exc;
      this.errorMsg = errorMsg;
    }
    public static ErrorMessage<T> Create<T>(T value, ErrorMessage error) => new ErrorMessage<T>(value, error);
    public static ErrorMessage<T> Empty<T>(T value) => new ErrorMessage<T>(value);
    public static ErrorMessage Empty(int reqId) => new ErrorMessage(reqId, 0, "", null);
    public bool HasError => !(errorCode == 0 || errorMsg.ToLower().Contains("warning")) || exc != null;
    public static implicit operator ErrorMessage((int id, int errorCode, string errorMsg, Exception exc) p) =>
      new ErrorMessage(p.id, p.errorCode, p.errorMsg, p.exc);
    public override string ToString() =>
      !HasError ? ""
      : exc == null
      ? new { reqId, errorCode, errorMsg } + ""
      : new { reqId, errorCode, errorMsg, exc } + "";
  }
  public class PlaceOrderException :Exception {
    //public PlaceOrderException(string message):base(message) {

    //}
  }
}