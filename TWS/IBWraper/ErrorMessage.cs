using HedgeHog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  public class OrderContractHolders :IEnumerable<OrderContractHolder> {
    private readonly IEnumerable<OrderContractHolder> source;
    public OrderContractHolders(IEnumerable<OrderContractHolder> source) {
      this.source = source;
    }
    public IEnumerator<OrderContractHolder> GetEnumerator() => source.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
  }
  class OrderContractHolderWithError :ErrorMessage<OrderContractHolders> {
    public OrderContractHolderWithError(OrderContractHolders value, ErrorMessage error = default) : base(value, error) {
    }
  }
  public class ErrorMessages<T> {
    public readonly IEnumerable<T> value;
    public readonly ErrorMessage error;

    public ErrorMessages(IEnumerable<T> value, ErrorMessage error = default) {
      this.value = value;
      this.error = error;
    }
    public override string ToString() => new { values = value.Flatter(Environment.NewLine), error } + "";
  }
  public class ErrorMessage<T> {
    public readonly T value;
    public readonly ErrorMessage error;

    public ErrorMessage(T value, ErrorMessage error = default) {
      this.value = value;
      this.error = error;
    }
    public override string ToString() => new { value, error } + "";
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
    public static ErrorMessages<T> Create<T>(IEnumerable<T> value, ErrorMessage error) => new ErrorMessages<T>(value, error);
    public static ErrorMessage<T> Empty<T>(T value) => new ErrorMessage<T>(value);
    public static ErrorMessages<T> Empty<T>(IEnumerable<T> value) => new ErrorMessages<T>(value);
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
    public PlaceOrderException():base("Pending request") {

    }
  }
}