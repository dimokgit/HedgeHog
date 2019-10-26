using System;
using System.Collections.Generic;
using System.Linq;
using HedgeHog.Bars;
using System.Reactive.Concurrency;
using System.Diagnostics;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using HedgeHog;
using AutoMapper;
using HedgeHog.DateTimeZone;
using HedgeHog.Core;

namespace HedgeHog.Shared {
  public static class TradesManagerStatic {
    // TODO - this CalcComboRatios should be used everywhere
    public static (int r1, int r2, int quantity) CalcComboRatios(int quantity, double ratio1, double ratio2) {
      int r1 = (ratio1 * quantity).ToInt();
      int r2 = (ratio2 * quantity).ToInt();
      var gcd = new[] { r1, r2 }.GCD();
      return (r1 / gcd, r2 / gcd, gcd);
    }
    public static int ExpirationDaysSkip(int start) => !DateTime.Now.InNewYork().isWeekend() && DateTime.Now.InNewYork().TimeOfDay > new TimeSpan(18, 0, 0) ? start + 1 : start;
    public static IMapper TradeMapper() => TradeMapper(opt => opt);//.ForMember(t => t.TradesManager, o => o.Ignore()));
    public static IMapper TradeMapper(Func<IMappingExpression<Trade, Trade>, IMappingExpression<Trade, Trade>> opt)
      => new MapperConfiguration(cfg => opt(cfg.CreateMap<Trade, Trade>())).CreateMapper();

    public readonly static Offer OfferDefault = new Offer { Pair = "DEFAULT", Digits = 2, PointSize = 1, MMRLong = 0.250, MMRShort = 0.3, ContractSize = 1 };
    public static Offer[] dbOffers = new[] {
            new Offer { Pair = "USDJPY", Digits = 3, PointSize = 0.01, MMRLong=1, ContractSize = 1000 },
            new Offer { Pair = "EURUSD", Digits = 5, PointSize = 0.0001, MMRLong=1, ContractSize = 1000 },
            new Offer { Pair = "XAUUSD", Digits = 2, PointSize = 0.01, MMRLong=0.513, ContractSize = 1 },
            new Offer { Pair = "SPY", Digits = 3, PointSize = 0.01, MMRLong = 0.250, MMRShort= 0.3, ContractSize = 1 },
            new Offer { Pair = "TVIX", Digits = 3, PointSize = 0.01, MMRLong = 1/1.14, MMRShort= 1/1.14, ContractSize = 1 },
            new Offer { Pair = "UVXY", Digits = 3, PointSize = 0.01, MMRLong = 1/1.14, MMRShort= 1/1.14, ContractSize = 1 },
            new Offer { Pair = "ES", Digits = 2, PointSize = 1, MMRLong = 1/4, MMRShort= 1/3, ContractSize = 50 }
          };
    static Func<string, Offer> GetOfferImpl = symbol
        => dbOffers
      .Where(o => o.Pair.ToUpper() == symbol.FutureCode().WrapPair())
      .Take(1)
      .DefaultIfEmpty(OfferDefault)
      .Single();
    public static Offer GetOffer(string pair) => GetOfferImpl(pair);
    public static double GetPointSize(string symbol) => GetOffer(symbol).PointSize;
    public static int GetBaseUnitSize(string symbol) => GetOffer(symbol).ContractSize;
    public static int GetDigits(string symbol) => GetOffer(symbol).Digits;
    public static double GetMMR(string symbol, bool isBuy) => isBuy ? GetOffer(symbol).MMRLong : GetOffer(symbol).MMRShort;
    public static double Leverage(string pair, double mmr) => GetBaseUnitSize(pair) / mmr;

    private static string[] _currencies = new[]{
      "USD",
      "SEK",
      "NZD",
      "JPY",
      "GBP",
      "EUR",
      "CHF",
      "CAD",
      "AUD"
    };
    private static string[] _commodities = new[]{
      "XAUUSD",
      "XAGUSD"
    };
    public static string WrapPair(this string pair) {
      return Regex.Replace(pair, "[./-]", "").ToUpper();
    }
    static string UnWrapPair(string pair) {
      if(!pair.IsNullOrEmpty())
        throw new NotImplementedException();
      return Regex.Replace(pair, @"(\w3)(\w3)", "$1/$2");
    }

