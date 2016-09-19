using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace Westwind.Web.WebApi {

  /// <summary>
  /// Generic Basic Authentication filter that checks for basic authentication
  /// headers and challenges for authentication if no authentication is provided
  /// Sets the Thread Principle with a GenericAuthenticationPrincipal.
  /// 
  /// You can override the OnAuthorize method for custom auth logic that
  /// might be application specific.    
  /// </summary>
  /// <remarks>Always remember that Basic Authentication passes username and passwords
  /// from client to server in plain text, so make sure SSL is used with basic auth
  /// to encode the Authorization header on all requests (not just the login).
  /// </remarks>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
  public class BasicAuthenticationFilter : Microsoft.AspNet.SignalR.AuthorizeAttribute {
    public static AuthenticationSchemes AuthenticationSchemes { get; set; }
    bool Active = true;

    public BasicAuthenticationFilter() { }

    /// <summary>
    /// Overriden constructor to allow explicit disabling of this
    /// filter's behavior. Pass false to disable (same as no filter
    /// but declarative)
    /// </summary>
    /// <param name="active"></param>
    public BasicAuthenticationFilter(bool active) {
      Active = active;
    }
    public bool IsLocalRequest(HubCallerContext hubContext) {
      Func<string, IEnumerable<string>> ipBase = ip => (hubContext.Request.Environment[ip] + "").Split('.').Take(2);
      return ipBase("server.LocalIpAddress").SequenceEqual(ipBase("server.RemoteIpAddress"));
    }
    public override bool AuthorizeHubConnection(HubDescriptor hubDescriptor, IRequest request) {
      return true;// base.AuthorizeHubConnection(hubDescriptor, request);
    }
    public override bool AuthorizeHubMethodInvocation(IHubIncomingInvokerContext hubIncomingInvokerContext, bool appliesToMethod) {
      return
        IsLocalRequest(hubIncomingInvokerContext.Hub.Context) ||
        !appliesToMethod ||
        ((HttpListener)hubIncomingInvokerContext.Hub.Context.Request.Environment["System.Net.HttpListener"]).AuthenticationSchemes == AuthenticationSchemes.Anonymous ||
        (
        hubIncomingInvokerContext.Hub.Context.User != null &&
        hubIncomingInvokerContext.Hub.Context.User.IsInRole("Traders")
        )
      ;//OnAuthorization(hubIncomingInvokerContext);
    }
    protected override bool UserAuthorized(IPrincipal user) {
      return true;// base.UserAuthorized(user);
    }

    /// <summary>
    /// Override to Web API filter method to handle Basic Auth check
    /// </summary>
    /// <param name="actionContext"></param>
    public bool OnAuthorization(IHubIncomingInvokerContext actionContext) {
      {
        var identity = ParseAuthorizationHeader(actionContext);
        if(identity == null) {
          return false;
        }


        //if (!OnAuthorizeUser(identity.Name, identity.Password, actionContext))
        //{
        //    return;
        //}

        var principal = new GenericPrincipal(identity, null);

        Thread.CurrentPrincipal = principal;

        // inside of ASP.NET this is also required for some async scenarios
        //if (HttpContext.Current != null)
        //    HttpContext.Current.User = principal;

        ///base.OnAuthorization(actionContext);
        return true;
      }
    }

    /// <summary>
    /// Base implementation for user authentication - you probably will
    /// want to override this method for application specific logic.
    /// 
    /// The base implementation merely checks for username and password
    /// present and set the Thread principal.
    /// 
    /// Override this method if you want to customize Authentication
    /// and store user data as needed in a Thread Principle or other
    /// Request specific storage.
    /// </summary>
    /// <param name="username"></param>
    /// <param name="password"></param>
    /// <param name="actionContext"></param>
    /// <returns></returns>
    protected virtual bool OnAuthorizeUser(string username, string password, HttpActionContext actionContext) {
      if(string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return false;

      return true;
    }

    /// <summary>
    /// Parses the Authorization header and creates user credentials
    /// </summary>
    /// <param name="actionContext"></param>
    protected virtual BasicAuthenticationIdentity ParseAuthorizationHeader(IHubIncomingInvokerContext actionContext) {
      string authHeader = null;
      var auth = actionContext.Hub.Context.Request.Headers["Authorization"];
      if(auth != null && auth.Contains("Basic"))
        authHeader = Regex.Split(auth, "Basic").Last();

      if(string.IsNullOrEmpty(authHeader))
        return null;

      authHeader = Encoding.Default.GetString(Convert.FromBase64String(authHeader));

      // find first : as password allows for :
      int idx = authHeader.IndexOf(':');
      if(idx < 0)
        return null;

      string username = authHeader.Substring(0, idx);
      string password = authHeader.Substring(idx + 1);

      return new BasicAuthenticationIdentity(username, password);
    }


    /// <summary>
    /// Send the Authentication Challenge request
    /// </summary>
    /// <param name="message"></param>
    /// <param name="actionContext"></param>
    void Challenge(HttpActionContext actionContext) {
      var host = actionContext.Request.RequestUri.DnsSafeHost;
      actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Unauthorized);
      actionContext.Response.Headers.Add("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", host));
    }

  }
}
