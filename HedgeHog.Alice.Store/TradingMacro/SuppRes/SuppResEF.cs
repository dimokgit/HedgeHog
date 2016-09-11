using System;
using System.ComponentModel;

using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Core.Objects.DataClasses;

using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;


namespace HedgeHog.Alice.Store {

  public partial class SuppRes : EntityObject {
    #region Factory Method

    /// <summary>
    /// Create a new SuppRes object.
    /// </summary>
    /// <param name="rate">Initial value of the Rate property.</param>
    /// <param name="isSupport">Initial value of the IsSupport property.</param>
    /// <param name="tradingMacroID">Initial value of the TradingMacroID property.</param>
    /// <param name="uID">Initial value of the UID property.</param>
    /// <param name="tradesCount">Initial value of the TradesCount property.</param>
    public static SuppRes CreateSuppRes(global::System.Double rate, global::System.Boolean isSupport, global::System.Guid tradingMacroID, global::System.Guid uID, global::System.Double tradesCount) {
      SuppRes suppRes = new SuppRes();
      suppRes.Rate = rate;
      suppRes.IsSupport = isSupport;
      suppRes.TradingMacroID = tradingMacroID;
      suppRes.UID = uID;
      suppRes.TradesCount = tradesCount;
      return suppRes;
    }

    #endregion

    #region Simple Properties

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double Rate {
      get {
        return _Rate;
      }
      set {
        OnRateChanging(value);
        ReportPropertyChanging("Rate");
        _Rate = StructuralObject.SetValidValue(value, "Rate");
        ReportPropertyChanged("Rate");
        OnRateChanged();
      }
    }
    private global::System.Double _Rate;
    partial void OnRateChanging(global::System.Double value);
    partial void OnRateChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsSupport {
      get {
        return _IsSupport;
      }
      set {
        OnIsSupportChanging(value);
        ReportPropertyChanging("IsSupport");
        _IsSupport = StructuralObject.SetValidValue(value, "IsSupport");
        ReportPropertyChanged("IsSupport");
        OnIsSupportChanged();
      }
    }
    private global::System.Boolean _IsSupport;
    partial void OnIsSupportChanging(global::System.Boolean value);
    partial void OnIsSupportChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Guid TradingMacroID {
      get {
        return _TradingMacroID;
      }
      set {
        OnTradingMacroIDChanging(value);
        ReportPropertyChanging("TradingMacroID");
        _TradingMacroID = StructuralObject.SetValidValue(value, "TradingMacroID");
        ReportPropertyChanged("TradingMacroID");
        OnTradingMacroIDChanged();
      }
    }
    private global::System.Guid _TradingMacroID;
    partial void OnTradingMacroIDChanging(global::System.Guid value);
    partial void OnTradingMacroIDChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = true, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Guid UID {
      get {
        return _UID;
      }
      set {
        if(_UID != value) {
          OnUIDChanging(value);
          ReportPropertyChanging("UID");
          _UID = StructuralObject.SetValidValue(value, "UID");
          ReportPropertyChanged("UID");
          OnUIDChanged();
        }
      }
    }
    private global::System.Guid _UID;
    partial void OnUIDChanging(global::System.Guid value);
    partial void OnUIDChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double TradesCount {
      get {
        return _TradesCount;
      }
      set {
        OnTradesCountChanging(value);
        ReportPropertyChanging("TradesCount");
        _TradesCount = StructuralObject.SetValidValue(value, "TradesCount");
        ReportPropertyChanged("TradesCount");
        OnTradesCountChanged();
      }
    }
    private global::System.Double _TradesCount;
    partial void OnTradesCountChanging(global::System.Double value);
    partial void OnTradesCountChanged();

    #endregion


  }
}