    public static double CalcHedgePrice(this IList<(double price, int multiplier, double positions)> hedges) => CalcHedgePrice(hedges.Select(h => (h.price, (double)h.multiplier, h.positions)).ToArray());
    public static double CalcHedgePrice(this IList<(double price, double multiplier, double positions)> hedges) => hedges.Sum(h => h.price * h.multiplier * h.positions) / hedges.Min(h => h.multiplier);

    public static ((TContract contract, int quantity)[] contracts, int quantity) HedgeQuanitiesByValue<TContract>(int multiplier
      , string mashDivider
      , params (TContract contract, double price, double weight, double multiplier, string context)[] hedges) {
      if(hedges == null)
        throw new ArgumentNullException(nameof(hedges));
      if(hedges.Length > 2)
        return Aggregated();
      var res = TradesManagerStatic.HedgeRatioByValue(mashDivider, hedges);
      var res2 = res.Select(t => new { t.contract, ratio = (t.ratio * multiplier).ToInt() }).ToArray();
      var gcd = res2.Select(t => t.ratio).ToArray().GCD();
      return (res2.Select(t => (t.contract, quamtity: t.ratio / gcd)).ToArray(), gcd);
      //// Locals
      ((TContract contract, int quantity)[], int) Aggregated() {
        var aa = (from h in hedges
                  group h by h.contract into g
                  select (g.Key, g.Average(t => t.price), g.Sum(t => t.weight), g.Select(t => t.multiplier).First(), g.Select(t => t.context).ToArray().MashDiffs())
                 ).ToArray();
        if(aa.Length != 2) throw new Exception("Too many contracts\n" + new { hedges }.ToJson());
        return HedgeQuanitiesByValue(multiplier, mashDivider, aa);
      }
    }

    public static IList<(TContract contract, double ratio)> HedgeRatioNyWeight<TContract>(
      params (TContract contract, double price, double weight, int multiplier)[] hedges) {
      var qs = hedges.Zip(hedges.Reverse(), (h1, h2) => new { h1.contract, w = (h1.price * h1.multiplier * h2.weight).AutoRound2(3) }).OrderByDescending(x => x.w).ToArray();
      var r = qs[0].w / qs[1].w;
      return new[] { (qs[0].contract, r), (qs[1].contract, 1.0) };
    }
    public static (TContract contract, double ratio, double price, string context)[] HedgeRatioByValue<TContract>(string mashDivider,
      params (TContract contract, double price, double timeValue, double multiplier, string context)[] hedges) {
      hedges.Take(0).ForEach(h => {
        if(double.IsInfinity(h.timeValue) || h.timeValue == 0)
          throw new Exception($"{nameof(HedgeRatioByValue)}: {new { h.timeValue }}");
      });

      var r = Aggregated().Pairwise((h1, h2)
        => (h1.multiplier * h1.multiplier / h2.multiplier / h2.multiplier) * h1.price * h1.timeValue / h2.price / h2.timeValue).SingleOrDefault().Round(6);
      if(r == 0) return new (TContract contract, double ratio, double price, string context)[0];
      var pos = r.PositionsFromRatio();
      return new[] { (hedges[0].contract, pos.p1, hedges[0].price, hedges[0].context), (hedges[1].contract, pos.p2, hedges[1].price, hedges[1].context) };
      //// Locals
      (TContract contract, double price, double timeValue, double multiplier, string context)[] Aggregated() {
        if(hedges.Length == 2) return hedges;
        var aa = (from h in hedges
                  group h by h.contract into g
                  select (g.Key, g.Average(t => t.price), g.Sum(t => t.timeValue), g.Select(t => t.multiplier).First(), g.Select(t => t.context).ToArray().MashDiffs(mashDivider))
                 ).ToArray();
        if(aa.Length != 2) return aa.Take(0).ToArray();
        //throw new Exception("Too many contracts\n" + new { hedges = hedges.Select(h => h.contract).ToJson() }.ToJson());
        return hedges = aa;
      }

    }

    public static (double p1, double p2) PositionsFromRatio(this double r) {
      var r0 = r > 1 ? 1 / r : 1;
      var r1 = r < 1 ? r : 1;
      return (r0, r1);
    }

    public static IList<(TContract contract, double ratio)> HedgeRatioByValue_Old<TContract>(params (TContract contract, double price, double timeValue, int multiplier)[] hedges) {
      var tvrs = hedges.Select(h => h.timeValue / h.price).Reverse().ToArray();
      var qs = hedges.Zip(tvrs, (h, tvr) => new { h.contract, w = (h.price * h.multiplier * tvr).AutoRound2(3) }).OrderByDescending(x => x.w).ToArray();
      var r = qs[0].w / qs[1].w;
      return new[] { (qs[0].contract, r), (qs[1].contract, 1.0) };
    }

