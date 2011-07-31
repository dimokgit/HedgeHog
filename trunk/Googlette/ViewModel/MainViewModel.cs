using System;
using System.Windows;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using WC = WatiN.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;

namespace Googlette.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    public class MainViewModel : ViewModelBase
    {

      [DllImport("user32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
      internal static extern IntPtr SetFocus(IntPtr hwnd);

      public string Url { get; set; }
      #region InPause
      private bool _InPause;
      public bool InPause {
        get { return _InPause; }
        set {
          if (_InPause != value) {
            _InPause = value;
            RaisePropertyChanged("InPause");
            //System.Windows.Forms.WebBrowser.FromHandle(Browser.NativeBrowser.hWnd).Focus();
            //SetFocus(Browser.NativeBrowser.hWnd);
          }
        }
      }

      #endregion
      #region IsSearchRunning
      private bool _IsSearchRunning;
      public bool IsSearchRunning {
        get { return _IsSearchRunning; }
        set {
          if (_IsSearchRunning != value) {
            _IsSearchRunning = value;
            RaisePropertyChanged("IsSearchRunning");
          }
        }
      }

      #endregion
      #region Search
      private string _SearchText;
      public string SearchText {
        get { return _SearchText; }
        set {
          if (_SearchText != value) {
            _SearchText = value;
            RaisePropertyChanged("Search");
          }
        }
      }

      #endregion
      #region Ctor
        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            ////if (IsInDesignMode)
            ////{
            ////    // Code runs in Blend --> create design time data.
            ////}
            ////else
            ////{
            ////    // Code runs "for real"
            ////}
        }
        ~MainViewModel() {
        }

      #endregion

        #region Commands
        ICommand _StartSearchCommand;
        public ICommand StartSearchCommand {
          get {
            if (_StartSearchCommand == null) {
              _StartSearchCommand = new RelayCommand(StartSearch, () => true);
            }

            return _StartSearchCommand;
          }
        }
        void StartSearch() {
          if (!IsSearchRunning) {
            IsSearchRunning = true;
            var t = new Thread(new ThreadStart(Search));
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
          } else
            IsSearchRunning = false;
        }
        void Search() {
          while (IsSearchRunning) {
            using (var browser = new WC.IE("doodle.com")) {
              browser.GoTo("www.google.com");
              var text = browser.TextField(WC.Find.ByTitle("Search"));
              text.Value = SearchText;
              browser.WaitForComplete();
              var counter = 3;
              while (counter-- > 0 && !Click(browser)) { }
              if (counter < 0) {
                ClickTheLink(browser);
                Thread.Sleep(1000);
              }
            }
          }
        }


        Func<string,string,bool> IsTheUrl { get { return new Func<string,string,bool>((s,u) => s.ToLower().Contains(u.ToLower())); } }
        Predicate<WC.Link> IsTheLink { get { return new Predicate<WC.Link>(l => IsTheUrl(l.Url, Url)); } }
        private bool Click(WC.Browser browser) {
          try {
            var list = GetList(browser);
            var listItems = list.ListItems;
            var listList = new List<WC.ListItem>(listItems);
            var rand = new Random();
            var index = rand.Next(listItems.Count);
            var link = listItems[index].Links.First();
            var mustBreak = (IsTheLink(link));
            try {
              link.SetAttributeValue("target", "_blank");
              link.Click();
              Thread.Sleep(1000);
              browser.WaitForComplete();
              Thread.Sleep(1000);
            } catch { }
            if (mustBreak)
              Thread.Sleep(2000);
            while (InPause)
              Thread.Sleep(1000);
            SendKeys.SendWait("^{F4}");
            return mustBreak;
          } catch {
            return false;
          }
        }

        private WC.Link GetTheLink(WC.Browser browser ) {
          return GetList(browser).Link(IsTheLink);
        }
        void ClickTheLink(WC.Browser browser) {
          try {
            GetTheLink(browser).Click();
          } catch { }
        }

        private static WC.List GetList(WC.Browser browser) {
          var list = browser.List("rso");
          list.WaitForComplete();
          list.WaitUntil(l => l.ListItems.Count > 0);
          return list;
        }


        #region OnWindowClose
        ICommand _OnWindowCloseCommand;
        public ICommand OnWindowCloseCommand {
          get {
            if (_OnWindowCloseCommand == null) {
              _OnWindowCloseCommand = new RelayCommand(OnWindowClose, () => true);
            }

            return _OnWindowCloseCommand;
          }
        }
        void OnWindowClose() {
        }
        #endregion


        #endregion
    }

  }