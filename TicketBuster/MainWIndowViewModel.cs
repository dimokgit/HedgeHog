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
using System.IO;

namespace TicketBuster {
  public class PersistAttribute : Attribute { }
  public class MainWIndowViewModel:ReactiveObject {
    #region fields
    string _modelKey;
    IDisposable _searchSubscribtion;
    string _messageBox2 = "Results:";
    FirefoxDriver _ie;
    #endregion
    #region Properties

    #region AirportFrom
    private string _AirportFrom = "EWR";
    [Persist]
    public string AirportFrom {
      get { return _AirportFrom; }
      set { this.RaiseAndSetIfChanged(ref _AirportFrom, value.ToUpper()); }      
    }
    #endregion
    #region AirportTo
    private string _AirportTo = "TLV";
    [Persist]
    public string AirportTo {
      get { return _AirportTo; }
      set { this.RaiseAndSetIfChanged(ref _AirportTo, value.ToUpper()); }
    }
    #endregion

    #region MainWindowTop
    private int _MainWindowTop;
    [Persist]
    public int MainWindowTop {
      get { return _MainWindowTop; }
      set { this.RaiseAndSetIfChanged(ref _MainWindowTop, value); }
    }
    #endregion
    #region MainWindowLeft
    private int _MainWindowLeft;
    [Persist]
    public int MainWindowLeft {
      get { return _MainWindowLeft; }
      set { this.RaiseAndSetIfChanged(ref _MainWindowLeft, value); }
    }
    #endregion
    #region MainWindowWidth
    private int _MainWindowWidth = 300;
    [Persist]
    public int MainWindowWidth {
      get { return _MainWindowWidth; }
      set { this.RaiseAndSetIfChanged(ref _MainWindowWidth, value); }
    }
    #endregion
    #region MainWindowHeight
    private int _MainWindowHeight = 400;
    [Persist]
    public int MainWindowHeight {
      get { return _MainWindowHeight; }
      set { this.RaiseAndSetIfChanged(ref _MainWindowHeight, value); }
    }
    #endregion

    #region RetryCountdown
    private double? _RetryCountdown;
    public double? RetryCountdown {
      get { return _RetryCountdown > 0 ? _RetryCountdown : null; }
      set { this.RaiseAndSetIfChanged(ref _RetryCountdown, value); }
    }
    #endregion
    #region RetryInterval
    private double _RetryInterval;
    [Persist]
    public double RetryInterval {
      get { return _RetryInterval; }
      set { this.RaiseAndSetIfChanged(ref _RetryInterval, value); }
    }
    #endregion
    bool _isInSearch;
    public bool IsInSearch {
      get { return _isInSearch; }
      set { this.RaiseAndSetIfChanged(ref _isInSearch, value); }
    }
    int _daysRange = 2;
    [Persist]
    public int DaysRange {
      get { return _daysRange; }
      set { this.RaiseAndSetIfChanged(ref _daysRange, value); }
    }

