using HedgeHog.Alice.Store;
using HedgeHog.Shared;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;

namespace HedgeHog.Alice.Client {
  public partial class RemoteControlModel {
    #region StartReplayCommand
    ReactiveCommand<object> _StartReplayCommand;
    public ReactiveCommand<object> StartReplayCommand {
      get {
        if (_StartReplayCommand == null) {
          var o = this.WhenAnyObservable(x => x._replayTasks.CountChanged)
            .StartWith(0)
            .Select(c => c == 0);
          //var o2 = this.ObservableForProperty(vm => true, false, false).Select(x => x.Value).ObserveOn(RxApp.MainThreadScheduler);
          _StartReplayCommand = ReactiveCommand.Create(o,RxApp.MainThreadScheduler);
          _StartReplayCommand.Subscribe(StartReplay);
        }
        return _StartReplayCommand;
      }
    }
    ReplayArguments<TradingMacro> _replayArguments = new ReplayArguments<TradingMacro>();
    public ReplayArguments<TradingMacro> ReplayArguments {
      get { return _replayArguments; }
    }

    public ReactiveList<Task> _replayTasks = new ReactiveList<Task>();
    void StartReplay(object _) {
      TradingMacro tmOriginal = (TradingMacro)_;
      try {
        while (_replayTasks.ToArray().Any(t => t.Status == TaskStatus.Running)) {
          Log = new Exception("Replay is running.");
          Thread.Sleep(1000);
          continue;
        }

        ReplayArguments.Initiator = tmOriginal;
        ReplayArguments.UseSuperSession = tmOriginal.TestUseSuperSession;
        ReplayArguments.SuperSessionId = tmOriginal.TestSuperSessionUid.ValueOrDefault(Guid.NewGuid());
        _testParamsRaw.Clear();
        tmOriginal.TestFileName = "";

        if (ReplayArguments.UseSuperSession) {
          #region getDateFromSuperSession
          Func<DateTime> getDateFromSuperSession = () => {
            try {
              var sessions = GetBestSessions(ReplayArguments.SuperSessionId).ToArray();
              if (sessions.Any()) FillTestParams(tmOriginal, tpr => SetTestCorridorDistanceRatio(tpr, sessions));
              else throw new Exception("Either ReplayArguments.DateStart or valid Supersession Uid must be provided.");
              return sessions.Min(s => s.DateStart.Value).AddDays(5);
            } catch (Exception exc) {
              Log = exc;
              throw;
            }
          };
          #endregion
          ReplayArguments.DateStart = ReplayArguments.DateStart ?? getDateFromSuperSession();
        }
        FillTestParams(tmOriginal, pt => { });
        Log = new Exception("Starting testing with {0} sets.".Formater(TestParams.Count));
        StartReplayInternal(tmOriginal, TestParams.Any() ? TestParams.Dequeue() : null, task => { ContinueReplayWith(tmOriginal, TestParams); });
      } catch (Exception exc) { Log = exc; }
    }
    void FillTestParams(TradingMacro tmOriginal, Action<IList<KeyValuePair<string, object>[]>> paramsTransformation) {
      var c = new[] { '^', '\t' };
      if (!_testParamsRaw.Any()) {
        if (tmOriginal.UseTestFile) {
          var od = new Microsoft.Win32.OpenFileDialog() { FileName = "TestParams", DefaultExt = ".txt", Filter = "Text documents(.txt)|*.txt" };
          var odRes = od.ShowDialog();
          if (!odRes.GetValueOrDefault()) throw new ArgumentException("Must provide test params file name.");
          tmOriginal.TestFileName = System.IO.Path.GetFileName(od.FileName);
          var paramsDict = Lib.ReadTestParameters(od.FileName);
          _testParamsRaw.AddRange(paramsDict.Select(kv => kv.Value.Split(c).Select(v => new KeyValuePair<string, object>(kv.Key, v)).ToArray()));
        } else {
          var testParams = tmOriginal.GetPropertiesByAttibute<CategoryAttribute>(a => a.Category == TradingMacro.categoryTest);
          var paramsDict = testParams.ToDictionary(p => p.Item2.Name.Substring(4), p => p.Item2.GetValue(tmOriginal, null).ToString().ParseParamRange());
          _testParamsRaw.AddRange(paramsDict.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Value.Split(c).Select(v => new KeyValuePair<string, object>(kv.Key, v)).ToArray()));
        }
      }
      TestParams.Clear();
      paramsTransformation(_testParamsRaw);
      _testParamsRaw.CartesianProduct().ForEach(tp => TestParams.Enqueue(tp.ToArray()));
    }

