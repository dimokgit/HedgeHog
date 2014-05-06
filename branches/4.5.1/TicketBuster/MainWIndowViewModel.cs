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

namespace TicketBuster {
  public class MainWIndowViewModel:ReactiveObject {
    public ReactiveCommand Search { get; set; }
    string _messageBox2;
    FirefoxDriver _ie;
    FirefoxDriver ie { get { return _ie ?? (_ie = new FirefoxDriver()); } }
    public string MessageBox2 {
      get { return _messageBox2; }
      set { this.RaiseAndSetIfChanged(ref _messageBox2,value); }
    }
    IDisposable _searchSubscribtion;
    EventLoopScheduler IESchedulerFactory() {
      return new EventLoopScheduler(ts => {
        var t = new Thread(ts) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        return t;
      });
    }
    public MainWIndowViewModel() {
      Search = new ReactiveCommand();
      _searchSubscribtion = Search
        .ObserveOn(IESchedulerFactory())
        .Subscribe(_ => {
          try {
            ie.Navigate().GoToUrl("https://www.united.com/web/en-US/default.aspx?root=1");
            var searches = new WebDriverWait(ie, TimeSpan.FromSeconds(10)).Until(d => d.FindElements(By.ClassName("txtAirLoc")));
            searches[0].SendKeys("EWR");
            searches[1].SendKeys("TLV");
            var bc = ie.FindElementsByTagName("select").FirstOrDefault(e => (e.GetAttribute("name") ?? "").Split('$').LastOrDefault() == "cboCabin"); 
            new SelectElement(bc).SelectByIndex(1);
            var dates = ie.FindElementsByClassName("txtDate");
            dates[0].SendKeys(string.Format("{0:M/d/yyyy}", DateTime.Now.Date.AddDays(2)) + "");
            dates[1].SendKeys(string.Format("{0:M/d/yyyy}", DateTime.Now.Date.AddDays(2+7)) + "\n");
            ie.SwitchTo().Alert().Accept();
            //ie.FindElementByXPath("input[@type='submit']").SendKeys("\n");
          } catch (Exception exc) {
            MessageBox2 = exc + "";
          }
        });
      this.ObservableForProperty(vm => vm.MessageBox2).ObserveOnDispatcher().Subscribe(o => MessageBox.Show(o.Value));
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
}
