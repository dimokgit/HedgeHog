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
using DynamicData;
using System.Diagnostics;
using static IBApp.AccountManagerMixins;

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
    //public ConcurrentDictionary<int, OrderContractHolder> OrderContractsInternal { get; } = new ConcurrentDictionary<int, OrderContractHolder>();
    public SourceCache<OrderContractHolder, int> OrderContractsInternal = new SourceCache<OrderContractHolder, int>(och => och.order.PermId);
    //readonly public IEnumerable<OrderContractHolder> OrderContractsInternalValues;
    #endregion
    public AccountManager(IBClientCore ibClient, string accountId, Func<string, Trade> createTrade, Func<Trade, double> commissionByTrade) : base(ibClient) {
      //OrderContractsInternalValues = OrderContractsInternal.Edit(u=>u.Remove(.KeyValues.Select(kv => kv.Value);
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
          TracePosition(Positions.ToTextOrTable("PositionsEnd:"));
        }).SideEffect(s => _strams.Add(s));
      RequestPositions();
      IbClient.OnReqMktData(() => IbClient.ClientSocket.reqOpenOrders());
      IbClient.OnReqMktData(() => IbClient.ClientSocket.reqAllOpenOrders());
      if(ibClient.ClientId == 0) IbClient.OnReqMktData(() => IbClient.ClientSocket.reqAutoOpenOrders(true));

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

      OrderContractsInternal.Connect()
        .OnItemAdded(RaiseOrderAdded)
        .Subscribe()
        .SideEffect(s => _strams.Add(s));
      OrderContractsInternal.Connect()
        .OnItemRemoved(RaiseOrderRemoved)
        .Subscribe()
        .SideEffect(s => _strams.Add(s));

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
        .Do(x => TracePosition($"Position: {new { x.Contract, x.Position, x.AverageCost, x.Account } }"))
        .Do(x => {
          if(x.Contract.SecType == "STK" && x.Contract.Exchange == "NASDAQ")
            x.Contract.Exchange = "SMART";
        })
        .SelectMany(p => ReqPositionContractDetailsAsync(p).Select(_ => p))
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
        );
      #region OpenOrderObservable Helpers
      IObservable<OpenOrderMessage> FixOpenOrderMessage(OpenOrderMessage m) =>
        (from cl in (m.Contract.ComboLegs ?? new List<ComboLeg>()).ToObservable()
         from con in ibClient.ReqContractDetailsCached(cl.ConId).Select(cd => cd.Contract)
         select con
         )
         .ToArray()
         .SelectMany(cons => cons
         .Select(c => c.Symbol)
         .IfTrue(_ => m.Contract.IsStocksCombo, s => s.OrderBy(s => s))
         .Do(sym => m.Contract.Symbol = sym)
         .Take(1)
         .Select(_ => m)
         .DefaultIfEmpty(m)
         );
      void AddOrderContractHolder(OpenOrderMessage m)
        => OrderContractsInternal.Items.ByPermId(m.Order.PermId).RunIfEmpty(()
        => OrderContractsInternal.AddOrUpdate(m));
      #endregion

      //string OrderKey(IBApi.Order o) => new { o.PermId, o.LmtPrice, o.AuxPrice, Conditions = o.Conditions.Flatter("; "), o.Account } + "";

      OrderStatusObservable = Observable.FromEvent<OrderStatusHandler, OrderStatusMessage>(
        onNext
        => (OrderStatusMessage m) => Try(() => onNext(m), nameof(IbClient.OrderStatus)),
        h => IbClient.OrderStatus += h,
        h => IbClient.OrderStatus -= h
        );

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

      var _raisedOrders = new ConcurrentDictionary<int, bool>();
      OpenOrderObservable // we only get it once per order
        .Where(x => x.Order.Account == _accountId && !x.OrderState.IsInactive)
        .Do(x => TraceDebug($"OnOpenOrder: {x.Order}: {x.Contract}: {x.OrderState}: {x.Contract.Exchange}"))
        .Distinct(x => x.Order.PermId)
        .SelectMany(x => ReqContextContractDetailsAsync(x.Contract).Select(_ => x))
        .Do(AddOrderContractHolder)
        .InjectIf(m => m.Contract.IsHedgeCombo, m => FixOpenOrderMessage(m).ObserveOn(TaskPoolScheduler.Default))
        //.Where(t => !t.OrderState.IsCancelled)
        .ObserveOn(esOpenOrder)
        .Do(x => Verbose($"OnOpenOrder[{x.OrderId}]: {x}"))
        .Subscribe(a => OnWhatIfOrder(a))
        .SideEffect(s => _strams.Add(s));

      OrderStatusObservable
          .Do(t => TraceDebug($"OrderStatus[{t.OrderId}]:{t.Status}" + new { t.Filled, t.Remaining, t.WhyHeld, isDone = t.IsOrderDone() }))
          .DistinctUntilChanged(t => new { t.OrderId, t.Status, t.Filled, t.Remaining, t.WhyHeld })
          .Where(t => OrderContractsInternal.Items.ByOrderId(t.OrderId).Any(oc => oc.order.Account == _accountId))
          //.Do(x => UseOrderContracts(oc => _verbous("* " + new { OrderStatus = x, Account = oc.ByOrderId(x.orderId, och => och.order.Account).SingleOrDefault() })))
          .Do(t => {
            OrderContractsInternal.Items.ByOrderId(t.OrderId)//.Where(oc => t.Status != "Inactive")
                                                             //.SelectMany(oc => new[] { oc }.Concat(ocs.ByOrderId(oc.order.ParentId).Where(och => och.isNew)))
              .ForEach(oc => {
                oc.status = new OrderContractHolder.Status(t.Status, t.Filled, t.Remaining);
              });
            //IbClient.OnReqMktData(()=>IbClient.ClientSocket.reqAllOpenOrders());
          })
          //.Do(t => {
          //  UseOrderContracts(oc => {
          //    if(!t.IsOrderDone()) {
          //      var raiseEvent = _raisedOrders.TryAdd(t.OrderId, true);
          //      if(raiseEvent)
          //        oc.ByOrderId(t.OrderId).ForEach(och
          //          => RaiseOrderAdded(OrderFromHolder(och.SideEffect(_
          //          => TraceDebug($"RaiseOrderAdded: {och}")))));
          //    }
          //  });
          //})
          .Where(m => m.IsOrderDone())
          .SelectMany(o => UseOrderContracts(ocs => ocs.ByOrderId(o.OrderId)).Concat())
          .ObserveOn(esOpenOrder)
          .Subscribe(o => UseOrderContracts(oci => OrderContractsInternal.Remove(o)))
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
      if(false) WireTrendEdges();

      DateTime thStart() => ibClient.ServerTime.Date.AddHours(9).AddMinutes(29);
      DateTime thEnd() => ibClient.ServerTime.Date.AddHours(16);
      var shouldExecute = (
      from pair in IbClient.PriceChangeObservable.Select(_ => _.EventArgs.Price.Pair)
      where false && !ibClient.ServerTime.Between(thStart(), thEnd())
      from oc in OrderContractsInternal.Items
      where oc.ShouldExecute
      from paren in OrderContractsInternal.Items.ByOrderId(oc.order.ParentId).DefaultIfEmpty()
      where (paren == null || paren.isDone)
      select oc
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
        from child in OrderContractsInternal.Items.ByLocalSymbol(pair)
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
      void OnWhatIfOrder(OpenOrderMessage m) {
        if(!m.Order.WhatIf) {
          //UseOrderContracts(oc => {
          //  var raiseEvent = _raisedOrders.TryAdd(m.Order.PermId, true);
          //  oc.ByOrderId(m.OrderId).Any();
          //  if(raiseEvent)
          //    RaiseOrderAdded(OrderFromOrderMessage(m));
          //});
        } else if(GetTrades().IsEmpty()) {
          // TODO: WhatIf leverage, MMR
          //RaiseOrderRemoved(o.OrderId);
          return;
          var offer = TradesManagerStatic.GetOffer(m.Contract.Instrument);
          var isBuy = m.Order.IsBuy;
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
        HedgeHog.Shared.Order OrderFromOrderMessage(OpenOrderMessage m) => new HedgeHog.Shared.Order {
          IsBuy = m.Order.Action == "BUY",
          Lot = (int)m.Order.TotalQuantity,
          Pair = m.Contract.Instrument,
          IsEntryOrder = m.Order.IsEntryOrder()
        };
      }
      #endregion

    }

    private IObservable<PositionMessage> ReqPositionContractDetailsAsync(PositionMessage p) =>
      from cd in ReqContextContractDetailsAsync(p.Contract)
      select p.SideEffect(_ => {
        p.Contract.Exchange = cd.Contract.Exchange;
        TracePosition($"PositionContract: {new { cd.Contract, cd.Contract.Exchange }}");
        if(p.Position == 0  || p.Contract.IsExpired) _positions.TryRemove(p.Contract.Key, out var r);
        else {
          var cp = ContractPosition(p);
          _positions.AddOrUpdate(cp.contract.Key, cp, (k, v) => cp);
        }
      });
    private IObservable<ContractDetails> ReqContextContractDetailsAsync(Contract Contract) =>
      from cds in IbClient.ReqContractDetailsCached(Contract).ToArray()
      from cd in cds.Count(1, i => {
        var m = $"Contract {Contract.FullString} has no details";
        TraceError(m);
        //throw new Exception(m);
      }, i => TraceError($"Contract {Contract} has more then 1 [{i}] details"))
      select cd;

    private HedgeHog.Shared.Order OrderFromHolder(OrderContractHolder m) => new HedgeHog.Shared.Order {
      IsBuy = m.order.Action == "BUY",
      Lot = (int)m.order.TotalQuantity,
      Pair = m.contract.Instrument,
      IsEntryOrder = m.order.IsEntryOrder()
    };

    //public ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)> OrderStatuses { get; } = new ConcurrentDictionary<string, (string status, double filled, double remaining, bool isDone)>();

    private void RaiseOrderRemoved(OrderContractHolder cd) {
      //if(Thread.CurrentThread.Name == "MsgProc") Debugger.Break();
      var trace = ($"{nameof(RaiseOrderRemoved)}: {cd}");
      TraceDebug($"{trace}\n{OrderContractsInternal.Items.Select(och => new { och }).ToTextOrTable()}");
      var o = cd.order;
      var c = cd.contract;
      RaiseOrderRemoved(new HedgeHog.Shared.Order {
        IsBuy = o.Action == "BUY",
        Lot = (int)o.TotalQuantity,
        Pair = c.Instrument,
        IsEntryOrder = o.IsEntryOrder()
      });
    }

    private void OnError((int reqId, int code, string error, Exception exc) e) {
      OrderContractsInternal.Items.ByOrderId(e.reqId)
        .Where(_ => new[] { ORDER_CAMCELLED }.Contains(e.code))
        .ToList().ForEach(OrderContractsInternal.RemoveByHolder);
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
