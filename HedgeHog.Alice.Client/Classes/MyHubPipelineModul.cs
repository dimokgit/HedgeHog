using HedgeHog.Shared;
using Microsoft.AspNet.SignalR.Hubs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.Alice.Client {
  public class MyHubPipelineModule2 : HubPipelineModule {
    protected override void OnIncomingError(ExceptionContext exceptionContext, IHubIncomingInvokerContext invokerContext) {
      dynamic caller = invokerContext.Hub.Clients.Caller;
      caller.ExceptionHandler(exceptionContext.Error.Message);
      GalaSoft.MvvmLight.Messaging.Messenger.Default.Send<LogMessage>(new LogMessage(exceptionContext.Error));
      //exceptionContext.Result = new { error = exceptionContext.Error.Message };
    }
  }
}
