using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Configuration;
using System.Data.Entity.Infrastructure;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using HedgeHog;
using LINQtoCSV;
using Manheim.Web;
using WC = WatiN.Core;
using System.IO;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Core.Objects.DataClasses;
namespace Manheim.ViewModel {
  public class ToSelectedStateConverter : IValueConverter {
    private static readonly ToSelectedStateConverter defaultInstance = new ToSelectedStateConverter();
    public static ToSelectedStateConverter Default { get { return defaultInstance; } }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      var vml = parameter as Manheim.ViewModel.ViewModelLocator;
      if (vml != null)
        vml.Main.SelectedAuction = value;
      return value;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      var dc = parameter as Manheim.ViewModel.MainViewModel;
      if(dc !=null)
        dc.SelectedAuction = value;
      return value;
    }
  }
  public class Auction:ViewModelBase {
    public string State { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    #region IsSelected
    private bool _IsSelected;
    public bool IsSelected {
      get { return _IsSelected; }
      set {
        if (_IsSelected != value) {
          _IsSelected = value;
          RaisePropertyChanged("IsSelected");
        }
      }
    }
    
    #endregion
    public Auction(string state,string name,string url) {
      this.State = state;
      this.Name = name;
      this.Url = url;
    }
    public override string ToString() {
      return State + ": " + Name;
    }
  }
  public class StateAuctions {
    public string State { get; set; }
    public List<Auction> Auctions { get; set; }
    public StateAuctions(string state,List<WC.ListItem> auctions) {
      this.State = state;
      this.Auctions = auctions.Select(a => new Auction(state,a.Text.Trim(), a.Links.First().Url)).ToList();
    }
  }
  /// <summary>
  /// This class contains properties that the main View can data bind to.
  /// <para>
  /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
  /// </para>
  /// <para>
  /// You can also use Blend to data bind with the tool's support.
  /// </para>
  /// <para>
  /// See http://www.galasoft.ch/mvvm/getstarted
  /// </para>
  /// </summary>
  public class MainViewModel : ViewModelBase {
    #region Properties
    string dataPath {
      get {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Manheim");
        System.IO.Directory.CreateDirectory(path);
        return path;
      }
    }
    string dbPath { get { return Path.Combine(dataPath,"DB", "Manheim.mdf"); } }
    string excelPath { get { return System.IO.Path.Combine(dataPath, "Manheim.csv"); } }

    #region UI

    #region SelectedAuctionItem
    private object _SelectedAuctionItem;
    public object SelectedAuctionItem {
      get { return _SelectedAuctionItem; }
      set {
        if (_SelectedAuctionItem != value) {
          if (value != null) {
            var auction = value as Auction;
            //if (auction != null) auction.IsSelected = true;
          } else {
            var auction = _SelectedAuctionItem as Auction;
            //if (auction != null) auction.IsSelected = false;
          }
          _SelectedAuctionItem = value;
          RaisePropertyChanged("SelectedAuctionItem");
        }
      }
    }

    #endregion

    public object SelectedAuction {
      set {
        if (value is Auction) {
          if (!_auctionsToRun.Contains(value))
            _auctionsToRun.Add(value as Auction);
        }
        if (value is StateAuctions) {
          foreach(var auction in (value as StateAuctions).Auctions)
            if (!_auctionsToRun.Contains(auction))
              _auctionsToRun.Add(auction);
        }
      }
    }

    #endregion

    #region Browser
    WC.Browser Browser {
      get {
        return ManheimExtensions.Browser;
      }
      set {
        ManheimExtensions.Browser = value;
      }
    }
    #endregion

    #region Manheim

    ObservableCollection<Auction> _auctionsToRun = new ObservableCollection<Auction>();
    public ObservableCollection<Auction> AuctionsToRun { get { return _auctionsToRun; } }
    #endregion

    #region States
    ObservableCollection<StateAuctions> _states = new ObservableCollection<StateAuctions>();
    public ObservableCollection<StateAuctions> States {
      get { return _states; }
      set { _states = value; }
    }
    #endregion

    private const string _vehicleInfoTargetName = "VehicleInfo";
    #endregion

    #region Methods
    #region Navigation

    #region Login
    void Login() {
      try {
        Browser.GoTo(this.GetLoginUrl());
      } catch (System.IO.FileNotFoundException exc) {
        MessageBox.Show(exc.FileName + " not found." + Environment.NewLine + exc.Message);
        return;
      }
      var userBox = Browser.TryFind<WC.TextField>("user_username");
      if (!userBox.Exists) return;
      userBox.Value = this.GetUserName();

      var passwordBox = Browser.TryFind<WC.TextField>("user_password");
      if (!passwordBox.Exists) return;
      passwordBox.Value = this.GetPassword();

      var loginButton = Browser.TryFind<WC.Button>("user_submit");
      if (!loginButton.Exists) return;
      loginButton.Click();
    }
    #endregion
    
    void NavigateToPreSale() {
      Login();
      var preSaleLink = Browser.TryFind<WC.Link>(this.GetTabBuyPreSaleId());
      if (!preSaleLink.Exists) return;
      preSaleLink.Click();
      States.Clear();
      var uls = Browser.Elements.Filter(WC.Find.ByClass("tier0"));
      if (uls.Count == 0) {
        Log = new Exception("No states found in Manheim page.");
        return;
      }
      foreach (WC.List ul in uls) {
        var el = ul.ElementWithTag("li",WC.Find.Any).AsElementContainer();
        var state = el.ElementWithTag("span",WC.Find.Any).Text;
        var ulKids = ul.ElementWithTag("ul",WC.Find.Any).AsElementContainer();
        var ulAuctions = new List<WC.ListItem>();
        foreach (WC.ListItem ulKid in ulKids.ElementsWithTag("li"))
          ulAuctions.Add(ulKid);
        States.Add(new StateAuctions(state,ulAuctions));
      }
      return;
      //var statAuction = States[0].Auctions[0] as WC.ListItem;
      //GetAuctionPreSale(States[0].State,statAuction);
    }

    void RunPresale() {
      foreach (var auction in _auctionsToRun) {
        var state = auction.State;
          GetAuctionPreSale(state, auction);
      }
    }

    private void GetAuctionPreSale(string state, Auction statAuction) {
      var auctionName = statAuction.Name;

      Browser.GoTo(statAuction.Url);

      var listAll = Browser.ElementWithTag("p", WC.Find.ByClass("enhancedSales"));
      var listAllLinks = Browser.ElementsOfType<WC.Link>().Where(l => l.Text == "List All Vehicles this Date").ToArray();
      foreach(var listAllLink in listAllLinks.Select(l=>l.Url).ToArray())
      GetAllVehiclesForTheDay(state, auctionName, listAllLink);
    }

    private void GetAllVehiclesForTheDay(string state, string auctionName, string listAllLink) {
      var there = false;
      while (!there) try {
          Browser.GoTo(listAllLink.Replace("standard", "enhanced"));
          there = true;
        } catch (WC.Exceptions.TimeoutException) { }
      var dataTable = Browser.ElementOfType<WC.Table>(WC.Find.ByClass("dataTable"));
      if (!dataTable.Exists) return;
      var dataHeader = dataTable.ChildWithTag("thead", WC.Find.Any).AsElementContainer();
      var trHeader = dataHeader.ElementsOfType<WC.TableRow>().First();
      foreach (var cell in trHeader.ChildrenWithTag("th"))
        Debug.WriteLine(Regex.Replace(cell.Text, @"[\W]", ""));
      var dataBody = dataTable.TableBody(WC.Find.Any);
      var manheimModel = new Manheim.Model.ManheimEntities(dbPath);

      var stateEntity = manheimModel.States.Where(s => s.Name == state).SingleOrDefault();
      if (stateEntity == null) {
        Log = new Exception("State " + state + " is not found.");
        return;
      }

      var mainColumn = Browser.Divs.Filter(WC.Find.ById("mainColumn")).Single();

      #region Get Manager
      Model.Manager managerEntity = null;
      var para = mainColumn.Para(p => p.Children().Select(c => c.TagName).DefaultIfEmpty("").First() == "B");
      if (para != null) {
        var b = para.Children().First();
        var managerName = b.Exists && b.TextAfter.Contains("-") ? b.Text.Trim() : "";
        managerEntity = manheimModel.Managers.SingleOrDefault(m => m.Name == managerName);
        if (managerEntity == null && !string.IsNullOrEmpty(managerName)) {
          var managerTitle = b.Exists ? b.TextAfter.Split('-')[1].Trim() : "";
          b = b.Exists ? b.NextSibling : b;
          var phoneFaxMail = b.Exists ? b.TextAfter.Split(';') : new string[0];
          var phone = phoneFaxMail.Length > 0 ? phoneFaxMail[0].Split(':')[1].Trim() : "";
          var fax = phoneFaxMail.Length > 1 ? phoneFaxMail[1].Split(':')[1].Trim() : "";
          var email = b.Exists ? b.NextSibling.Text.Trim() : "";
          managerEntity = new Model.Manager() {
            Name = managerName,
            Title = managerTitle,
            Phone = phone,
            Fax = fax,
            Email = email
          };
          manheimModel.Managers.Add(managerEntity);
          manheimModel.SaveChanges();
        }
      } else {
        managerEntity = manheimModel.Managers.SingleOrDefault(m => m.Name == "");
        if (managerEntity == null) {
          managerEntity = new Model.Manager() { Email = "", Fax = "", Phone = "", Title = "", Name = "" };
          manheimModel.Managers.Add(managerEntity);
          manheimModel.SaveChanges();
        }
      }
      #endregion

      #region Get Auction
      var auctionEntity = manheimModel.Auctions.SingleOrDefault(a => a.Name == auctionName);
      if (auctionEntity == null) {
        auctionEntity = new Model.Auction() {
          Name = auctionName,
          State = stateEntity,
          PreSaleManager = managerEntity
        };
        manheimModel.Auctions.Add(auctionEntity);
        manheimModel.SaveChanges();
      }
      #endregion

      var saleDateText = mainColumn.ElementWithTag("h4", WC.Find.ByText(new Regex(@"\d{2}/\d{2}/\d{4}"))).Text.Split('-')[0].Trim();
      DateTime saleDate;

      if (!DateTime.TryParse(saleDateText, out saleDate)) {
        Log = new Exception("Sale date not found for " + auctionName + " in " + state);
        return;
      }

      #region if(false)Delete by SaleDate
      var md = ((IObjectContextAdapter)manheimModel).ObjectContext.MetadataWorkspace;
      var item = md.GetItems(DataSpace.CSpace);
      item = md.GetItems(DataSpace.CSSpace);
      item = md.GetItems(DataSpace.OCSpace);
      item = md.GetItems(DataSpace.OSpace);
      item = md.GetItems(DataSpace.SSpace);
      if (false) {
        var preSaleTable = typeof(Model.PreSale).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>().First();
        var auctionIdField = ExpressionExtentions.GetLambda(() => new Model.PreSale().AuctionId);
        manheimModel.Database.ExecuteSqlCommand("DELETE " + preSaleTable.Name + " WHERE " + auctionIdField + " = {0}", auctionEntity.AuctionId);
      }
      #endregion

      //dataBody.OwnTableRows.ToList().ForEach(tr => 
      foreach (var tr in dataBody.OwnTableRows) {
        var tds = tr.OwnTableCells;
        if (tds.Count == 0) continue;
        var vin = tds[6].Text + "";
        var addPresSale = false;
        lock (auctionEntity) {
          addPresSale = !manheimModel.PreSales.Any(ps => ps.SaleDate == saleDate && ps.VIN == vin);
        }

        if (addPresSale)
          try {
            WC.IE vehicleInfoIE = OpenVehicleInfoWindow(tds);
            if (vehicleInfoIE != null)
              AddPreSale(manheimModel, auctionEntity, saleDate, tds, vin, vehicleInfoIE);
          } catch (System.Data.Entity.Validation.DbEntityValidationException exc){
            var ev = exc.EntityValidationErrors.First().ValidationErrors.First().ErrorMessage;
            Log = new Exception(ev);
          }
      }
    }

    private static void AddPreSale(Model.ManheimEntities manheimModel, Model.Auction auctionEntity, DateTime saleDate, WC.TableCellCollection tds, string vin, WC.IE vehicleInfoIE) {
      var rightContent = vehicleInfoIE.Div("rightContent");

      #region Vehicle

      #region Grade
      var grade = 0.0;
      var lastColumn = rightContent.Div(WC.Find.ByClass("lastColumn"));
      if (lastColumn.Exists) {
        var gradeLink = lastColumn.Link(WC.Find.ByText(new Regex("Grade")));
        if (gradeLink.Exists)
          double.TryParse(Regex.Replace(gradeLink.Text, @"[^\d.]", ""), out grade);
      }
      #endregion


      var mmrElement = vehicleInfoIE.Link("mmrHover");
      int mmrPrice = 0;
      if (mmrElement.Exists)
        int.TryParse(mmrElement.Text, NumberStyles.AllowThousands | NumberStyles.AllowCurrencySymbol, NumberFormatInfo.CurrentInfo, out mmrPrice);
      var div = vehicleInfoIE.Div(new Regex("vdpTab_detail-1"));
      var table = div.Tables.First();
      if (table != null) {
        var vehicleEntity = manheimModel.Vehicles.SingleOrDefault(v => v.VIN == vin);
        if (vehicleEntity == null) {
          vehicleEntity = SetVehicle(table);
          vehicleEntity.Grade = grade;
          manheimModel.Vehicles.Add(vehicleEntity);
        } else {
          UpdateVehicle(vehicleEntity, table);
        }
        vehicleEntity.MMR = mmrPrice;
        manheimModel.SaveChanges();
      #endregion

        #region Seller
        var sellerElement = rightContent.Div(WC.Find.ByClass("firstColumn")).ElementWithTag("li", WC.Find.First());
        var sellerName = sellerElement.Exists ? sellerElement.Text : "";
        var sellerEntity = manheimModel.Sellers.SingleOrDefault(s => s.Name == sellerName);
        if (sellerEntity == null) {
          sellerEntity = new Model.Seller() { Name = sellerName };
          manheimModel.Sellers.Add(sellerEntity);
          manheimModel.SaveChanges();
        }
        #endregion

        #region PreSale
        var preSale = new Model.PreSale() {
          Ln = int.Parse(tds[0].Text.Split('-')[0]),
          Run = int.Parse(tds[0].Text.Split('-')[1]),
          SaleDate = saleDate
        };
        lock (auctionEntity) {
          preSale.Vehicle = vehicleEntity;
          preSale.Auction = auctionEntity;
          preSale.Seller = sellerEntity;
          manheimModel.PreSales.Add(preSale);
        }
        #endregion
        manheimModel.SaveChanges();
      }
    }

    Predicate<WC.IE> p = ie => {
      var doc = ((WC.Native.InternetExplorer.IEBrowser)ie.NativeBrowser).WebBrowser.Document as mshtml.IHTMLDocument2;
      return false;
    };
    WC.IE vehicleInfoIE = null;
    private WC.IE OpenVehicleInfoWindow(WC.TableCellCollection tdsInput) {
      Func<WC.TableCellCollection, WC.IE> f = (tds) => {
        var link = tds[2].Links.First();
        var target = link.GetAttributeValue("target");
        link.SetAttributeValue("target", _vehicleInfoTargetName);
        link.Click();
        try {
          vehicleInfoIE = WC.IE.AttachTo<WC.IE>(WC.Find.ByTitle(t => {
            return t.StartsWith("Manheim - PowerSearch");
          }));
        } catch { }
        if (vehicleInfoIE == null || vehicleInfoIE.Title == "Manheim - PowerSearch - Search Results")
          try {
            vehicleInfoIE = WC.IE.AttachTo<WC.IE>(WC.Find.ByTitle(t => {
              return t == "Manheim - PowerSearch - Search Results";
            }));
            if (vehicleInfoIE != null) {
              var vehicle_detail_row = vehicleInfoIE.TableRow(WC.Find.ByClass("vehicle_detail_row "));
              if (vehicle_detail_row.Exists) {
                var link1 = vehicle_detail_row.Link("vehicleDetailsLink_0");
                if (link1 != null) {
                  link1.Click();
                  try {
                    return WC.IE.AttachTo<WC.IE>(WC.Find.ByTitle(t => {
                      return t == "Manheim - PowerSearch - Vehicle Details";
                    }));
                  } catch { }
                }
              }
              Log = new Exception("VehicleInfo window not found.");
            }
          } catch { }
        return vehicleInfoIE;
      };
      if (Application.Current.Dispatcher.CheckAccess())
        return f(tdsInput);
      var d = Application.Current.Dispatcher.BeginInvoke(f,tdsInput);
      d.Wait(TimeSpan.FromSeconds(30));
      return d.Result as WC.IE;
    }

    private static Model.Vehicle SetVehicle(WC.Table table) {
      Model.Vehicle vehicleEntity;
      vehicleEntity = new Model.Vehicle();
      UpdateVehicle(vehicleEntity, table);
      return vehicleEntity;
    }

    private static void UpdateVehicle(Model.Vehicle vehicleEntity, WC.Table table) {
      vehicleEntity.Year = GetCellByLabel<int>(table, "Year:", true);
      vehicleEntity.Make = GetCellByLabel(table, "Make:");
      vehicleEntity.Model = GetCellByLabel(table, "Model:");
      vehicleEntity.TrimLevel = GetCellByLabel(table, "Trim Level:");
      vehicleEntity.Odometer = GetCellByLabel<int>(table, "Odometer:", true);
      vehicleEntity.InServiceDate = GetCellByLabel(table, "In-Service Date:");
      vehicleEntity.FuelType = GetCellByLabel(table, "Fuel Type:");
      vehicleEntity.Engine = GetCellByLabel(table, "Engine:");
      vehicleEntity.Displacement = GetCellByLabel(table, "Displacement:");
      vehicleEntity.Transmission = GetCellByLabel(table, "Transmission:");
      vehicleEntity.ExteriorColor = GetCellByLabel(table, "Exterior Color:");
      vehicleEntity.InteriorColor = GetCellByLabel(table, "Interior Color:");
      vehicleEntity.WindowSticker = GetCellByLabel(table, "Window Sticker:");
      vehicleEntity.VIN = GetCellByLabel(table, "VIN:");
      vehicleEntity.BodyStyle = GetCellByLabel(table, "Body Style:");
      vehicleEntity.Doors = GetCellByLabel<int>(table, "Doors:", true);
      vehicleEntity.VehicleType = GetCellByLabel(table, "Vehicle Type:");
      vehicleEntity.Salvage = GetCellByLabel(table, "Salvage:") != "No";
      vehicleEntity.TitleState = GetCellByLabel(table, "Title State:");
      vehicleEntity.TitleStatus = GetCellByLabel(table, "Title Status:");
      vehicleEntity.DriveTrain = GetCellByLabel(table, "Drive Train:");
      vehicleEntity.InteriorType = GetCellByLabel(table, "Interior Type:");
      vehicleEntity.TopType = GetCellByLabel(table, "Top Type:");
      vehicleEntity.Stereo = GetCellByLabel(table, "Stereo:");
      vehicleEntity.Airbags = GetCellByLabel(table, "Airbags:");
    }

    private static string GetCellByLabel(WC.Table table, string labelText) {
      return GetCellByLabel<string>(table, labelText);
    }
    private static T GetCellByLabel<T>(WC.Table table,string labelText,bool isNumeric = false) {
      var label = table.ElementWithTag("th", WC.Find.ByText(labelText));
      if (!label.Exists) {
        throw new WC.Exceptions.ElementNotFoundException("th", "Text=" + labelText, "", table);
      }
      string text = label.NextSibling.Text;
      if (isNumeric) {
        text = Regex.Replace(label.NextSibling.Text, @"[^\d.]", "");
        if (string.IsNullOrEmpty(text))
          text = "0";
      }
      return (T)Convert.ChangeType(text, typeof(T));
    }

    #endregion
    #endregion

    #region Commands


    #region Close
    ICommand _CloseCommand;
    public ICommand CloseCommand {
      get {
        if (_CloseCommand == null) {
          _CloseCommand = new RelayCommand(CloseWindow, () => true);
        }

        return _CloseCommand;
      }
    }
    void CloseWindow() {
      _mustShutDown = true;
      try {
        this.vehicleInfoIE.Close();
      } catch { }
    }
    #endregion


    #region DataFolder
    ICommand _DataFolderCommand;
    public ICommand DataFolderCommand {
      get {
        if (_DataFolderCommand == null) {
          _DataFolderCommand = new RelayCommand(DataFolder, () => true);
        }

        return _DataFolderCommand;
      }
    }
    void DataFolder() {
      System.Diagnostics.Process.Start(dataPath);
    }
    #endregion


    #region Export
    ICommand _ExportCommand;
    public ICommand ExportCommand {
      get {
        if (_ExportCommand == null) {
          _ExportCommand = new RelayCommand(Export, () => true);
        }

        return _ExportCommand;
      }
    }
    void Export() {
      using (var entities = new Model.ManheimEntities(dbPath)) {
        CsvFileDescription outputFileDescription = new CsvFileDescription {
          SeparatorChar = ',', 
          FirstLineHasColumnNames = true,
           
        };
        CsvContext cc = new CsvContext();
        var path = System.IO.Path.GetDirectoryName(excelPath) 
          + "\\" + System.IO.Path.GetFileNameWithoutExtension(excelPath) 
          + DateTime.Today.ToString("_yyyyMMdd") 
          + System.IO.Path.GetExtension(excelPath);
        cc.Write(entities.vPreSales, path, outputFileDescription);
        System.Diagnostics.Process.Start(path);
      }
    }
    #endregion


    #region FillSearch
    ICommand _FillSearchCommand;
    public ICommand FillSearchCommand {
      get {
        if (_FillSearchCommand == null) {
          _FillSearchCommand = new RelayCommand(FillSearch, () => true);
        }
        return _FillSearchCommand;
      }
    }
    void FillSearch() {
      Browser.GoTo(this.GetHomeUrl());
      using (var entities = new Model.ManheimEntities(dbPath)) {
        entities.Configuration.LazyLoadingEnabled = true;
        while (Browser.Frames.Count < 2)
          Thread.Sleep(100);
        var makeList = Browser.Frames[1].TryFind<WC.SelectList>("makeList");
        if (!makeList.Exists) return;
        var modelList = Browser.Frames[1].TryFind<WC.SelectList>("modelList");
        if (!modelList.Exists) return;
        var models = modelList.Options.Select(o => o.Text);
        bool saveToDb = false;
        foreach (var make in makeList.Options.Skip(1)) {
          Debug.WriteLine(make.Text);
          var dbMake = entities.Makes.SingleOrDefault(m => m.Name == make.Text);
          if (dbMake == null) {
            dbMake = new Model.Make() { Name = make.Text };
            entities.Makes.Add(dbMake);
            saveToDb = true;
          }
          modelList.SetAttributeValue("selectedIndex", "-1");
          makeList.Select(make.Text);
          while (modelList.SelectedItem == null)
            Thread.Sleep(100);
          foreach (var model in modelList.Options.Skip(1)) {
            Debug.WriteLine("\t" + model.Text);
            if (!dbMake.Models.Where(m => m.Name == model.Text).Any()) {
              dbMake.Models.Add(new Model.Model() { Name = model.Text });
              saveToDb = true;
            }
          }
          if (saveToDb) {
            entities.SaveChanges();
            saveToDb = false;
          }
        }
      }
      Log = new Exception("Done with FillSearch!");
    }
    #endregion


    ICommand _PreSaleCommand;
    public ICommand PreSaleCommand {
      get {
        if (_PreSaleCommand == null) {
          _PreSaleCommand = new RelayCommand(NavigateToPreSale, () => true);
        }

        return _PreSaleCommand;
      }
    }
    #region LoginCommand
    ICommand _LoginCommand;
    private bool _mustShutDown;
    public ICommand LoginCommand {
      get {
        if (_LoginCommand == null) {
          _LoginCommand = new RelayCommand(Login, () => true);
        }
        return _LoginCommand;
      }
    }
    #endregion
    #endregion

    #region Ctor
    /// <summary>
    /// Initializes a new instance of the MainViewModel class.
    /// </summary>
    public MainViewModel() {
      if (IsInDesignMode) {
        // Code runs in Blend --> create design time data.
      } else {
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
          h => _auctionsToRun.CollectionChanged += h, h => _auctionsToRun.CollectionChanged -= h)
          .Where(ie => ie.EventArgs.Action == NotifyCollectionChangedAction.Add)
          .ObserveOn(Scheduler.ThreadPool)
          .SelectMany(ie => ie.EventArgs.NewItems.Cast<Auction>())
          .SubscribeOn(Scheduler.NewThread)
          .Subscribe(auction => {
            if (_mustShutDown) return;
            var t = new Thread(new ThreadStart(() => {
              try {
                GetAuctionPreSale(auction.State, auction);
              } catch (Exception exc) {
                if (!_mustShutDown)
                  Log = exc;
              } finally {
                try {
                  Application.Current.Dispatcher.Invoke(new Action(() => {
                    _auctionsToRun.Remove(auction);
                    SelectedAuctionItem = null;
                  }));
                } catch { }
              }
            }));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
          });
      }
    }
    #endregion

    public override void Cleanup() {
      _mustShutDown = true;
      Browser = null;
      base.Cleanup();
    }

    #region Helpers
    private bool EnsureElement<TElement>(string id, out TElement element) where TElement : WC.Element<TElement> {
      element = Browser.ElementOfType<TElement>(id);
      if (!element.Exists) {
        MessageBox.Show(id + " " + typeof(TElement).Name + " is not found");
        return false;
      }
      return true;
    }
    #endregion

    public Exception Log {
      set {
        MessageBox.Show(value + "");
      }
    }
  }
  static class WatiNExtension {
    public static WC.ElementContainer<WC.Element> AsElementContainer(this WC.Element element) {
      if (element == null)
        throw new NullReferenceException();
      if (!(element is WC.ElementContainer<WC.Element>))
        throw new InvalidCastException(element.GetType().Name + " does not implement ElementContainer<Element>.");
      return element as WC.ElementContainer<WC.Element>;
    }
    public static TElement TryFind<TElement>(this WC.Document browser,string id)where TElement:WC.Element {
      var element = browser.ElementOfType<TElement>(id);
      element.WaitUntilExists(5);
      if (!element.Exists)
        MessageBox.Show(id + " " + typeof(TElement).Name + " is not found");
      return element;
    }
  }
}