    private void ReplaceTestParamRaw(string name,IList<KeyValuePair<string,object>> testParamsKeyValues) {
      var cdr = _testParamsRaw.First(tpr => tpr.Any(kv => kv.Key == name));
      var index = _testParamsRaw.IndexOf(cdr);
      _testParamsRaw[index] = testParamsKeyValues.ToArray();
    }

    void ContinueReplayWith(TradingMacro tm, Queue<TestParam> testParams) {
      try {
        if (tm.Strategy == Strategies.None) return;
        if (testParams.Any()) {
          StartReplayInternal(tm, testParams.Dequeue(), t => { ContinueReplayWith(tm, testParams); });
        } else if (ReplayArguments.UseSuperSession) {
          var super = GetBestSessions(ReplayArguments.SuperSessionId).ToArray();
          FillTestParams(tm, tpr => SetTestCorridorDistanceRatio(tpr, super));
          ReplayArguments.SuperSessionId = Guid.NewGuid();
          ReplayArguments.DateStart = ReplayArguments.DateStart.Value.AddDays(6);
          StartReplayInternal(tm, TestParams.Any() ? TestParams.Dequeue() : null, task => { ContinueReplayWith(tm, TestParams); });
        }
      } catch (Exception exc) { Log = exc; }
    }

    #region GetBestSession
    Func<DB.v_TradeSession, decimal> _bestSessionCriteria = s => -s.PL.Value;
    private IEnumerable<DB.v_TradeSession> GetBestSessions(Guid superSessionUid) {
      return GlobalStorage.UseForexContext(c => {
        var sessions = c.v_TradeSession.Where(s => s.SuperSessionUid == superSessionUid).OrderBy(s => s.TimeStamp).ToArray();
        if (!sessions.Any()) return sessions.AsEnumerable();
        var sessionTuples = sessions.Select(s => new { p = s, n = s }).Take(0).ToList();
        sessions.Aggregate((p, n) => { sessionTuples.Add(new { p, n }); return n; });
        return sessionTuples.OrderBy(st => _bestSessionCriteria(st.p) + _bestSessionCriteria(st.n)).Take(1)
          .Select(st => new[] { st.p, st.n }).DefaultIfEmpty(sessions).First()
          .OrderBy(_bestSessionCriteria).ThenBy(s => s.LotA).ThenByDescending(s => s.DollarPerLot);
      });
    }
    #endregion

    #region Test params setters
    private void SetTestCorridorDistanceRatio(IList<KeyValuePair<string,object>[]> testParamsRaw, DB.v_TradeSession[] sessions) {
      var testParam = sessions[0].CorridorDistanceRatio.Value;
      var a = sessions.OrderBy(s => s.TimeStamp).ToArray();
      var testParamStep = (a[1].CorridorDistanceRatio - a[0].CorridorDistanceRatio).Value.ToInt();
      var testParamCount = 5;
      var testParamStepMin = -testParamCount / 2;
      var startMin = (-(testParam.ToInt() / testParamStep) + 1).Max(testParamStepMin);
      var testParamName = "CorridorDistanceRatio";
      ReplaceTestParamRaw(testParamName, Enumerable.Range(startMin, testParamCount)
        .Select(r => new KeyValuePair<string, object>(testParamName, testParam + r * testParamStep)).ToArray());
    }
    #endregion

