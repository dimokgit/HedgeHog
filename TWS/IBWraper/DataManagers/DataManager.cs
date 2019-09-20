/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBSampleApp.util;
using static HedgeHog.Core.JsonExtensions;
using HedgeHog.Shared;
using System.Threading;
using System.Diagnostics;
using HedgeHog;

namespace IBApp {
  public abstract class DataManager {

    #region Fields/Properties
    public static bool UseVerbose = false;
    public static bool DoShowRequestErrorDone = true;
    public static IBClientCore IBClientMaster;
    private IBClientCore _ibClient;

    public static string ShowThread() => $"~{Thread.CurrentThread.ManagedThreadId}{Thread.CurrentThread.Name.With(tn => tn.IsNullOrEmpty() ? "" : (":" + tn))}";
    public void TraceError<T>(T v) => Trace("{!}" + v + ShowThread());
    public void TraceDebug<T>(T v) { if(Debugger.IsAttached) Trace("{?}" + v+ShowThread()); }
    public void TraceDebug0<T>(T v) { }
    protected Action<object> Trace { get; }
    protected Action<bool, object> TraceIf => (b, o) => { if(b) Trace(o); };
    protected Action<object> Verbose => o => { if(UseVerbose) Trace(o); };
    protected Action<object> Verbose0 => o => { };
    protected IBClientCore IbClient {
      get => _ibClient;
      private set {
        if(IBClientMaster==null)
          IBClientMaster=value;
        _ibClient = value;
      }
    }
    #endregion


    #region Ctor
    public DataManager(IBClientCore ibClient) {
      IbClient = ibClient;
      Trace = IbClient.Trace;
    }
    #endregion

    public override string ToString() => new { IbClient } + "";
  }
}
