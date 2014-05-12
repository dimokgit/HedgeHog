using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading;
using ReactiveUI;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support;
using System.Windows;
//using WatiN.Core;
using System.Reactive.Concurrency;
using System.Threading;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Remote;
using System.Text.RegularExpressions;
using HedgeHog;

namespace TicketBuster {
  public class MainWIndowViewModel:ReactiveObject {
    #region fields
    IDisposable _searchSubscribtion;
    string _messageBox2 = "Results:";
    FirefoxDriver _ie;
    #endregion
    #region Properties
    bool _isInSearch;
    public bool IsInSearch {
      get { return _isInSearch; }
      set { this.RaiseAndSetIfChanged(ref _isInSearch, value); }
    }
    int _daysRange = 2;
    public int DaysRange {
      get { return _daysRange; }
      set { this.RaiseAndSetIfChanged(ref _daysRange, value); }
    }

    string _flight;
    public string Flight {
      get { return _flight; }
      set { this.RaiseAndSetIfChanged(ref  _flight, value); }
    }
    bool _mustStopSearch;
    public bool MustStopSearch {
      get { return _mustStopSearch; }
      set { this.RaiseAndSetIfChanged(ref _mustStopSearch, value); }
    }
    bool _isRoundTrip;
    public bool IsRoundTrip {
      get { return _isRoundTrip; }
      set { this.RaiseAndSetIfChanged(ref _isRoundTrip, value); }
    }
    DateTime _dateDepart = DateTime.Parse("12/4/2014");
    public DateTime DateDepart {
      get { return _dateDepart; }
      set { this.RaiseAndSetIfChanged(ref _dateDepart, value); }
    }
    DateTime? _dateReturn = null;
    public DateTime? DateReturn {
      get { return _dateReturn; }
      set { this.RaiseAndSetIfChanged(ref _dateReturn, value); }
    }
    bool _isNonstop = true;
    public bool IsNonstop {
      get { return _isNonstop; }
      set { this.RaiseAndSetIfChanged(ref _isNonstop, value); }
    }
    public ReactiveCommand Search { get; set; }
    FirefoxDriver ie { get { return _ie ?? (_ie = new FirefoxDriver()); } }
    public string MessageBox2 {
      get { return _messageBox2; }
      set { this.RaiseAndSetIfChanged(ref _messageBox2,value); }
    }
    EventLoopScheduler IESchedulerFactory() {
      return new EventLoopScheduler(ts => {
        var t = new Thread(ts) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        return t;
      });
    }
    #endregion
    public MainWIndowViewModel() {
      Search = new ReactiveCommand(this.ObservableForProperty(vm => vm.IsInSearch).Select(o => !o.Value));
      _searchSubscribtion = Search
        .ObserveOn(IESchedulerFactory())
        .Subscribe(_ => SearchFlight());
      this.ObservableForProperty(a => a.IsRoundTrip)
        .DistinctUntilChanged(b => b.Value)
        .Where(b => b.Value && DateReturn.GetValueOrDefault() < DateDepart)
        .Subscribe(_ => DateReturn = _dateDepart.AddDays(3));
    }
    void SearchFlight() {
      try {
        IsInSearch = true;
        if (!Enumerable.Range(0, 5).SkipWhile(_ => {
          ie.Navigate().GoToUrl("https://www.united.com/web/en-US/default.aspx?root=1");
          return !IsOnSearchPage(ie);
        }).Any()) throw new Exception("Couldn't navigate to search page.");
        FillSearch();
        while (!MustStopSearch && !Enumerable.Range(0, DaysRange)
          .Select(d => DateDepart.AddDays(d))
          .Where(_ => !MustStopSearch)
          .Select(d => new { d, l = FindAward(ie, Flight ?? "") })
          .SkipWhile(d => {
            if (d.l.Any()) return false;
            FillResultSearch(ie, d.d);
            return true;
          })
          .Do(d => { MessageBox2 = "Found award @ " + d.d.ToShortDateString(); })
          .Any()) { }
      } catch (Exception exc) {
        MessageBox2 = exc + "";
      } finally {
        MustStopSearch = IsInSearch = false;
      }
    }
    static bool IsFlightOk(string result, string flight) {
      return Regex.IsMatch(result, @"flight:\s+" + flight, RegexOptions.IgnoreCase);
    }
    private void FillSearch() {
      var searches = new WebDriverWait(ie, TimeSpan.FromSeconds(10)).Until(d => d.FindElements(By.ClassName("txtAirLoc")));
      searches[0].SendKeys("EWR");
      searches[1].SendKeys("TLV");
      ie.FindElementsByTagName("label").SingleOrDefault(FindByLastNamePart("for", '_', "rdosearchby3")).Click();
      if (!IsRoundTrip)
        ie.FindElementsByTagName("label").SingleOrDefault(FindByLastNamePart("for", '_', "rdoSearchType2")).Click();
      if (IsNonstop) {
        var label = ie.FindSingleLabelByLastNamePart("Direct_chkFltOpt");
        label.Click();
      }
      //var bc = ie.FindElementsByTagName("select").FirstOrDefault(FindByLastNamePart("name", '$', "cboCabin")); 
      //new SelectElement(bc).SelectByIndex(1);
      var dates = ie.FindElementsByClassName("txtDate");
      if (IsRoundTrip) {
        if (DateReturn <= DateDepart) throw new Exception("Return date must be after departure one.");
        dates[1].Clear();
        dates[1].SendKeys(string.Format("{0:M/d/yyyy}", CheckDate(DateReturn, "Return Date is missing.")));// + "\n");
      }
      dates[0].Clear();
      dates[0].SendKeys(string.Format("{0:M/d/yyyy}", DateDepart) + "\n");
      ie.SwitchTo().Alert().Accept();
      WaitForResultPage(ie);
    }

