using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace HedgeHog.Alice.Store {
  public class TraderModelPersist :HedgeHog.Models.ModelBase {
    public static string CurrentDirectory() => System.Net.Dns.GetHostName() + "::" + Lib.CurrentDirectory.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();

    public TraderModelPersist() {
      _key = CurrentDirectory();
    }
    //public ObjectId _id { get; set; }
    private int _IpPort;
    public int IpPort {
      get { return _IpPort; }
      set {
        if(_IpPort != value) {
          _IpPort = value;
          RaisePropertyChangedCore();
        }
      }
    }

  }
}