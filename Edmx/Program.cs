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
        var fileName = args.FirstOrDefault();
        var fileNames = new List<string>();
        if ((Path.GetExtension(fileName) + "").ToLower() == ".edmx") fileNames.Add(fileName);
        else {
          fileNames.AddRange(Directory.GetFiles(fileName, "*.edmx"));
          if (!fileNames.Any())
            fileNames.AddRange(Directory.GetFiles(Path.Combine(fileName, "App_Code"), "*.edmx"));
        }
        var entityNames = args.Skip(1).ToArray();
        fileNames.ForEach(fn => ProcessEdmx(fn, entityNames));
      } catch (Exception exc) {
        Console.WriteLine(exc + "");
        Console.WriteLine("Press any key ...");
        Console.ReadKey();
      }
    }

    private static void ProcessEdmx(string fileName, string[] entityNames) {
      File.Copy(fileName, fileName + ".bak", true);
      var xDoc = XDocument.Load(fileName);
      var dqs = xDoc.XPathSelectElements("//*").Where(e => e.Name.LocalName == "DefiningQuery" && entityNames.Contains(e.Parent.Attribute("Name").Value) ).ToList();
      //if( !dqs.Any()) throw new Exception("No Entities provided in command string.");
      dqs.ForEach(d => {
        var aSchema = d.Parent.Attributes().Single(a => a.Name.LocalName == "Schema");
        d.Parent.Add(new XAttribute(XName.Get("Schema"), aSchema.Value));
        aSchema.Remove();
        d.Parent.Attributes().Where(a => a.Name.LocalName == "Name" && !string.IsNullOrWhiteSpace(a.Name.NamespaceName))
          .ToList().ForEach(aName => aName.Remove());
        d.Remove();
      });
      xDoc.Save(fileName);
      Console.WriteLine(fileName + " done.");
    }
  }
}
