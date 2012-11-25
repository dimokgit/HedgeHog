using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HedgeHog.Alice.Store {
  public enum CorridorHighLowMethod { AskHighBidLow = 0, Average = 1, BidHighAskLow = 2, BidLowAskHigh = 3, AskLowBidHigh = 4, AskBidByMA = 5, PriceByMA = 6, BidAskByMA = 7,PriceMA = 8 }
  public enum MovingAverageType { Cma = 0, Trima = 1 }
  public partial class AliceEntities {
    [MethodImpl(MethodImplOptions.Synchronized)]
    public override int SaveChanges(System.Data.Objects.SaveOptions options) {
        try {
          InitGuidField<TradingAccount>(ta => ta.Id, (ta, g) => ta.Id = g);
          InitGuidField<TradingMacro>(ta => ta.UID, (ta, g) => ta.UID = g);
        } catch (Exception exc) {
          Debug.Fail(exc + "");
        }
        try {
          var a = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added).Count();
          a = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Deleted).Count().Max(a);
          a = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Modified).Count().Max(a);
          if (a > 0)
            return base.SaveChanges(options);
          else
            return 0;
        } catch (Exception exc) {
          GalaSoft.MvvmLight.Messaging.Messenger.Default.Send(exc);
          return 0;
        }
    }

    private void InitGuidField<TEntity>(Func<TEntity, Guid> getField, Action<TEntity, Guid> setField) {
      var d = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Added).ToArray();
      var f = d.Select(o => o.Entity).ToArray();
      var g = f.OfType<TEntity>().Where(e => getField(e) == new Guid()).ToList();
      g.ForEach(e => setField(e, Guid.NewGuid()));
      var h = ObjectStateManager.GetObjectStateEntries(System.Data.EntityState.Modified).ToArray();
      var i = d.Select(o => o.Entity).ToArray();
      var j = f.OfType<TEntity>().Where(e => getField(e) == new Guid()).ToList();
      j.ForEach(e => { });
    }
  }

  public partial class TradeHistory {
    public double NetPL { get { return GrossPL - Commission; } }
  }

  public class SessionInfoAttribute : Attribute {
    public string Nick { get; set; }
    public SessionInfoAttribute(string nick) {
      this.Nick = nick;
    }
    public SessionInfoAttribute() { }
  }
  public enum ScanCorridorFunction {
    WaveDistance = 1,
    WaveRelative = 2,
    WaveDistance1 = 3,
    WaveDistanceByHeight = 4,
    WaveStDev = 5,
    WaveDistance2 = 6,
    WaveDistance4 = 7,
    WaveDistance41 = 71,
    WaveDistance42 = 72,
    WaveStDevHeight = 8,
  }
  public enum TrailingWaveMethod {
    WaveAuto = 0,
    WaveShort = 1,
    WaveTrade = 2,
    WaveMax = 3,
    WaveMin = 4,
    WaveLeft = 5,
    WaveRight = 6,
  }
  public enum TradingMacroTakeProfitFunction {
    CorridorStDev = 3,
    Corridor_RH_2 = 4,
    CorridorHeight = 5,
    BuySellLevels = 6,
    RatesHeight = 10,
    RatesStDev = 11,
    RatesHeight_2 = 12,
    WaveShort = 13,
    WaveTradeStart = 14,
    RatesStDevMin = 15,
    Spread = 20,
    Zero = 25,
    PriceSpread = 26,
    WaveShortStDev = 27,
    WaveTradeStartStDev = 28,

  }
  public enum Freezing { None = 0, Freez = 1, Float = 2 }
  public enum CorridorCalculationMethod { Height = 1, Price = 2, HeightUD = 3,Minimum = 4,Maximum = 5,PriceAverage = 6 }
  [Flags]
  public enum LevelType { CenterOfMass = 1, Magnet = 2, CoM_Magnet = CenterOfMass | Magnet }
  [Flags]
  public enum Strategies {
    None = 0,
    Auto = 1,
    Hot = 2,
    Trailer01 = Hot * 2, Trailer01A = Trailer01 + Auto,//4
    Trailer = Trailer01 * 2, TrailerA = Trailer + Auto,//8
    FreeRoam = Trailer * 2, FreeRoamA = FreeRoam + Auto,//16
    Deviator = FreeRoam * 2, DeviatorA = Deviator + Auto,//32
    Ender = Deviator * 2, EnderA = Ender + Auto,//64
    Minimalist = Ender * 2, MinimalistA = Minimalist + Auto,//128
    Distancer = Minimalist * 2, DistancerA = Distancer + Auto,//256
    Distancer2 = Distancer * 2, Distancer2A = Distancer2 + Auto,//512
    Distancer4 = Distancer2 * 2, Distancer4A = Distancer4 + Auto,//1024
    Averager = Distancer4 * 2, AveragerA = Averager + Auto,//2048
    MiddleMan = Averager * 2, MiddleManA = MiddleMan + Auto,//4096
    StDevCombo = MiddleMan * 2, StDevComboA = StDevCombo + Auto//8192
  }
  public enum MovingAverageValues { PriceAverage = 0, Volume = 1, PriceSpread = 2, PriceMove = 3 }
  public struct Playback {
    public bool Play;
    public DateTime StartDate;
    public TimeSpan Delay;
    public Playback(bool play, DateTime startDate, TimeSpan delay) {
      this.Play = play;
      this.StartDate = startDate;
      this.Delay = delay;
    }
  }
  public class GannAngle : Models.ModelBase {
    public double Price { get; set; }
    public double Time { get; set; }
    public double Value { get { return Price / Time; } }
    public bool IsDefault { get; set; }
    private bool _IsOn;
    #region IsOn
    public bool IsOn {
      get { return _IsOn; }
      set {
        if (_IsOn != value) {
          _IsOn = value;
          RaisePropertyChanged("IsOn");
        }
      }
    }
    #endregion

    public GannAngle(double price, double time, bool isDefault) {
      this.Price = price;
      this.Time = time;
      this.IsDefault = isDefault;
    }
    public override string ToString() {
      return string.Format("{0}/{1}={2:n3}", Price, Time, Value);
    }
  }
  public class GannAngles : Models.ModelBase {
    int _Angle1x1Index = -1;
    public int Angle1x1Index {
      get { return _Angle1x1Index; }
      set { _Angle1x1Index = value; }
    }
    GannAngle[] _Angles = new[]{
     new GannAngle(8,1,true),
     new GannAngle(7,1,false),
     new GannAngle(6,1,false),
     new GannAngle(5,1,false),
     new GannAngle(4,1,true),
     new GannAngle(3,1,true),
     new GannAngle(2,1,true),
     new GannAngle(1.618,1,false),
     new GannAngle(1.382,1,false),
     new GannAngle(1.236,1,false),
     new GannAngle(1,1,true),
     new GannAngle(1,1.236,false),
     new GannAngle(1,1.382,false),
     new GannAngle(1,1.618,false),
     new GannAngle(1,2,true),
     new GannAngle(1,3,true),
     new GannAngle(1,4,true),
     new GannAngle(1,5,false),
     new GannAngle(1,6,false),
     new GannAngle(1,7,false),
     new GannAngle(1,8,true)
    };

    public GannAngle[] Angles {
      get { return _Angles; }
      set { _Angles = value; }
    }

    public void Reset() {
      Angles.ToList().ForEach(a => a.IsOn = a.IsDefault);
    }

    public GannAngle[] ActiveAngles { get { return Angles.Where(a => a.IsOn).ToArray(); } }

    public GannAngles(string priceTimeValues)
      : this() {
      FromString(priceTimeValues);
    }
    public GannAngles() {
      Angles.ToList().ForEach(angle => angle.PropertyChanged += (o, p) => {
        if (ActiveAngles.Length == 0)
          Get1x1().IsOn = true;
        else {
          Angle1x1Index = GetAngle1x1Index();
          RaisePropertyChanged("Angles");
        }
      });
    }
    private GannAngle Get1x1() { return Angles.Where(a => a.Price == a.Time).Single(); }
    public int GetAngle1x1Index() { return ActiveAngles.ToList().FindIndex(a => a.Price == a.Time); }

    public GannAngle[] FromString(string priceTimeValues) {
      var ptv = priceTimeValues.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Split('/').Select(v1 => double.Parse(v1)).ToArray()).ToArray();
      var aaa = (from v in ptv
                 join a in Angles on new { Price = v[0], Time = v.Length > 1 ? v[1] : 1 } equals new { a.Price, a.Time }
                 select a).ToList();
      aaa.ForEach(a => a.IsOn = true);
      return Angles;
    }

    public override string ToString() {
      return string.Join(",", ActiveAngles.Select(a => string.Format("{0}/{1}", a.Price, a.Time)));
    }
  }
  static class TradingMacroEx {
    public static TradingMacro ToTradingMacro(this object o) { return o as TradingMacro; }
  }
}