    private static void WaitForResultPage(RemoteWebDriver ie) {
      try {
        var title = new WebDriverWait(ie, TimeSpan.FromSeconds(30)).Until(d => {
          ie.SwitchTo().DefaultContent();
          return IsOnResultPage(ie);
        });
      } catch (Exception exc) {
        throw new Exception("Result page didn't come up in 30 seconds.");
      }
    }
    static void FillResultSearch(RemoteWebDriver ie, DateTime dateDepart) {
      var departTxt = new WebDriverWait(ie, TimeSpan.FromSeconds(10))
        .Until(_ => ie.FindElementByXPath("//input[contains(@name,'$Depdate$txtDptDate')]"));
      departTxt.Clear();
      departTxt.SendKeys(string.Format("{0:M/d/yyyy}", dateDepart) + "\n");
      WaitForResultPage(ie);
    }
    static IList<IWebElement> FindAward(RemoteWebDriver ie, string flight) {
      if(!IsOnResultPage(ie))throw new Exception("Navigate to Result page first.");
      var awardButtons = new WebDriverWait(ie, TimeSpan.FromSeconds(2)).Until(d => FindAwardButton(ie).Cast<FirefoxWebElement>().ToArray());
      return awardButtons;
    }

    private static FirefoxWebElement FindParentByTag(FirefoxWebElement we, string tag) {
      return WorkflowMixin.Y<FirefoxWebElement, FirefoxWebElement>(p => e => e.TagName == tag ? e : p(e.FindElementByXPath("..") as FirefoxWebElement))(we);
    }
    static IEnumerable<IWebElement> FindAwardButton(RemoteWebDriver ie) {
      return ie.FindElementsByXPath("//tr//input[contains(@class,'btnBlue')]");
    }
    static DateTime CheckDate(DateTime? date,string message) {
      if (!date.HasValue) throw new NullReferenceException(message);
      return date.Value;
    }
    private static Func<IWebElement, bool> FindByLastNamePart(string attr,char separator, string part) {
      return e => (e.GetAttribute(attr) ?? "").Split(separator).LastOrDefault() == part;
    }
    static bool IsOnSearchPage(RemoteWebDriver ie) {
      return ie.Title.StartsWith("United Airlines - Airline Tickets, Travel Deals and Flights");
    }
    static bool IsOnResultPage(RemoteWebDriver ie) {
      return ie.Title.ToLower().StartsWith("United Airlines - Flight Search".ToLower());
    }
    ~MainWIndowViewModel() {
      if (_searchSubscribtion != null) {
        _searchSubscribtion.Dispose();
        _searchSubscribtion = null;
      }
      if (_ie != null) {
        _ie.Dispose();
        _ie = null;
      }
    }
  }
  static class IWebDriverEx {
    private static Func<IWebElement, bool> FindByLastNamePart(string attr,  string part) {
      return e => (e.GetAttribute(attr) ?? "").ToLower().EndsWith( part.ToLower());
    }
    private static Func<IWebElement, bool> FindByLastNamePart(string attr, char separator, string part) {
      return e => (e.GetAttribute(attr) ?? "").Split(separator).LastOrDefault() == part;
    }
    public static IWebElement FindSingleLabelByLastNamePart(this RemoteWebDriver drv, string namePart) {
      try {
        return drv.FindElementsByTagName("label").SingleOrDefault(FindByLastNamePart("for", namePart));
      } catch (Exception exc) {
        throw new Exception(new { namePart } + " - multiple names end with it.");
      }
    }
  }
}
