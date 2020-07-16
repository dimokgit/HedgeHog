using HedgeHog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static IBApp.AccountManager;

namespace IBApp {
  public class OrderContractHolders :IEnumerable<OrderContractHolder> {
    private readonly IEnumerable<OrderContractHolder> source;
    public OrderContractHolders(OrderContractHolder source) : this(new[] { source }) {
    }
    public OrderContractHolders(IEnumerable<OrderContractHolder> source) {
      this.source = source;
    }
    public IEnumerator<OrderContractHolder> GetEnumerator() => source.GetEnumerator();
    public override string ToString() => base.ToString();
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
  }
  public class OrderContractHolderWithError :ErrorMessage<OrderContractHolders> {
    public OrderContractHolder holder => value.SingleOrDefault();
    public OrderContractHolderWithError(OrderContractHolders value, ErrorMessage error = default) : base(value, error) { }
    public OrderContractHolderWithError(ErrorMessages<OrderContractHolder> em) : base(new OrderContractHolders(em.value), em.error) { }
    public static implicit operator OrderContractHolderWithError((OrderContractHolder holder, ErrorMessage error) value) => new OrderContractHolderWithError(new OrderContractHolders(value.holder), value.error);
    public object ToAnon() => new { holders = value.Flatter(","), error };
    public override string ToString() => ToAnon().ToString();
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
  public static class GenericException {
    public static GenericException<T> Create<T>(T t) => new GenericException<T>(t);
  }
  public class GenericException<TData> :Exception {
    public GenericException(TData context) {
      Context = context;
    }

    public TData Context { get; }
  }
  public class IBException :Exception {
    public IBException(int reqId, int errorCode, string errorMsg, Exception exc) {
      ReqId = reqId;
      ErrorCode = errorCode;
      ErrorMsg = errorMsg;
      Exc = exc;
    }
    public IBException(ErrorMessage em) : this(em.reqId, em.errorCode, em.errorMsg, em.exc) { }
    public int ReqId { get; }
    public int ErrorCode { get; }
    public string ErrorMsg { get; }
    public Exception Exc { get; }

    public override string ToString() =>
      Exc == null
      ? new { ReqId, ErrorCode, ErrorMsg } + ""
      : new { ReqId, ErrorCode, ErrorMsg, Exc } + "";
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
    public PlaceOrderException() : base("Pending request") {

    }
  }
}