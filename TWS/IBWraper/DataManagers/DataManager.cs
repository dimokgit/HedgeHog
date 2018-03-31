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

namespace IBApp {
  public abstract class DataManager {

    #region Fields/Properties
    private int currentTicker = 0;
    private readonly int _baseReqId;
    public static bool UseVerbose = false;

    protected Action<object> Trace { get; }
    protected Action<object> Verbose => o => { if(UseVerbose) Trace(o); };
    protected IBClientCore IbClient { get; private set; }
    #endregion


    #region Ctor
    public DataManager(IBClientCore ibClient, int baseReqId) {
      IbClient = ibClient;
      _baseReqId = baseReqId;
      Trace = IbClient.Trace;
    }
    #endregion

    protected int NextReqId() => _baseReqId + Interlocked.Increment(ref currentTicker);
    protected int CurrReqId() => _baseReqId + currentTicker;

    public override string ToString() => new { IbClient } + "";
  }
}
