using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Edmx {
  class Program {
    static void Main(string[] args) {
      try {
        var fileName = args.FirstOrDefault() ?? @"C:\Inetpub\wwwroot\DS\KPI\App_Code\KpiModel.edmx";
        File.Copy(fileName, fileName + ".bak", true);
        var xDoc = XDocument.Load(fileName);
        xDoc.XPathSelectElements("//*").Where(e => e.Name.LocalName == "DefiningQuery").ToList().ForEach(d => {
          var aSchema = d.Parent.Attributes().Single(a => a.Name.LocalName == "Schema");
          d.Parent.Add(new XAttribute(XName.Get("Schema"), aSchema.Value));
          aSchema.Remove();
          var aName = d.Parent.Attributes().Single(a => a.Name.LocalName == "Name" && !string.IsNullOrWhiteSpace(a.Name.NamespaceName));
          aName.Remove();
          d.Remove();
        });
        xDoc.Save(fileName);
      } catch (Exception exc) {
        Console.WriteLine(exc + "");
        Console.WriteLine("Press any key ...");
        Console.ReadKey();
      }
    }
  }
}
