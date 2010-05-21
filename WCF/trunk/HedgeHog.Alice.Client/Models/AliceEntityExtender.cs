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
        var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added)
          .Select(o => o.Entity).OfType<Models.TradingAccount>().Where(e => e.Id == new Guid()).ToList();
        d.ForEach(e => e.Id = Guid.NewGuid());
      } catch { }
      return base.SaveChanges(options);
    }
  }
}
