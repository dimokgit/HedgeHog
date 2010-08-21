using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;

namespace HedgeHog {
  public class MEF {
    #region Container

    public static CompositionContainer Container {
      get {
        AggregateCatalog catalog = new AggregateCatalog();
        // Add the EmailClient.Presentation assembly into the catalog
        catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetExecutingAssembly()));
        catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetEntryAssembly()));
        // Add the EmailClient.Applications assembly into the catalog
        //catalog.Catalogs.Add(new AssemblyCatalog(typeof(IMainModel).Assembly));

        var container = new CompositionContainer(catalog);
        CompositionBatch batch = new CompositionBatch();
        batch.AddExportedValue(container);
        container.Compose(batch);
        return container;
      }
    }
    #endregion
  }
}
