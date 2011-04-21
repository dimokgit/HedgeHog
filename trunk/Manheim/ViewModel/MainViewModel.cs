using GalaSoft.MvvmLight;
using System;
using System.Text.RegularExpressions;
using GalaSoft.MvvmLight.Command;
using System.Windows.Input;
using WC = WatiN.Core;
using System.Windows;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Data.Objects.DataClasses;
using HedgeHog;
using System.Globalization;
namespace Manheim.ViewModel {
  public class Auction {
    public string Name { get; set; }
    public string Url { get; set; }
    public Auction(string name,string url) {
      this.Name = name;
      this.Url = url;
    }
  }
  public class StateAuctions {
    public string State { get; set; }
    public List<Auction> Auctions { get; set; }
    public StateAuctions(string state,List<WC.ListItem> auctions) {
      this.State = state;
      this.Auctions = auctions.Select(a => new Auction(a.Text.Trim(), a.Links.First().Url)).ToList();
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
    #region Browser
    WC.Browser _browser;
    WC.Browser Browser {
      get {
        if (_browser == null)
          Login();
        return _browser;
      }
    }
    #endregion

    #region Manheim
    private const string _manheimLoginUrl = "manheim.com/login";
    private const string _manheimUserName = "afservices";
    private const string _manheimPassword = "password";
    private const string _manheimTabBuyPreSaleId = "tab_buy_pre_sale";
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
      Login(false);
    }
    void Login(bool reBrowser) {
      try {
        if (reBrowser || _browser == null)
          _browser = new WC.IE(_manheimLoginUrl);
        else
          Browser.GoTo(_manheimLoginUrl);
      } catch (System.IO.FileNotFoundException exc) {
        MessageBox.Show(exc.FileName + " not found." + Environment.NewLine + exc.Message);
        return;
      }
      var userBox = Browser.TryFind<WC.TextField>("user_username");
      if (!userBox.Exists) return;
      userBox.Value = _manheimUserName;

      var passwordBox = Browser.TryFind<WC.TextField>("user_password");
      if (!passwordBox.Exists) return;
      passwordBox.Value = _manheimPassword;

      var loginButton = Browser.TryFind<WC.Button>("user_submit");
      if (!loginButton.Exists) return;
      loginButton.Click();
    }
    #endregion
    
