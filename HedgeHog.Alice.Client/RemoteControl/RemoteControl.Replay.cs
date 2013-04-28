using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Gala = GalaSoft.MvvmLight.Command;
using HedgeHog.Shared;
using HedgeHog.Alice.Store;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;

namespace HedgeHog.Alice.Client {
  public partial class RemoteControlModel {
    #region StartReplayCommand
    ICommand _StartReplayCommand;
    public ICommand StartReplayCommand {
      get {
        if (_StartReplayCommand == null) {
          _StartReplayCommand = new Gala.RelayCommand<TradingMacro>(StartReplay, tm => !_replayTasks.Any(t => t.Status == TaskStatus.Running));
        }
        return _StartReplayCommand;
      }
    }
    ReplayArguments _replayArguments = new ReplayArguments();
    public ReplayArguments ReplayArguments {
      get { return _replayArguments; }
    }

    class TestParameter {
      public int PriceCmaLevel { get; set; }
      public double ProfitToLossExitRatio { get; set; }
      public double CorridorDistanceRatio { get; set; }
      public double WaveStDevRatio { get; set; }
      public int BarsCount { get; set; }
      public double DistanceIterations { get; set; }
      public Guid SuperessionId { get; set; }
      public double CorrelationMinimum { get; set; }
    }

    static class TestParameters {
      public static int[] PriceCmaLevels = new int[0];
      public static double[] ProfitToLossExitRatio = new double[0];
      public static double[] CorridorDistanceRatio = new double[0];
      public static double[] WaveStDevRatio = new double[0];
      public static int[] BarsCount = new int[0];
      public static double[] DistanceIterations = new double[0];
      public static double[] CorrelationMinimum = new double[0];