    public static bool IsCurrenncy(this string s) => _currencies.Any(c => s.ToUpper().StartsWith(c)) && _currencies.Any(c => s.ToUpper().EndsWith(c));
    private static Regex FutureMatch = new Regex(@"^(?<code>\w{2,3})[HMQUZ]\d{1,2}|VX[A-Z]\d$", RegexOptions.IgnoreCase);
    public static bool IsFuture(this string s) => FutureMatch.IsMatch(s);
    public static string FutureCode(this string s) => IsFuture(s) ? FutureMatch.Match(s).Groups["code"].Value : s;
    public static bool IsCommodity(this string s) => _commodities.Contains(s.ToUpper());
    public static bool IsUSStock(this string s) => !s.IsCurrenncy() && !s.IsFuture() && !s.IsCommodity();
    public static bool IsOptionFull(this string s) => Regex.IsMatch(s, @"^[a-z ]{6}[0-9]{6}[CP][0-9]{8}$", RegexOptions.IgnoreCase);
    public static bool IsFOPtion(this string s) => Regex.IsMatch(s, @"^[a-z ]{5}\s[CP][0-9]{2-6}$", RegexOptions.IgnoreCase);
    public static bool HasOptions(this string s) => !(s.Substring(0, 2) == "VX" && s.IsFuture());

    //E3AJ8 C2665
    public static bool IsOption(this string s) => s.IsOptionFull() || s.IsFOPtion();
    static string[] _indices = new[] { "SPX", "VIX", "ES" };
    public static bool IsIndex(this string s) => _indices.Contains(s.ToUpper());
    //public static bool IsIndex(this string s) => Regex.IsMatch(s, @"\sindex$", RegexOptions.IgnoreCase);
    static string[] _etfs = new[] { "SPY", "TVIX", "VXX", "UVXY", "SVXY", "QQQ" };
    public static bool IsETF(this string s) => _etfs.Contains(s);
    private static readonly EventLoopScheduler _tradingThread =
      new EventLoopScheduler(ts => { return new Thread(ts) { IsBackground = true }; });

    public static EventLoopScheduler TradingScheduler { get { return _tradingThread; } }

    private static object syncRoot = new Object();
    private static volatile ISubject<Action> _tradingSubject;
    public static ISubject<Action> TradingSubject {
      get {
        if(_tradingSubject == null)
          lock(syncRoot)
            if(_tradingSubject == null) {
              _tradingSubject = new Subject<Action>();
              _tradingSubject.ObserveOn(TradesManagerStatic.TradingScheduler).Subscribe(a => a()
                , exc => {
                  LogMessage.Send(exc);
                  Debug.Fail("TradingSubject stopped working.", exc + "");
                });
            }
        return _tradingSubject;
      }
    }

