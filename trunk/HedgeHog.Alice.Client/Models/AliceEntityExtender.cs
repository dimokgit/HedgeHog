using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Client.Models {
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
