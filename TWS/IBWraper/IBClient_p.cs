using IBSampleApp.messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using System.Windows.Threading;
using System.Windows;

namespace IBApi {
  partial class IBClient {
    public IObservable<ManagedAccountsMessage> ManagedAccountsObservable { get; }
    public event Action ConnectionOpend;
    void RaiseConnectionOpened() => ConnectionOpend?.Invoke();

    public IBClient(EReaderSignal signal) {
      clientSocket = new EClientSocket(this, signal);


      //if(SynchronizationContext.Current == null)
      //SynchronizationContext.SetSynchronizationContext(
      //  new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

      //sc = SynchronizationContext.Current ?? new DispatcherSynchronizationContext();
      sc = new SynchronizationContextDummy();

      ManagedAccountsObservable = Observable.FromEvent<Action<ManagedAccountsMessage>, ManagedAccountsMessage>(
        onNext => (ManagedAccountsMessage a) => onNext(a),
        h => ManagedAccounts += h,
        h => ManagedAccounts -= h
        )
        .ObserveOn(new EventLoopScheduler(ts => new Thread(ts)))
        .Catch<ManagedAccountsMessage, Exception>(exc => {
          Error(-1, 0, nameof(ManagedAccounts), exc);
          return new ManagedAccountsMessage[0].ToObservable();
        });
    }

  }
}
