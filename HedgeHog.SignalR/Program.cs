using System;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using Owin;
using Microsoft.Owin.Cors;
using System.Threading.Tasks;

namespace HedgeHog.SignalR {
  class Program {
    static void Main(string[] args) {
      // This will *ONLY* bind to localhost, if you want to bind to all addresses
      // use http://*:8080 to bind to all addresses. 
      // See http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx 
      // for more information.
      string url = "http://+:80/";
      using (WebApp.Start(url)) {
        Console.WriteLine("Server running on {0}", url);
        Console.ReadLine();
      }
    }
  }
  class Startup {
    public void Configuration(IAppBuilder app) {
      app.UseCors(CorsOptions.AllowAll);
      app.Use((context,next) => {
        Console.WriteLine(context.Request.Path.Value);
        if (context.Request.Path.Value.ToLower() == "/hello") {
          return context.Response.WriteAsync("privet");
        }
        if (context.Request.Query.ToString() == "throw")
          throw new Exception("thrown");
        return next();
      });
      app.MapSignalR();
    }
  }
  public class MyHub : Hub {
    public void Send(string name, string message) {
      Clients.All.addMessage(name, message);
    }
  }
}