    public static DateTime GetVirtualServerTime(this IList<Rate> rates, int barMinutes) {
      if(rates == null || rates.Count == 0)
        return DateTime.MinValue;
      return rates.Last().StartDate;
    }
    public static readonly DateTime FX_DATE_NOW = DateTime.FromOADate(0);
    public static int GetLotSize(double lot, int baseUnitSize, bool useCeiling) {
      return (lot / baseUnitSize).ToInt(useCeiling) * baseUnitSize;
    }
    public static int GetLotSize(double lot, int baseUnitSize) {
      return GetLotSize(lot, baseUnitSize, false);
    }
    public static int GetLotstoTrade(double rate, string symbol, double balance, double leverage, double tradeRatio, int baseUnitSize) {
      var amountToTrade = symbol.IsCurrenncy()
        ? balance * leverage * tradeRatio
        : rate == 0
        ? 0
        : balance * leverage / rate * tradeRatio;
      return GetLotSize(amountToTrade, baseUnitSize);
    }
    public static double MoneyAndLotToPips(this ITradesManager tm, double money, int lots, string pair) {
      return tm == null || !tm.TryGetPrice(pair, out Price price) ? double.NaN : MoneyAndLotToPips(pair, money, lots, tm.RateForPipAmount(price), tm.GetPipSize(pair));
    }
    public static double MarginRequired(int lot, double baseUnitSize, double mmr) {
      return lot / baseUnitSize * mmr;
    }
    public static string AccountCurrency = null;
    static string[] PairCurrencies(string pair) {
      var ret = Regex.Matches(Regex.Replace(pair, "[^a-z]", "", RegexOptions.IgnoreCase), @"\w{3}")
        .Cast<Match>().Select(m => m.Value.ToUpper()).ToArray();
      //if(ret.Length != 2)
      //  throw new ArgumentException(new { pair, error = "Wrong format" } + "");
      return ret;
    }
    public static double PipByPair(string pair, Func<double> left, Func<double> right, Func<double> middle) {
      if(string.IsNullOrEmpty(AccountCurrency))
        throw new ArgumentNullException(new { AccountCurrency } + "");
      Func<double> error = () => { throw new NotSupportedException(new { pair, error = "Not Supported" } + ""); };
      var acc = AccountCurrency.ToUpper();
      var foos = new[] {
        new { acc, a = left} ,
        new { acc, a = right}
      };
      var curs = PairCurrencies(pair);
      return (curs.Length > 1 && curs.All(c => c.IsCurrenncy())
        ? PairCurrencies(pair)
        .Where(cur => cur.IsCurrenncy())
        .Zip(foos, (c, f) => new { ok = c == f.acc, f.a })
        .Where(a => a.ok)
        .Select(a => a.a)
        .DefaultIfEmpty(error)
        .First()
        : middle)
        ();
    }
    #region PipAmount
    /// <summary>
    /// Pip Dollar Value
    /// </summary>
    /// <param name="pair"></param>
    /// <param name="lot"></param>
    /// <param name="rate">Usually Ask or (Ask+Bid)/2</param>
    /// <param name="pipSize">EUR/USD:0.0001, USD/JPY:0.01</param>
    /// <returns></returns>
    public static double PipAmount(string pair, int lot, double rate, double pipSize) {
      return PipsAndLotToMoney(pair, 1, lot, rate, pipSize);
    }
    #endregion
    public static double PipsAndLotToMoney(string pair, double pips, int lot, double rate, double pipSize) {
      var pl = pips * lot;
      return PipByPair(pair,
        () => pl * pipSize / rate,
        () => pl / 10000,
        () => pl * pipSize);
    }
    public static double MoneyAndLotToPips(string pair, double money, int lot, double rate, double pipSize) {
      if(money == 0 || lot == 0 || double.IsNaN(rate))
        return 0;
      var ml = money / lot;
      return PipByPair(pair,
        () => ml * rate / pipSize,
        () => ml * 10000,
        () => ml / pipSize);
    }
    #region PipCost
    public static double PipCost(string pair, double rate, int baseUnit, double pipSize) {
      return PipByPair(pair,
        () => baseUnit * pipSize / rate,
        () => baseUnit / 10000.0,
        () => pipSize
        );
    }
    #endregion
    public static double InPoins(ITradesManager tm, string pair, double? price) {
      return (price * tm.GetPipSize(pair)).GetValueOrDefault();
    }
    public static double MarginLeft2(int lot, double pl, double balance, double mmr, double baseUnitSize, double pipAmount) {
      return balance - MarginRequired(lot, baseUnitSize, mmr) + pl * pipAmount;
    }
    public static double PipToMarginCall(int lot, double pl, double balance, double mmr, double baseUnitSize, double pipAmount) {
      return MarginLeft2(lot, pl, balance, mmr, baseUnitSize, pipAmount) / pipAmount;
    }
    public static int LotToMarginCall(int pipsToMC, double balance, int baseUnitSize, double pipCost, double MMR) {
      var lot = balance / (pipsToMC * pipCost / baseUnitSize + 1.0 / baseUnitSize * MMR);
      return pipsToMC < 1 ? 0 : GetLotSize(lot, baseUnitSize);
    }
    public static double InPips(double? price, double pipSize) { return price.GetValueOrDefault() / pipSize; }
    public static bool IsInPips(this double value, double curentPrice) { return value / curentPrice < .5; }
    public static int GetMaxBarCount(int periodsBack, DateTime StartDate, List<Rate> Bars) {
      return Math.Max(StartDate == TradesManagerStatic.FX_DATE_NOW ? 0 : Bars.Count(b => b.StartDate >= StartDate), periodsBack);
    }
    public static double PriceFromProfitAmount(double profitAmount, double position, int multiplier, double openPrce)
      => profitAmount / position / multiplier + openPrce;
  }
}
