using System;
using System.ComponentModel;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Core.Objects.DataClasses;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace HedgeHog.Alice.Store {
  public partial class TradingAccount : EntityObject {
    #region Factory Method

    /// <summary>
    /// Create a new TradingAccount object.
    /// </summary>
    /// <param name="password">Initial value of the Password property.</param>
    /// <param name="isDemo">Initial value of the IsDemo property.</param>
    /// <param name="id">Initial value of the Id property.</param>
    /// <param name="isMaster">Initial value of the IsMaster property.</param>
    /// <param name="tradeRatio">Initial value of the TradeRatio property.</param>
    /// <param name="commission">Initial value of the Commission property.</param>
    /// <param name="isActive">Initial value of the IsActive property.</param>
    /// <param name="tradingMacroName">Initial value of the TradingMacroName property.</param>
    /// <param name="currency">Initial value of the Currency property.</param>
    public static TradingAccount CreateTradingAccount(global::System.String password, global::System.Boolean isDemo, global::System.Guid id, global::System.Boolean isMaster, global::System.String tradeRatio, global::System.Double commission, global::System.Boolean isActive, global::System.String tradingMacroName, global::System.String currency) {
      TradingAccount tradingAccount = new TradingAccount();
      tradingAccount.Password = password;
      tradingAccount.IsDemo = isDemo;
      tradingAccount.Id = id;
      tradingAccount.IsMaster = isMaster;
      tradingAccount.TradeRatio = tradeRatio;
      tradingAccount.Commission = commission;
      tradingAccount.IsActive = isActive;
      tradingAccount.Currency = currency;
      return tradingAccount;
    }

    #endregion

    #region Simple Properties

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String Password {
      get {
        return _Password;
      }
      set {
        OnPasswordChanging(value);
        ReportPropertyChanging("Password");
        _Password = StructuralObject.SetValidValue(value, false, "Password");
        ReportPropertyChanged("Password");
        OnPasswordChanged();
      }
    }
    private global::System.String _Password;
    partial void OnPasswordChanging(global::System.String value);
    partial void OnPasswordChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public global::System.String MasterId {
      get {
        return _MasterId;
      }
      set {
        OnMasterIdChanging(value);
        ReportPropertyChanging("MasterId");
        _MasterId = StructuralObject.SetValidValue(value, true, "MasterId");
        ReportPropertyChanged("MasterId");
        OnMasterIdChanged();
      }
    }
    private global::System.String _MasterId;
    partial void OnMasterIdChanging(global::System.String value);
    partial void OnMasterIdChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsDemo {
      get {
        return _IsDemo;
      }
      set {
        OnIsDemoChanging(value);
        ReportPropertyChanging("IsDemo");
        _IsDemo = StructuralObject.SetValidValue(value, "IsDemo");
        ReportPropertyChanged("IsDemo");
        OnIsDemoChanged();
      }
    }
    private global::System.Boolean _IsDemo;
    partial void OnIsDemoChanging(global::System.Boolean value);
    partial void OnIsDemoChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public global::System.String AccountId {
      get {
        return _AccountId;
      }
      set {
        OnAccountIdChanging(value);
        ReportPropertyChanging("AccountId");
        _AccountId = StructuralObject.SetValidValue(value, true, "AccountId");
        ReportPropertyChanged("AccountId");
        OnAccountIdChanged();
      }
    }
    private global::System.String _AccountId;
    partial void OnAccountIdChanging(global::System.String value);
    partial void OnAccountIdChanged();

    [DataMemberAttribute()]
    public string AccountSubId { get; set; }

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = true, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Guid Id {
      get {
        return _Id;
      }
      set {
        if(_Id != value) {
          OnIdChanging(value);
          ReportPropertyChanging("Id");
          _Id = StructuralObject.SetValidValue(value, "Id");
          ReportPropertyChanged("Id");
          OnIdChanged();
        }
      }
    }
    private global::System.Guid _Id;
    partial void OnIdChanging(global::System.Guid value);
    partial void OnIdChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsMaster {
      get {
        return _IsMaster;
      }
      set {
        OnIsMasterChanging(value);
        ReportPropertyChanging("IsMaster");
        _IsMaster = StructuralObject.SetValidValue(value, "IsMaster");
        ReportPropertyChanged("IsMaster");
        OnIsMasterChanged();
      }
    }
    private global::System.Boolean _IsMaster;
    partial void OnIsMasterChanging(global::System.Boolean value);
    partial void OnIsMasterChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String TradeRatio {
      get {
        return _TradeRatio;
      }
      set {
        OnTradeRatioChanging(value);
        ReportPropertyChanging("TradeRatio");
        _TradeRatio = StructuralObject.SetValidValue(value, false, "TradeRatio");
        ReportPropertyChanged("TradeRatio");
        OnTradeRatioChanged();
      }
    }
    private global::System.String _TradeRatio;
    partial void OnTradeRatioChanging(global::System.String value);
    partial void OnTradeRatioChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Double Commission {
      get {
        return _Commission;
      }
      set {
        OnCommissionChanging(value);
        ReportPropertyChanging("Commission");
        _Commission = StructuralObject.SetValidValue(value, "Commission");
        ReportPropertyChanged("Commission");
        OnCommissionChanged();
      }
    }
    private global::System.Double _Commission;
    partial void OnCommissionChanging(global::System.Double value);
    partial void OnCommissionChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.Boolean IsActive {
      get {
        return _IsActive;
      }
      set {
        OnIsActiveChanging(value);
        ReportPropertyChanging("IsActive");
        _IsActive = StructuralObject.SetValidValue(value, "IsActive");
        ReportPropertyChanged("IsActive");
        OnIsActiveChanged();
      }
    }
    private global::System.Boolean _IsActive;
    partial void OnIsActiveChanging(global::System.Boolean value);
    partial void OnIsActiveChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = true)]
    [DataMemberAttribute()]
    public Nullable<global::System.Double> PipsToExit {
      get {
        return _PipsToExit;
      }
      set {
        OnPipsToExitChanging(value);
        ReportPropertyChanging("PipsToExit");
        _PipsToExit = StructuralObject.SetValidValue(value, "PipsToExit");
        ReportPropertyChanged("PipsToExit");
        OnPipsToExitChanged();
      }
    }
    private Nullable<global::System.Double> _PipsToExit;
    partial void OnPipsToExitChanging(Nullable<global::System.Double> value);
    partial void OnPipsToExitChanged();

    /// <summary>
    /// No Metadata Documentation available.
    /// </summary>
    [EdmScalarPropertyAttribute(EntityKeyProperty = false, IsNullable = false)]
    [DataMemberAttribute()]
    public global::System.String Currency {
      get {
        return _Currency;
      }
      set {
        OnCurrencyChanging(value);
        ReportPropertyChanging("Currency");
        _Currency = StructuralObject.SetValidValue(value, false, "Currency");
        ReportPropertyChanged("Currency");
        OnCurrencyChanged();
      }
    }
    private global::System.String _Currency;
    partial void OnCurrencyChanging(global::System.String value);
    partial void OnCurrencyChanged();

    #endregion

  }
}