    string _flight;
    [Persist]
    public string Flight {
      get { return _flight; }
      set { this.RaiseAndSetIfChanged(ref  _flight, value.ToUpper()); }
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
    DateTime _dateDepart = DateTime.Now.AddDays(7);
    [Persist]
    public DateTime DateDepart {
      get { return _dateDepart; }
      set { this.RaiseAndSetIfChanged(ref _dateDepart, value); }
    }
    DateTime? _dateReturn = null;
    [Persist]
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
    string _alert;
    public string Alert {
      get { return _alert; }
      set { _alert = null; this.RaiseAndSetIfChanged(ref _alert, value); }
    }
    EventLoopScheduler _IEScheduler;
    EventLoopScheduler IESchedulerFactory() {
      return _IEScheduler ?? (_IEScheduler = new EventLoopScheduler(ts => {
        var t = new Thread(ts) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        return t;
      }));
    }
    #endregion
    #region Load/Save Properties
    string ActiveSettingsPath() { return Lib.CurrentDirectory + "\\{0}_Last.txt".Formater(_modelKey); }
    void LoadActiveSettings() { LoadActiveSettings(ActiveSettingsPath()); }
    public void LoadActiveSettings(string path) {
      try {
        if (!File.Exists(path)) return;
        var settings = Lib.ReadTestParameters(path);
        settings.ForEach(tp => this.SetProperty(tp.Key, (object)tp.Value));
      } catch (Exception exc) {
        MessageBox2 = exc + "";
      }
    }
    void SaveActiveSettings() {
      try {
        string path = ActiveSettingsPath();
        SaveActiveSettings(path);
      } catch (Exception exc) { MessageBox2 = exc + ""; }
    }
    public void SaveActiveSettings(string path) {
      File.WriteAllLines(path, GetActiveSettings().ToArray());
    }
    IEnumerable<string> GetActiveSettings() {
      return
        from setting in this.GetPropertiesByAttibute<PersistAttribute>(a => true)
        group setting by "Settings" into g
        orderby g.Key
        from g2 in new[] { "//{0}//".Formater(g.Key) }
        .Concat(g.Select(p => "{0}={1}".Formater(p.Item2.Name, p.Item2.GetValue(this, null))).OrderBy(s => s))
        .Concat(new[] { "\n" })
        select g2;
    }
    #endregion
    #region ctor
    public MainWIndowViewModel(string modelKey) {
      _modelKey = modelKey;
      LoadActiveSettings();
      Search = new ReactiveCommand(this.WhenAnyValue(vm => vm.IsInSearch, vm => vm.RetryCountdown, (b1, b2) => !b1 && b2.GetValueOrDefault() <= 0));
      _searchSubscribtion = Search
        .ObserveOn(IESchedulerFactory())
        .Subscribe(_ => SearchFlight());
      this.ObservableForProperty(a => a.IsRoundTrip)
        .DistinctUntilChanged(b => b.Value)
        .Where(b => b.Value && DateReturn.GetValueOrDefault() < DateDepart)
        .Subscribe(_ => DateReturn = _dateDepart.AddDays(3));
    }
    ~MainWIndowViewModel() {
      if (_searchSubscribtion != null) {
        _searchSubscribtion.Dispose();
        _searchSubscribtion = null;
      }
      DisposeBrowser();
      SaveActiveSettings();
    }
    #endregion
    void StartRetryCountdown() {
      Observable.Generate(
        RetryCountdown = RetryInterval,
        ri => ri >= 0 && !MustStopSearch,
        ri => RetryCountdown = TimeSpan.FromMinutes(ri.GetValueOrDefault()).Subtract(TimeSpan.FromSeconds(1)).TotalMinutes,
        d => d,
        d => TimeSpan.FromSeconds(1)
        )
        .Do(_=>MessageBox2 = "Idling ...")
        .TakeLast(1)
        .Where(d => d <= 0)
        .Subscribe(
        _ => {
          IsInSearch = true;
          Observable.Start(SearchFlight, IESchedulerFactory());
        },
        () => {
          RetryCountdown = 0;
          MustStopSearch = false;
        });
//      Observable.Range(0,RetryInterval*10).in
    }
    void SearchFlight() {
      try {
        IsInSearch = true;
        MessageBox2 = "Starting browser ...";
        DisposeBrowser();
        if (!Enumerable.Range(0, 5).SkipWhile(_ => {
          ie.Navigate().GoToUrl("https://www.united.com/web/en-US/default.aspx?root=1");
          return !IsOnSearchPage(ie);
        }).Any()) throw new Exception("Couldn't navigate to search page.");
        Enumerable.Range(0, DaysRange)
          .Where(_ => !MustStopSearch)
          .Select((d, i) => new { d = DateDepart.AddDays(d), i })
          .Do(a => {
            MessageBox2 = "Searching ...";
            if (a.i == 0) FillSearch();
            else FillResultSearch(ie, a.d);
          })
          .Select(d => new { d.d, we = FindAward(ie, Flight ?? "") })
          .SkipWhile(d =>
            !(d.we.Any(we => string.IsNullOrWhiteSpace(Flight) || IsFlightOk(FindParentByTag(we, "tr").Text, Flight)))
          )
          .ForEach(d => {
            Alert = MessageBox2 = "Found award @ " + d.d.ToShortDateString();
            MustStopSearch = true;
          });
      } catch (Exception exc) {
        MessageBox2 = Alert = exc + "";
        MustStopSearch = true;
      } finally {
        if (!MustStopSearch) StartRetryCountdown();
        MustStopSearch = IsInSearch = false;
      }
    }
    static bool IsFlightOk(string result, string flight) {
      return Regex.IsMatch(result, @"flight:\s+" + flight, RegexOptions.IgnoreCase);
    }
    private void FillSearch() {
      var searches = new WebDriverWait(ie, TimeSpan.FromSeconds(10)).Until(d => d.FindElements(By.ClassName("txtAirLoc")));
      if (new[] { AirportFrom, AirportTo }.Any(a => string.IsNullOrWhiteSpace(a))) {
        Alert = "Fill From and To airports.";
        throw new Exception("Fill From and To airports.");
      }
      searches[0].SendKeys(AirportFrom);
      searches[1].SendKeys(AirportTo);
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
    static IList<FirefoxWebElement> FindAward(RemoteWebDriver ie, string flight) {
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
    private void DisposeBrowser() {
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