      public static Queue<TestParameter> GenerateTestParameters(Guid superSessionId) {
        var ret = from p in PriceCmaLevels
                  from cm in CorrelationMinimum
                  from rhm in DistanceIterations
                  from wr in WaveStDevRatio
                  from pl in ProfitToLossExitRatio
                  from cd in CorridorDistanceRatio
                  from pler in BarsCount
                  select new TestParameter() { PriceCmaLevel = p
                    , CorrelationMinimum = cm
                    , ProfitToLossExitRatio = pl
                    , CorridorDistanceRatio = cd
                    , BarsCount = pler
                    , SuperessionId = superSessionId
                    , DistanceIterations = rhm
                    , WaveStDevRatio = wr };
        return new Queue<TestParameter>(ret);
      }
    }
    List<Task> _replayTasks = new List<Task>();
    void StartReplay(TradingMacro tmOriginal) {
      if (_replayTasks.Any(t => t.Status == TaskStatus.Running)) {
        MessageBox.Show("Replay is running.");
        return;
      }

      ReplayArguments.UseSuperSession = tmOriginal.TestUseSuperSession;
      ReplayArguments.SuperSessionId = tmOriginal.TestSuperSessionUid.ValueOrDefault(Guid.NewGuid());

      if (tmOriginal.UseTestFile) {
        var paramsDict = Lib.ReadTestParameters();
        foreach (var p in paramsDict) {
          tmOriginal.SetProperty(p.Key, p.Value);
        }
      }
      {
        var c = new[] { ',', ' ', '\t' };
        TestParameters.PriceCmaLevels = tmOriginal.TestPriceCmaLevels.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s))
          .DefaultIfEmpty(tmOriginal.PriceCmaLevels).ToArray();
        TestParameters.ProfitToLossExitRatio = tmOriginal.TestProfitToLossExitRatio.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s))
          .DefaultIfEmpty(tmOriginal.ProfitToLossExitRatio).ToArray();
        TestParameters.CorridorDistanceRatio = tmOriginal.TestCorridorDistanceRatio.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s))
          .DefaultIfEmpty(tmOriginal.CorridorDistanceRatio).ToArray();
        TestParameters.WaveStDevRatio = tmOriginal.TestWaveStDevRatio.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s))
          .DefaultIfEmpty(tmOriginal.WaveStDevRatio).ToArray();
        TestParameters.BarsCount = tmOriginal.TestBarsCount.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s))
          .DefaultIfEmpty(tmOriginal.BarsCount).ToArray();
        TestParameters.CorrelationMinimum = tmOriginal.TestCorrelationMinimum.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s))
          .DefaultIfEmpty(tmOriginal.CorrelationMinimum).ToArray();
        TestParameters.DistanceIterations = tmOriginal.TestDistanceIterations.ParseParamRange().Split(c, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s))
          .DefaultIfEmpty(tmOriginal.DistanceIterations).ToArray();
      }
      if (ReplayArguments.UseSuperSession) {
        ReplayArguments.DateStart = ReplayArguments.DateStart ?? new Lazy<DateTime>(() => {
          try {
            var sessions = GetBestSessions(ReplayArguments.SuperSessionId).ToArray();
            if (sessions.Any()) SetTestCorridorDistanceRatio(sessions);
            else throw new Exception("Either ReplayArguments.DateStart or valid Supersession Uid must be provided.");
            return sessions.Min(s => s.DateStart.Value).AddDays(5);
          } catch (Exception exc) {
            Log = exc;
            throw;
          }
        }).Value;
      }
      var testQueue = TestParameters.GenerateTestParameters(ReplayArguments.SuperSessionId);
      StartReplayInternal(tmOriginal, testQueue.Any() ? testQueue.Dequeue() : null, task => { ContinueReplayWith(tmOriginal, testQueue); });
    }
    void ContinueReplayWith(TradingMacro tm, Queue<TestParameter> testParams) {
      if (tm.Strategy == Strategies.None) return;
      if (testParams.Any()) {
        StartReplayInternal(tm, testParams.Dequeue(), t => { ContinueReplayWith(tm, testParams); });
      } else if (ReplayArguments.UseSuperSession) {
        var super = GetBestSessions(ReplayArguments.SuperSessionId).ToArray();
        SetTestCorridorDistanceRatio(super);
        ReplayArguments.SuperSessionId = Guid.NewGuid();
        ReplayArguments.DateStart = ReplayArguments.DateStart.Value.AddDays(6);
        var testQueue = TestParameters.GenerateTestParameters(ReplayArguments.SuperSessionId);
        StartReplayInternal(tm, testQueue.Any() ? testQueue.Dequeue() : null, task => { ContinueReplayWith(tm, testQueue); });
      }
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
    private void SetTestCorridorDistanceRatio(DB.v_TradeSession[] sessions) {
      var testParam = sessions[0].CorridorDistanceRatio.Value;
      var a = sessions.OrderBy(s => s.TimeStamp).ToArray();
      var testParamStep = (a[1].CorridorDistanceRatio - a[0].CorridorDistanceRatio).Value.ToInt();
      var testParamCount = 5;
      var testParamStepMin = -testParamCount / 2;
      var startMin = (-(testParam.ToInt() / testParamStep) + 1).Max(testParamStepMin);
      TestParameters.CorridorDistanceRatio = Enumerable.Range(startMin, testParamCount).Select(r => testParam + r * testParamStep).ToArray();
    }
    #endregion

    CancellationTokenSource _replayTaskCancellationToken = new CancellationTokenSource();
    void StartReplayInternal(TradingMacro tmOriginal, TestParameter testParameter, Action<Task> continueWith) {
      if (IsInVirtualTrading) {
        if (_replayTasks.Any(t => t.Status == TaskStatus.Running)) {
          MessageBox.Show("Replay is running.");
          return;
        }
        _replayTasks.Clear();
        MasterModel.AccountModel.Balance = MasterModel.AccountModel.Equity = 50000;
        tradesManager.GetAccount().Balance = tradesManager.GetAccount().Equity = 50000;
      }
      var tms = GetTradingMacros().Where(t => t.Strategy != Strategies.None).ToList();
      ReplayArguments.SetTradingMacros(tms);
      foreach (var tm in tms) {
        if (IsInVirtualTrading) {
          tradesManager.ClosePair(tm.Pair);
          tm.ResetSessionId(ReplayArguments.SuperSessionId);
          if (testParameter != null) {
            tm.PriceCmaLevels_ = testParameter.PriceCmaLevel;
            tm.CorrelationMinimum = testParameter.CorrelationMinimum;
            tm.ProfitToLossExitRatio = testParameter.ProfitToLossExitRatio;
            tm.CorridorDistanceRatio = testParameter.CorridorDistanceRatio;
            tm.WaveStDevRatio = testParameter.WaveStDevRatio;
            tm.BarsCount = testParameter.BarsCount;
            tm.DistanceIterations = testParameter.DistanceIterations;
          }
        }
        var tmToRun = tm;
        tmToRun.ReplayCancelationToken = (_replayTaskCancellationToken = new CancellationTokenSource()).Token;
        var task = Task.Factory.StartNew(() => tmToRun.Replay(ReplayArguments), tmToRun.ReplayCancelationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        task.ContinueWith(continueWith);
        _replayTasks.Add(task);
      }
    }
    #endregion
  }
}
