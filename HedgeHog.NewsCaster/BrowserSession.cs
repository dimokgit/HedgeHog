using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HedgeHog.NewsCaster {
  /// <summary>
  /// Represents a combined list and collection of Form Elements.
  /// </summary>
  public class FormElementCollection : NameValueCollection {
    public FormElementCollection() {

    }
    /// <summary>
    /// Constructor. Parses the HtmlDocument to get all form input elements. 
    /// </summary>
    public FormElementCollection(HtmlDocument htmlDoc) {
      var inputs = htmlDoc.DocumentNode.Descendants("input");
      foreach (var element in inputs) {
        string name = element.GetAttributeValue("name", "undefined");
        string value = element.GetAttributeValue("value", "");
        if (!name.Equals("undefined")) Add(name, value);
      }
    }

    /// <summary>
    /// Assembles all form elements and values to POST. Also html encodes the values.  
    /// </summary>
    public string AssemblePostPayload() {
      return string.Join("&",
        (from key in AllKeys
         from value in this[key].Split(',')
         .Select(s => System.Web.HttpUtility.UrlEncode(s))
         select System.Web.HttpUtility.UrlEncode(key) + "=" + value
         ));
    }
  }
  public class BrowserSession {
    private bool _isPost;
    private HtmlDocument _htmlDoc;

    /// <summary>
    /// System.Net.CookieCollection. Provides a collection container for instances of Cookie class 
    /// </summary>
    public CookieCollection Cookies { get; set; }

    /// <summary>
    /// Provide a key-value-pair collection of form elements 
    /// </summary>
    FormElementCollection _FormElements = new FormElementCollection();
    public FormElementCollection FormElements {
      get { return _FormElements; }
      set { _FormElements = value; }
    }

    /// <summary>
    /// Makes a HTTP GET request to the given URL
    /// </summary>
    public string Get(string url) {
      _isPost = false;
      CreateWebRequestObject().Load(url);
      return _htmlDoc.DocumentNode.InnerHtml;
    }

    /// <summary>
    /// Makes a HTTP POST request to the given URL
    /// </summary>
    public HtmlDocument Post(string url) {
      _isPost = true;
      CreateWebRequestObject().Load(url, "POST");
      return _htmlDoc;
    }

    /// <summary>
    /// Creates the HtmlWeb object and initializes all event handlers. 
    /// </summary>
    private HtmlWeb CreateWebRequestObject() {
      HtmlWeb web = new HtmlWeb();
      web.UseCookies = true;
      web.PreRequest = new HtmlWeb.PreRequestHandler(OnPreRequest);
      web.PostResponse = new HtmlWeb.PostResponseHandler(OnAfterResponse);
      web.PreHandleDocument = new HtmlWeb.PreHandleDocumentHandler(OnPreHandleDocument);
      return web;
    }

    /// <summary>
    /// Event handler for HtmlWeb.PreRequestHandler. Occurs before an HTTP request is executed.
    /// </summary>
    protected bool OnPreRequest(HttpWebRequest request) {
      AddCookiesTo(request);               // Add cookies that were saved from previous requests
      if (_isPost) AddPostDataTo(request); // We only need to add post data on a POST request
      return true;
    }

    /// <summary>
    /// Event handler for HtmlWeb.PostResponseHandler. Occurs after a HTTP response is received
    /// </summary>
    protected void OnAfterResponse(HttpWebRequest request, HttpWebResponse response) {
      SaveCookiesFrom(response); // Save cookies for subsequent requests
    }

    /// <summary>
    /// Event handler for HtmlWeb.PreHandleDocumentHandler. Occurs before a HTML document is handled
    /// </summary>
    protected void OnPreHandleDocument(HtmlDocument document) {
      SaveHtmlDocument(document);
    }

    /// <summary>
    /// Assembles the Post data and attaches to the request object
    /// </summary>
    private void AddPostDataTo(HttpWebRequest request) {
      string payload = FormElements.AssemblePostPayload();
      byte[] buff = Encoding.UTF8.GetBytes(payload.ToCharArray());
      request.ContentLength = buff.Length;
      request.ContentType = "application/x-www-form-urlencoded";
      System.IO.Stream reqStream = request.GetRequestStream();
      reqStream.Write(buff, 0, buff.Length);
    }

    /// <summary>
    /// Add cookies to the request object
    /// </summary>
    private void AddCookiesTo(HttpWebRequest request) {
      if (Cookies != null && Cookies.Count > 0) {
        request.CookieContainer.Add(Cookies);
      }
    }

    /// <summary>
    /// Saves cookies from the response object to the local CookieCollection object
    /// </summary>
    private void SaveCookiesFrom(HttpWebResponse response) {
      if (response.Cookies.Count > 0) {
        if (Cookies == null) Cookies = new CookieCollection();
        Cookies.Add(response.Cookies);
      }
    }

    /// <summary>
    /// Saves the form elements collection by parsing the HTML document
    /// </summary>
    private void SaveHtmlDocument(HtmlDocument document) {
      _htmlDoc = document;
      FormElements = new FormElementCollection(_htmlDoc);
    }
  }
}