    void NavigateToPreSale() {
      var preSaleLink = Browser.TryFind<WC.Link>(_manheimTabBuyPreSaleId);
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
      foreach (var stateActions in States.Where(s=>s.State == "Florida")) {
        var state = stateActions.State;
        foreach (var auction in stateActions.Auctions)
          GetAuctionPreSale(state, auction);
      }
      //var statAuction = States[0].Auctions[0] as WC.ListItem;
      //GetAuctionPreSale(States[0].State,statAuction);
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
      Browser.GoTo(listAllLink.Replace("standard", "enhanced"));

      var dataTable = Browser.ElementOfType<WC.Table>(WC.Find.ByClass("dataTable"));
      if (!dataTable.Exists) return;
      var dataHeader = dataTable.ChildWithTag("thead", WC.Find.Any).AsElementContainer();
      var trHeader = dataHeader.ElementsOfType<WC.TableRow>().First();
      foreach (var cell in trHeader.ChildrenWithTag("th"))
        Debug.WriteLine(Regex.Replace(cell.Text, @"[\W]", ""));
      var dataBody = dataTable.TableBody(WC.Find.Any);
      var manheimModel = new Manheim.Model.ManheimEntities();

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
          manheimModel.AddToManagers(managerEntity);
          manheimModel.SaveChanges();
        }
      } else {
        managerEntity = manheimModel.Managers.SingleOrDefault(m => m.Name == "");
        if (managerEntity == null) {
          managerEntity = new Model.Manager() { Email = "", Fax = "", Phone = "", Title = "", Name = "" };
          manheimModel.AddToManagers(managerEntity);
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
        manheimModel.AddToAuctions(auctionEntity);
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
      if (false) {
        var preSaleTable = typeof(Model.PreSale).GetCustomAttributes(typeof(EdmEntityTypeAttribute), true).Cast<EdmEntityTypeAttribute>().First();
        var auctionIdField = ExpressionExtentions.GetLambda(() => new Model.PreSale().AuctionId);
        manheimModel.ExecuteStoreCommand("DELETE " + preSaleTable.Name + " WHERE " + auctionIdField + " = {0}", auctionEntity.AuctionId);
      }
      #endregion

      #region getOdometer
      Func<string, int> getOdometer = s => {
        int o = -1;
        int.TryParse(s + "", System.Globalization.NumberStyles.AllowThousands, NumberFormatInfo.CurrentInfo, out o);
        return o;
      };
      #endregion

      dataBody.OwnTableRows.ToList().ForEach(tr => {
        var tds = tr.OwnTableCells;
        if (tds.Count == 0) return;
        var vin = tds[6].Text + "";
        var addPresSale = false;
        lock (auctionEntity) {
          addPresSale = !manheimModel.PreSales.Any(ps => ps.SaleDate == saleDate && ps.VIN == vin);
        }
        if (addPresSale) {
          WC.IE vehicleInfoIE = OpenVehicleInfoWindow(tds);
          if (vehicleInfoIE == null) return;

          #region Vehicle
          var mmrElement = vehicleInfoIE.Link("mmrHover");
          int mmrPrice = 0;
          if (mmrElement.Exists)
            int.TryParse(mmrElement.Text, NumberStyles.AllowThousands | NumberStyles.AllowCurrencySymbol, NumberFormatInfo.CurrentInfo, out mmrPrice);
          var div = vehicleInfoIE.Div(new Regex("vdpTab_detail-1"));
          var table = div.Tables.First();
          if (table == null) return;
          var vehicleEntity = manheimModel.Vehicles.SingleOrDefault(v => v.VIN == vin);
          if (vehicleEntity == null) {
            vehicleEntity = SetVehicle(table);
            manheimModel.AddToVehicles(vehicleEntity);
          } else {
            UpdateVehicle(vehicleEntity, table);
          }
          vehicleEntity.MMR = mmrPrice;
          manheimModel.SaveChanges();
          #endregion

          #region Seller
          var sellerElement = vehicleInfoIE.Div("rightContent").Div(WC.Find.ByClass("firstColumn")).ElementWithTag("li", WC.Find.First());
          var sellerName = sellerElement.Exists ? sellerElement.Text : "";
          var sellerEntity = manheimModel.Sellers.SingleOrDefault(s => s.Name == sellerName);
          if (sellerEntity == null) {
            sellerEntity = new Model.Seller() { Name = sellerName };
            manheimModel.AddToSellers(sellerEntity);
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
            manheimModel.AddToPreSales(preSale);
          }
          #endregion
          manheimModel.SaveChanges();
        }
      });
    }

    private WC.IE OpenVehicleInfoWindow(WC.TableCellCollection tds) {
      var link = tds[2].Links.First();
      var target = link.GetAttributeValue("target");
      link.SetAttributeValue("target", _vehicleInfoTargetName);
      link.Click();
      WC.IE vehicleInfoIE = null;
      var dateStart = DateTime.Now;
      while (vehicleInfoIE == null && DateTime.Now < dateStart.AddSeconds(10)) {
        foreach (var ie in WC.IE.InternetExplorers()) {
          var doc = ((WC.Native.InternetExplorer.IEBrowser)ie.NativeBrowser).WebBrowser.Document as mshtml.IHTMLDocument2;
          if (doc.parentWindow.name == _vehicleInfoTargetName) {
            vehicleInfoIE = ie;
            break;
          }
        }
      }
      if (vehicleInfoIE == null) {
        Log = new Exception("VehicleInfo window not found.");
      }
      return vehicleInfoIE;
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
        // Code runs "for real"
      }
    }
    #endregion

    public override void Cleanup() {
      if (_browser != null)
        _browser.Dispose();
      base.Cleanup();
    }

    #region Helpers
    private bool EnsureElement<TElement>(string id, out TElement element) where TElement : WC.Element<TElement> {
      element = _browser.ElementOfType<TElement>(id);
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
    public static TElement TryFind<TElement>(this WC.Browser browser,string id)where TElement:WC.Element {
      var element = browser.ElementOfType<TElement>(id);
      element.WaitUntilExists(5);
      if (!element.Exists)
        MessageBox.Show(id + " " + typeof(TElement).Name + " is not found");
      return element;
    }
  }
}