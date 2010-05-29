using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client.Models {
  public partial class AliceEntities {
    //~AliceEntities() {
    //  if (GalaSoft.MvvmLight.ViewModelBase.IsInDesignModeStatic) return;
    //  var newName = Path.Combine(
    //    Path.GetDirectoryName(Connection.DataSource),
    //    Path.GetFileNameWithoutExtension(Connection.DataSource)
    //    ) + ".backup" + Path.GetExtension(Connection.DataSource);
    //  if (File.Exists(newName)) File.Delete(newName);
    //  File.Copy(Connection.DataSource, newName);
    //}
  }
  public partial class AliceEntities {
    public override int SaveChanges(System.Data.Objects.SaveOptions options) {
      try {
        InitGuidField<Models.TradingAccount>(ta => ta.Id, (ta, g) => ta.Id = g);
        InitGuidField<Models.TradingMacro>(ta => ta.UID, (ta, g) => ta.UID = g);
      } catch { }
      return base.SaveChanges(options);
    }

    private void InitGuidField<TEntity>(Func<TEntity,Guid> getField, Action<TEntity,Guid> setField) {
      var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added)
        .Select(o => o.Entity).OfType<TEntity>().Where(e =>getField(e)  == new Guid()).ToList();
      d.ForEach(e => setField(e, Guid.NewGuid()));
    }
  }
  public partial class TradingMacro {
    int _lotSize;
    public int LotSize { get { return _lotSize; } set { _lotSize = value; OnPropertyChanged("LotSize"); } }
    
    int _lots = 1;
    public int Lots { get { return _lots; } set { _lots = value; } }

    double? _net;
    public double? Net { get { return _net; } 
      set {
        if (_net == value) return;
        _net = value; OnPropertyChanged("Net"); } }

    double? _StopAmount;
    public double? StopAmount {
      get { return _StopAmount; }
      set {
        if (_StopAmount == value) return;
        _StopAmount = value; 
        OnPropertyChanged("StopAmount"); }
    }
    double? _LimitAmount;
    public double? LimitAmount {
      get { return _LimitAmount; }
      set {
        if (_LimitAmount == value) return;
        _LimitAmount = value; 
        OnPropertyChanged("LimitAmount"); }
    }

    double? _netInPips;
    public double? NetInPips { get { return _netInPips; } 
      set {
        if (_netInPips == value) return;
        _netInPips = value; 
        OnPropertyChanged("NetInPips"); } }
  }
}
