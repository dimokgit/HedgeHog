/* Copyright (C) 2018 Interactive Brokers LLC. All rights reserved. This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */

using HedgeHog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IBApi {
  partial class OrderState {
    public enum OrderCancelStatuses { Cancelled, Inactive, PendingCancel };
    public enum OrderDoneStatuses { Filled };
    public override string ToString() => new { Status, WarningText } + "";
    public bool IsPreSubmited => Status == "PreSubmitted";
    public bool IsInactive => Status == "Inactive";
    public bool IsCancelled => EnumUtils.Contains<OrderCancelStatuses>(Status);
    public static bool IsCancelledState(string status) => EnumUtils.Contains<OrderCancelStatuses>(status);
    public static bool IsDoneState(string status, double remaining) => EnumUtils.Contains<OrderDoneStatuses>(status) && remaining == 0;
    public bool IsOrderDone(double remaining) => IsCancelled || IsDoneState(Status, remaining);

  }
}