    CancellationTokenSource _replayTaskCancellationToken = new CancellationTokenSource();
    void StartReplayInternal(TradingMacro tmOriginal, TestParam testParameter, Action<Task> continueWith) {
      if (IsInVirtualTrading) {
        while (_replayTasks.ToArray().Any(t => t.Status == TaskStatus.Running)) {
          Log = new Exception("Replay is running.");
          Thread.Sleep(1000);
          continue;
        }
        _replayTasks.Clear();
        MasterModel.AccountModel.Balance = MasterModel.AccountModel.Equity = 50000;
        MasterModel.AccountModel.CurrentGrossInPips = 0;
        MasterModel.AccountModel.CurrentLoss = 0;
        TradesManager.GetAccount().Balance = TradesManager.GetAccount().Equity = 50000;
      }
        SaveTradingSettings(tmOriginal);
      var tms = GetTradingMacros().Where(t => t.Strategy != Strategies.None).ToList();
      ReplayArguments.SetTradingMacros(tms);
      ReplayArguments.GetOriginalBalance = new Func<double>(() => MasterModel.AccountModel.OriginalBalance);
      foreach (var tm in tms) {
        if (IsInVirtualTrading) {
          TradesManager.ClosePair(tm.Pair);
          tm.ResetSessionId(ReplayArguments.SuperSessionId);
          if (testParameter != null && tm == tmOriginal)
            testParameter.ForEach(tp => {
              try {
                tm.SetProperty(tp.Key, tp.Value);
              }catch(SetLotSizeException){
              } catch (Exception exc) {
                if(!(exc.InnerException is SetLotSizeException))
                  throw new Exception("Property:" + new { tp.Key, tp.Value }, exc);
              }
            });
        }
        var tmToRun = tm;
        tmToRun.ReplayCancelationToken = (_replayTaskCancellationToken = new CancellationTokenSource()).Token;
        var task = Task.Factory.StartNew(() => tmToRun.Replay(ReplayArguments), tmToRun.ReplayCancelationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        task.ContinueWith(t => {
          RxApp.MainThreadScheduler.Schedule(() => _replayTasks.Remove(t));
          if(tm == tmOriginal)
            continueWith(t);
        });
        _replayTasks.Add(task);
      }
    }

    void SaveTradingSettings(TradingMacro tmOriginal) {
      try {
        var attrs=  new[] { TradingMacro.categoryActive, TradingMacro.categoryActiveFuncs };
        tmOriginal.GetPropertiesByAttibute<CategoryAttribute>(a => attrs.Contains(a.Category))
          .GroupBy(a => a.Item2.Name).ToList().ForEach(g => {
          });

        //.ForEach(p => Debug.WriteLine("{0}={1}", p.Name, p.GetValue(tmOriginal, null)));
      } catch { }
    }
    #endregion

    class TestParam : IEnumerable<KeyValuePair<string, object>> {
      private IEnumerable<KeyValuePair<string, object>> _pairs;
      public TestParam(IEnumerable<KeyValuePair<string, object>> pairs) {
        _pairs = pairs;
      }

      public static implicit operator TestParam(KeyValuePair<string, object>[] rhs) {
        TestParam c = new TestParam( rhs); //Internally call Currency constructor
        return c;

      }
      #region IEnumerable<KeyValuePair<string,object>> Members

      public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
        return _pairs.GetEnumerator();
      }

      #endregion

      #region IEnumerable Members

      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
        return _pairs.GetEnumerator();
      }

      #endregion
    }
    Queue<TestParam> TestParams = new Queue<TestParam>();
    private List<KeyValuePair<string, object>[]> _testParamsRaw = new List<KeyValuePair<string,object>[]>();
  }
}
