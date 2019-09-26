using HedgeHog;
using HedgeHog.Shared;
using IBSampleApp.messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using OpenOrderHandler = System.Action<IBSampleApp.messages.OpenOrderMessage>;
using OrderStatusHandler = System.Action<IBSampleApp.messages.OrderStatusMessage>;
using PortfolioHandler = System.Action<IBApp.UpdatePortfolioMessage>;
using PositionHandler = System.Action<IBApp.PositionMessage>;
using PositionEndHandler = System.Action;
using System.Collections.Concurrent;
using System.Reactive.Subjects;
using IBApi;
using System.Reactive;
using HedgeHog.Core;

namespace IBApp {
  public partial class AccountManager {
    #region Fields
    List<IDisposable> _strams = new List<IDisposable>();
    #endregion
    #region Properties
    public readonly EventLoopScheduler MainScheduler;
    public System.IObservable<IBApp.PositionMessage> PositionsObservable { get; private set; }
    public IObservable<Unit> PositionsEndObservable { get; }
    public IObservable<OpenOrderMessage> OpenOrderObservable { get; private set; }
    public IObservable<OrderStatusMessage> OrderStatusObservable { get; private set; }
    public ConcurrentDictionary<int, OrderContractHolder> OrderContractsInternal { get; } = new ConcurrentDictionary<int, OrderContractHolder>();
    #endregion
    public AccountManager(IBClientCore ibClient, string accountId, Func<string, Trade> createTrade, Func<Trade, double> commissionByTrade) : base(ibClient) {
      CommissionByTrade = commissionByTrade;
      CreateTrade = createTrade;
      Account = new Account();
      _accountId = accountId;

      RequestAccountSummary();
      SubscribeAccountUpdates();

      var _reqPositionsEnd = Observable.FromEvent(
        h => IbClient.PositionEnd += h,
        h => IbClient.PositionEnd -= h
        )
        .Subscribe(_ => {
          TraceDebug(Positions.ToTextOrTable("PositionsEnd:"));
        }).SideEffect(s => _strams.Add(s));
      RequestPositions();
      IbClient.OnReqMktData(() => IbClient.ClientSocket.reqOpenOrders());
      IbClient.OnReqMktData(() => IbClient.ClientSocket.reqAllOpenOrders());
      if(ibClient.ClientId == 0)
        IbClient.OnReqMktData(() => IbClient.ClientSocket.reqAutoOpenOrders(true));

      IbClient.OnReqMktData(() => IbClient.AccountSummary += OnAccountSummary);
      IbClient.OnReqMktData(() => IbClient.AccountSummaryEnd += OnAccountSummaryEnd);
      IbClient.OnReqMktData(() => IbClient.UpdateAccountValue += OnUpdateAccountValue);
      IbClient.OnReqMktData(() => IbClient.UpdatePortfolio += OnUpdatePortfolio);

      OpenTrades.ItemsAdded.Delay(TimeSpan.FromSeconds(5)).Subscribe(RaiseTradeAdded).SideEffect(s => _strams.Add(s));
      OpenTrades.ItemChanged
        .Where(e => e.PropertyName == "Lots")
        .Select(e => e.Sender)
        .Subscribe(RaiseTradeChanged)
        .SideEffect(s => _strams.Add(s));
      OpenTrades.ItemsRemoved.SubscribeOn(TaskPoolScheduler.Default).Subscribe(RaiseTradeRemoved).SideEffect(s => _strams.Add(s));
      //ClosedTrades.ItemsAdded.SubscribeOn(TaskPoolScheduler.Default).Subscribe(RaiseTradeClosed).SideEffect(s => _strams.Add(s));
      ibClient.ErrorObservable.Subscribe(OnError);

      #region Observables
      void Try(Action a, string source) {
        try {
          a();
        } catch(Exception exc) {
          TraceError(new Exception(source, exc));
        }
      }
      MainScheduler = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = nameof(AccountManager) });
      var positionsScheduler = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "Positions", Priority = ThreadPriority.Normal });

      PositionsObservable = Observable.FromEvent<PositionHandler, PositionMessage>(
        onNext => (PositionMessage m) => Try(() => onNext(m), nameof(IbClient.Position)),
        h => IbClient.Position += h,
        h => IbClient.Position -= h
        )
        .Where(x => /*x.Position != 0 &&*/ x.Account == _accountId)
        //.DistinctUntilChanged(t => new { t.Contract, t.Position })
        .Do(x => Trace($"Position: {new { x.Contract, x.Position, x.AverageCost, x.Account } }"))
        .SelectMany(p =>
          from cds in IbClient.ReqContractDetailsCached(p.Contract).ToArray()
          from cd in cds.Count(1, i => {
            TraceError($"Position contract {p.Contract.FullString} has no details");
            //RequestPositions();
          }, i => TraceError($"Position contract {p.Contract} has more then 1 [{i}] details"))
          select p.SideEffect(_ => {
            p.Contract.Exchange = cd.Contract.Exchange;
            TraceDebug($"PositionContract: {cd.Contract}");
            if(p.Position == 0) _positions.TryRemove(p.Contract.Key, out var r);
            else {
              var cp = ContractPosition(p);
              _positions.AddOrUpdate(cp.contract.Key, cp, (k, v) => cp);
            }
          })
        )
        //.Spy("**** AccountManager.PositionsObservable ****")
        ;
      PositionsEndObservable = Observable.FromEvent(
      h => IbClient.PositionEnd += h,//.SideEffect(_ => Trace($"+= IbClient.Position")),
      h => IbClient.PositionEnd -= h//.SideEffect(_ => Trace($"-= IbClient.Position"))
      )
      //.Spy("**** AccountManager.PositionsObservable ****")
      ;

      IScheduler esOpenOrder = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "OpenOrder", Priority = ThreadPriority.Normal });
      OpenOrderObservable = Observable.FromEvent<OpenOrderHandler, OpenOrderMessage>(
        onNext => (OpenOrderMessage m) =>
        Try(() => onNext(m), nameof(IbClient.OpenOrder)),
        h => IbClient.OpenOrder += h,
        h => IbClient.OpenOrder -= h
        )
        .ObserveOn(esOpenOrder)
        .Distinct(x => OrderKey(x.Order))
        .SelectMany(m => {
          var x = (from cl in (m.Contract.ComboLegs ?? new List<ComboLeg>()).ToObservable()
                   from con in ibClient.ReqContractDetailsCached(cl.ConId).Select(cd => cd.Contract)
                   select con)
                   .ToArray()
                   .Where(_ => m.Contract.IsFuturesCombo || m.Contract.IsStocksCombo)
                   .Select(cons => m.SideEffect(_ => {
                     var syms = cons.Select(c => c.Symbol).ToList();
                     m.Contract.Symbol = m.Contract.IsStocksCombo ? syms.OrderBy(s => s).First() : syms[0];
                   })
                   );
          return x.ObserveOn(TaskPoolScheduler.Default).DefaultIfEmpty(m);
        })
        ;
      string OrderKey(IBApi.Order o) => new { o.PermId, o.LmtPrice, o.AuxPrice, Conditions = o.Conditions.Flatter("; ") } + "";

      OrderStatusObservable = Observable.FromEvent<OrderStatusHandler, OrderStatusMessage>(
        onNext
        => (OrderStatusMessage m) => Try(() => onNext(m), nameof(IbClient.OrderStatus)),
        h => IbClient.OrderStatus += h,
        h => IbClient.OrderStatus -= h
        )
        ;

      var portObs = Observable.FromEvent<PortfolioHandler, UpdatePortfolioMessage>(
        onNext => (UpdatePortfolioMessage m) => Try(() => onNext(m), nameof(IbClient.UpdatePortfolio)),
        h => IbClient.UpdatePortfolio += h,
        h => IbClient.UpdatePortfolio -= h
        )
        .ObserveOn(MainScheduler)
        ;


      #endregion
      #region Subscibtions
      DoShowRequestErrorDone = false;
      PositionsObservable
        .Subscribe(OnPosition, () => { Trace("posObs done"); })
        .SideEffect(s => _strams.Add(s));

      OpenOrderObservable
        .Where(x => x.Order.Account == _accountId)
        .Do(x => TraceDebug($"OpenOrder: {new { x.Order.OrderId, x.Order.Transmit, conditions = x.Order.Conditions.Flatter(";") } }"))
        //.Do(UpdateOrder)
        .Do(x => Trace($"OpenOrder: {x}"))
        .Subscribe(a => OnOrderImpl(a))
        .SideEffect(s => _strams.Add(s));

      IScheduler esOrderStatus = new EventLoopScheduler(ts => new Thread(ts) { IsBackground = true, Name = "OrderStatus", Priority = ThreadPriority.Normal });
      OrderStatusObservable
          .Do(t => TraceDebug("OrderStatus: " + new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, isDone = (t.Status, t.Remaining).IsOrderDone() }))
          .Distinct(t => new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld })
          .Do(t => TraceDebug0("OrderStatus: " + new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, isDone = (t.Status, t.Remaining).IsOrderDone() }))
          .Where(t => OrderContractsInternal.ByOrderId(t.OrderId).Any(oc => oc.order.Account == _accountId))
          .Do(t => Verbose("* OrderStatus " + new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld, isDone = (t.Status, t.Remaining).IsOrderDone() }))
          //.Do(x => UseOrderContracts(oc => _verbous("* " + new { OrderStatus = x, Account = oc.ByOrderId(x.orderId, och => och.order.Account).SingleOrDefault() })))
          .Do(t => {
            OrderContractsInternal.ByOrderId(t.OrderId).Where(oc => t.Status != "Inactive")
              //.SelectMany(oc => new[] { oc }.Concat(ocs.ByOrderId(oc.order.ParentId).Where(och => och.isNew)))
              .ForEach(oc => {
                oc.status = new OrderContractHolder.Status(t.Status, t.Filled, t.Remaining);
              });
            IbClient.ClientSocket.reqAllOpenOrders();
          }
          )
          .Where(m => m.IsOrderDone())
          .SelectMany(o => UseOrderContracts(ocs => ocs.ByOrderId(o.OrderId)).Concat())
          .ObserveOn(esOpenOrder)
          .Subscribe(o => RaiseOrderRemoved(o))
          .SideEffect(s => _strams.Add(s));

      portObs
        .Where(x => x.AccountName == _accountId)
        .Select(t => new { t.Contract.LocalSymbol, t.Position, t.UnrealisedPNL, t.AccountName })
        .Timeout(TimeSpan.FromSeconds(5))
        .Where(x => x.Position != 0)
        .CatchAndStop(() => new TimeoutException())
        .Subscribe(x => _verbous("* " + new { Portfolio = x }), () => _verbous($"portfolioStream is done."))
        .SideEffect(s => _strams.Add(s));

      WireOrderEntryLevels();

      DateTime thStart() => ibClient.ServerTime.Date.AddHours(9).AddMinutes(29);
      DateTime thEnd() => ibClient.ServerTime.Date.AddHours(16);
      var shouldExecute = (
      from pair in IbClient.PriceChangeObservable.Select(_ => _.EventArgs.Price.Pair)
      where false && !ibClient.ServerTime.Between(thStart(), thEnd())
      from oc in OrderContractsInternal
      where oc.Value.ShouldExecute
      from paren in OrderContractsInternal.ByOrderId(oc.Value.order.ParentId).DefaultIfEmpty()
      where (paren == null || paren.isDone)
      select oc.Value
      )
      .Distinct(oc => oc.order.PermId)
      .ObserveOn(MainScheduler);
      // Execute parent
      (from oc in shouldExecute
         //where oc.order.ParentId == 0
       from child in ChildHolder(oc).DefaultIfEmpty()
       let c = oc.contract.SideEffect(() => Trace($"Will Execute: {oc}{(child == null ? "" : " with " + child)}"))
       let tpCond = child?.order.Conditions.FirstOrDefault()
       let q = (oc.order.IsBuy ? 1 : -1) * oc.order.TotalQuantity.ToInt()
       from ot in OpenTrade(oc.contract, q, 0, 0, tpCond != null, default, default, null, tpCond)
       select ot
      ).Subscribe();

      var shouldCancel = (
        from pair in IbClient.PriceChangeObservable.Select(_ => _.EventArgs.Price.Pair)
        from child in OrderContractsInternal.ByLocalSymbol(pair)
        let any = Positions.Any(p => p.contract == child.contract)
        where !any
        select child
      )
      .Take(0)
      .Distinct(x => x.order.PermId)
      .ObserveOn(MainScheduler);
      (from cancel in shouldCancel
       from oc in CancelOrder(cancel.order.OrderId)
       select oc
       ).Subscribe(h => Trace("Orphan Cancelled:" + h));
      #endregion

      IbClient.ClientSocket.reqAllOpenOrders();

      Trace($"{nameof(AccountManager)}:{_accountId} is ready");

      #region Local methods
      void OnOrderImpl(OpenOrderMessage m) {
        if(!m.Order.WhatIf) {
          var raiseEvent = true;
          var h = OrderContractsInternal.AddOrUpdate(m.OrderId, m, (k, v) => { raiseEvent = false; return m; });
          TraceDebug0($"{nameof(OnOrderImpl)}: {h}");
          if(raiseEvent)
            RaiseOrderAdded(new HedgeHog.Shared.Order {
              IsBuy = m.Order.Action == "BUY",
              Lot = (int)m.Order.TotalQuantity,
              Pair = m.Contract.Instrument,
              IsEntryOrder = m.Order.IsEntryOrder()
            });
        } else if(GetTrades().IsEmpty()) {
          // TODO: WhatIf leverage, MMR
          //RaiseOrderRemoved(o.OrderId);
          var offer = TradesManagerStatic.GetOffer(m.Contract.Instrument);
          var isBuy = m.Order.IsBuy();
          var levelrage = (m.Order.LmtPrice * m.Order.TotalQuantity) / (double.Parse(m.OrderState.InitMarginChange));
          if(levelrage != 0 && !double.IsInfinity(levelrage))
            if(isBuy) {
              offer.MMRLong = 1 / levelrage;
              Trace(new { offer = new { offer.Pair, offer.MMRLong } });
            } else {
              offer.MMRShort = 1 / levelrage;
              Trace(new { offer = new { offer.Pair, offer.MMRShort } });
            }
        }
      }
      #endregion

    }

    //public ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)> OrderStatuses { get; } = new ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)>();

    private void RaiseOrderRemoved(OrderContractHolder cd) {
      var trace = ($"{nameof(RaiseOrderRemoved)}: {cd}");
      if(OrderContractsInternal.TryRemove(cd.order.OrderId, out var _)) {
        var o = cd.order;
        var c = cd.contract;
        RaiseOrderRemoved(new HedgeHog.Shared.Order {
          IsBuy = o.Action == "BUY",
          Lot = (int)o.TotalQuantity,
          Pair = c.Instrument,
          IsEntryOrder = o.IsEntryOrder()
        });
      }
    }

    private void OnError((int reqId, int code, string error, Exception exc) e) {
      OrderContractsInternal.ByOrderId(e.reqId).ToList().ForEach(oc => {
        if(new[] { ORDER_CAMCELLED }.Contains(e.code)) {
          RaiseOrderRemoved(oc);
        }
      });
    }

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if(!disposedValue) {
        if(disposing) {
          _strams.ForEach(s => s.Dispose());
          _strams.Clear();
        }
        disposedValue = true;
      }
    }

    // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
    // ~AccountManager() {
    //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
    //   Dispose(false);
    // }

    // This code added to correctly implement the disposable pattern.
    public void Dispose() {
      // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
      Dispose(true);
      // TODO: uncomment the following line if the finalizer is overridden above.
      // GC.SuppressFinalize(this);
    }
    #endregion
  }
}
