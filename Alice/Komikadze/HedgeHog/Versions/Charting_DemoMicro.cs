public class Charting : Window, INotifyPropertyChanged, IComponentConnector
{
    // Fields
    private ObservableCollection<PriceBar> _barsDataSource;
    private bool _canTrade;
    private bool _contentLoaded;
    private int _timeFrame;
    private IAsyncResult asyncRes;
    private const int barsMax = 300;
    internal ComboBox cbPositionFoo;
    internal ComboBox cbRegressionMode;
    internal CheckBox chkCloseOnProfitOnly;
    internal CheckBox chkCloseOnReverseOnly;
    internal CheckBox chkCloseTrade;
    internal CheckBox chkDB;
    internal CheckBox chkFastClose;
    internal CheckBox chkOpenTrade;
    internal CheckBox chkSaveVoltsToFile;
    internal CheckBox chkShowOtherCorridors;
    internal CheckBox chkSpeedWeight;
    internal CheckBox chkTradeHighLow;
    internal CheckBox chkWaveWeight;
    internal DataGrid dgBuySellBars;
    private TimeSpan FindMaximasPeakAndValleyInterval;
    private ThreadScheduler getVoltagesScheduler;
    internal Label lblBSMinMax;
    internal Label lblMainWave;
    internal Label lblOpenBuy;
    internal Label lblOpenSell;
    internal Label lblPeriodsMax;
    internal Label lblPower;
    internal Label lblServerTime;
    internal Label lblUpDown;
    internal Label lblVolatility;
    public int LotsToTrade;
    private Volt PeakVolt;
    internal Popup popUpSettings;
    internal Popup popVolts;
    private object priceSync;
    private List<FXCoreWrapper.Rate> Rates;
    private bool runPrice;
    private Scheduler showBarsScheduler;
    private Scheduler showCorridorScheduler;
    private double spreadAverage10Min;
    private double spreadAverage10MinInPips;
    private double spreadAverage15Min;
    private double spreadAverage15MinInPips;
    private double spreadAverage300InPips;
    private double spreadAverage5Min;
    private double spreadAverage5MinInPips;
    private List<FXCoreWrapper.Tick> Ticks;
    private DateTime TimeFrameTime;
    private DispatcherTimer timer;
    internal TextBox txtBSPeriodMin;
    internal TextBox txtEdgeMargin;
    internal TextBox txtfirstBarRow;
    internal TextBox txtFoo;
    internal TextBox txtMaximasCount;
    internal TextBox txtPositionsAddOn;
    internal TextBox txtProfitMin;
    internal TextBox txtSellOnProfit;
    internal TextBox txtSpreadMinutesBack;
    internal TextBox txtSpreadRatio;
    internal TextBox txtVolatilityMin;
    internal TextBox txtVoltageCMAPeriod;
    internal TextBox txtVoltageTimeFrame;
    internal TextBox txtWaveMinRatio;
    private Volt ValleyVolt;
    private double volatility;
    private double? voltageAvgHigh;
    private double? voltageAvgLow;
    private List<Volt> voltages;
    private List<FXCoreWrapper.Volt> VoltagesByTick;
    private List<Volt> voltagesCorridor;
    internal StackPanel wpMain;

    // Events
    public event Action PriceGridChanged;

    public event PriceGridErrorHandler PriceGridError;

    public event PropertyChangedEventHandler PropertyChanged;

    public event EventHandler<TickChangedEventArgs> TicksChanged;

    // Methods
    public Charting()
    {
        this.timer = new DispatcherTimer();
        this.LotsToTrade = 1;
        this.voltageAvgHigh = null;
        this.voltageAvgLow = null;
        this._barsDataSource = new ObservableCollection<PriceBar>();
        this.priceSync = new object();
        this._timeFrame = 300;
        this.TimeFrameTime = DateTime.MinValue;
        this.voltages = new List<Volt>();
        this.voltagesCorridor = new List<Volt>();
        this.VoltagesByTick = new List<FXCoreWrapper.Volt>();
        this.FindMaximasPeakAndValleyInterval = new TimeSpan(0, 1, 0);
        this.InitializeComponent();
        this.timer.Interval = new TimeSpan(0, 1, 0);
        this.timer.Tick += new EventHandler(this.timer_Tick);
        this.timer.Start();
        this.showBarsScheduler = new Scheduler(base.Dispatcher);
        this.showCorridorScheduler = new Scheduler(base.Dispatcher);
        this.getVoltagesScheduler = new ThreadScheduler(new TimeSpan(0, 0, 1));
    }

    public Charting(FXCoreWrapper FW) : this()
    {
        this.fw = FW;
        this.fw.PriceChanged += new FXCoreWrapper.PriceChangedHandler(this.fw_PriceChanged);
    }

    private void dgBuySellBars_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        DataGridBoundColumn column = (DataGridBoundColumn) e.Column;
        MemberDescriptor descriptor = e.PropertyDescriptor as MemberDescriptor;
        DisplayFormatAttribute displayFormat = descriptor.Attributes.OfType<DisplayFormatAttribute>().FirstOrDefault<DisplayFormatAttribute>();
        if (displayFormat != null)
        {
            column.Binding.StringFormat = displayFormat.DataFormatString;
        }
        DisplayNameAttribute displayName = descriptor.Attributes.OfType<DisplayNameAttribute>().FirstOrDefault<DisplayNameAttribute>();
        if (displayName != null)
        {
            if (displayName.DisplayName == "")
            {
                column.Visibility = Visibility.Collapsed;
            }
            else
            {
                column.Header = displayName.DisplayName;
            }
        }
    }

    private void FindMaximas(DateTime dateLast)
    {
        try
        {
            int maxExtreams = this.maximasCount;
            List<Volt> maximas = new List<Volt>();
            int period = 1;
            var rateDistances = this.GetVoltage(this.TicksInTimeFrame.ToArray<FXCoreWrapper.Tick>()).Select((Func<RateDistance, int, <>f__AnonymousType4<double, double, double, double, DateTime>>) ((rd, i) => new { Average = (rd.AverageAsk + rd.AverageBid) / 2.0, AverageAsk = rd.AverageAsk, AverageBid = rd.AverageBid, Volts = rd.Distance / ((double) (i + 1)), StartDate = rd.StartDate }));
            int count = rateDistances.Count();
            Func<bool> checkMaximasSpread =  => (maximas.Max<Volt>(((Func<Volt, double>) (m => m.Average))) - maximas.Min<Volt>(((Func<Volt, double>) (m => m.Average)))) >= this.spreadAverage5Min;
            Action<bool> getMaximas = delegate (bool clearIfTooNarrow) {
                maximas.Clear();
                for (int i = 2; i < count; i++)
                {
                    int prd = (period * 2) + (((period % 2) == 0) ? 1 : 0);
                    var range = rateDistances.Skip(i).Take(prd).Select(((Func<<>f__AnonymousType4<double, double, double, double, DateTime>, int, <>f__AnonymousType1<double, DateTime, double, int, double, double>>) ((pb, row) => new { Volts = pb.Volts, StartDate = pb.StartDate, Average = pb.Average, Row = row, AverageAsk = pb.AverageAsk, AverageBid = pb.AverageBid }))).ToArray();
                    double maxVolt = range.Max((Func<<>f__AnonymousType1<double, DateTime, double, int, double, double>, double>) (pb => pb.Volts));
                    var priceBar = (from pb in range
                        where pb.Volts == maxVolt
                        select pb).SingleOrDefault();
                    if (priceBar.Row == period)
                    {
                        maximas.Add(new Volt(priceBar.StartDate, priceBar.Volts, Math.Round(priceBar.AverageAsk, this.fw.Digits), Math.Round(priceBar.AverageBid, this.fw.Digits)));
                    }
                }
            };
            getMaximas(false);
            while (maximas.Count >= maxExtreams)
            {
                period++;
                getMaximas(false);
            }
            if (period > 1)
            {
                this.voltageTimeFrame = --period;
            }
            getMaximas(false);
            if (!checkMaximasSpread())
            {
                period = 1;
                getMaximas(false);
                while (maximas.Count >= (maxExtreams + 1))
                {
                    period++;
                    getMaximas(false);
                }
                if (period > 1)
                {
                    this.voltageTimeFrame = --period;
                }
                getMaximas(false);
            }
            while ((maximas.Count == 0) && (period > 1))
            {
                this.voltageTimeFrame = --period;
                getMaximas(false);
            }
            this.voltageTimeFrame = Math.Max(1, period);
            this.voltages = (from m in maximas
                orderby m.StartDate descending
                select m).ToList<Volt>();
            base.Dispatcher.BeginInvoke(delegate {
                this.DC.Voltage = new ObservableCollection<Volt>(this.voltages);
            }, new object[0]);
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    private void FindMaximas_Strict(DateTime dateLast, int period)
    {
        try
        {
            <>c__DisplayClass6e CS$<>8__locals6f;
            <>c__DisplayClass6e CS$<>8__locals6f = CS$<>8__locals6f;
            List<Volt> maximas = new List<Volt>();
            var rateDistances = this.GetVoltage(this.TicksInTimeFrame.ToArray<FXCoreWrapper.Tick>()).Select((Func<RateDistance, int, <>f__AnonymousType4<double, double, double, double, DateTime>>) ((rd, i) => new { Average = (rd.AverageAsk + rd.AverageBid) / 2.0, AverageAsk = rd.AverageAsk, AverageBid = rd.AverageBid, Volts = rd.Distance / ((double) (i + 1)), StartDate = rd.StartDate }));
            int count = rateDistances.Count();
            Action<bool> getMaximas = delegate (bool clearIfTooNarrow) {
                maximas.Clear();
                for (int i = 2; i < count; i++)
                {
                    <>c__DisplayClass6e classe1 = CS$<>8__locals6f;
                    var range = rateDistances.Skip(i).Take(((CS$<>8__locals6f.period * 2) + 1)).Select(((Func<<>f__AnonymousType4<double, double, double, double, DateTime>, int, <>f__AnonymousType1<double, DateTime, double, int, double, double>>) ((pb, row) => new { Volts = pb.Volts, StartDate = pb.StartDate, Average = pb.Average, Row = row, AverageAsk = pb.AverageAsk, AverageBid = pb.AverageBid }))).ToArray();
                    double maxVolt = range.Max((Func<<>f__AnonymousType1<double, DateTime, double, int, double, double>, double>) (pb => pb.Volts));
                    var priceBar = (from pb in range
                        where pb.Volts == maxVolt
                        select pb).SingleOrDefault();
                    if (priceBar.Row == CS$<>8__locals6f.period)
                    {
                        maximas.Add(new Volt(priceBar.StartDate, priceBar.Volts, Math.Round(priceBar.AverageAsk, CS$<>8__locals6f.<>4__this.fw.Digits), Math.Round(priceBar.AverageBid, CS$<>8__locals6f.<>4__this.fw.Digits)));
                    }
                }
                if (clearIfTooNarrow && (Math.Abs((double) (maximas.Max<Volt>(((Func<Volt, double>) (m => m.Average))) - maximas.Min<Volt>(((Func<Volt, double>) (m => m.Average))))) < CS$<>8__locals6f.<>4__this.spreadAverage5Min))
                {
                    maximas.Clear();
                }
            };
            getMaximas(false);
            this.voltagesCorridor = (from m in maximas
                orderby m.StartDate descending
                select m).ToList<Volt>();
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    private void FindMaximasPeakAndValley()
    {
        try
        {
            DateTime now = DateTime.Now;
            this.VoltagesByTick = this.fw.GetVoltageByTick(this.TicksInTimeFrame, this.regressionMode);
            if (this.saveVoltsToFile)
            {
                try
                {
                    string s = "";
                    (from v in this.VoltagesByTick
                        orderby v.StartDate
                        select v).ToList<FXCoreWrapper.Volt>().ForEach(delegate (FXCoreWrapper.Volt v) {
                        object CS$0$0000 = s;
                        s = string.Concat(new object[] { CS$0$0000, v.StartDate, ",", v.Volts, ",", v.Price, ",", v.PriceAvg, Environment.NewLine });
                    });
                    File.WriteAllText(@"C:\Volts.csv", s);
                }
                catch
                {
                }
            }
            FXCoreWrapper.Volt volts = (from rd in this.VoltagesByTick
                where rd.Price > rd.PriceAvg
                orderby rd.Volts
                select rd).Last<FXCoreWrapper.Volt>();
            this.PeakVolt = new Volt(volts.StartDate, volts.Volts, volts.Price, volts.AskMax);
            volts = (from rd in this.VoltagesByTick
                where rd.Price < rd.PriceAvg
                orderby rd.Volts
                select rd).Last<FXCoreWrapper.Volt>();
            this.ValleyVolt = new Volt(volts.StartDate, volts.Volts, volts.Price, volts.BidMin);
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    public void fw_PriceChanged()
    {
        this.fw_PriceChanged(null);
    }

    private void fw_PriceChanged(Price Price)
    {
        lock (this.priceSync)
        {
            this.ProcessPrice();
        }
    }

    private void GetBars(int Period, DateTime StartDate, DateTime EndDate)
    {
        if (this.Rates == null)
        {
            this.Rates = new List<FXCoreWrapper.Rate>();
        }
        this.GetBars<FXCoreWrapper.Rate>(0, StartDate, EndDate, ref this.Rates);
    }

    private void GetBars<BT>(int Period, DateTime StartDate, DateTime EndDate, ref List<BT> Bars) where BT: FXCoreWrapper.BarBase, new()
    {
        if (Bars.Count == 0)
        {
            Bars = this.fw.GetBarsBase<BT>(Period, StartDate, EndDate).OfType<BT>().ToList<BT>();
        }
        if (StartDate < Bars.Min<BT, DateTime>(b => b.StartDate))
        {
            Bars = Bars.Union<BT>(this.fw.GetBarsBase<BT>(Period, StartDate, Bars.Min<BT, DateTime>(b => b.StartDate)).OfType<BT>()).ToList<BT>();
        }
        if ((EndDate == DateTime.FromOADate(0.0)) || (EndDate > Bars.Max<BT, DateTime>(b => b.StartDate)))
        {
            Bars = Bars.Union<BT>(this.fw.GetBarsBase<BT>(Period, Bars.Max<BT, DateTime>(b => b.StartDate), EndDate).OfType<BT>()).ToList<BT>();
        }
    }

    private void GetTicks(DateTime StartDate, DateTime EndDate)
    {
        <>c__DisplayClass25 CS$<>8__locals26;
        if (this.Ticks == null)
        {
            if (this.doDB)
            {
                <>c__DisplayClass25 CS$<>8__locals26 = CS$<>8__locals26;
                Table<t_Tick> t_Ticks = new ForexDBDataContext().t_Ticks;
                DateTime maxDBStartDate = (t_Ticks.Count<t_Tick>() > 0) ? t_Ticks.Max<t_Tick, DateTime>(t => t.StartDate) : DateTime.MaxValue;
                if (maxDBStartDate < StartDate)
                {
                    this.GetTicks(maxDBStartDate, EndDate);
                    return;
                }
                DateTime ed = (EndDate == DateTime.FromOADate(0.0)) ? DateTime.MaxValue : EndDate;
                this.Ticks = (from t in new ForexDBDataContext().t_Ticks
                    where (t.StartDate > StartDate) && (t.StartDate <= ed)
                    select new FXCoreWrapper.Tick(t.StartDate, t.Ask, t.Bid, true)).ToList<FXCoreWrapper.Tick>();
            }
            else
            {
                this.Ticks = new List<FXCoreWrapper.Tick>();
            }
        }
        else
        {
            this.Ticks = (from b in this.Ticks
                where b.IsHistory
                select b).ToList<FXCoreWrapper.Tick>();
        }
        this.GetBars<FXCoreWrapper.Tick>(0, StartDate, EndDate, ref this.Ticks);
        if (this.doDB)
        {
            <>c__DisplayClass25 CS$<>8__locals26 = CS$<>8__locals26;
            Table<t_Tick> t_Ticks = new ForexDBDataContext().t_Ticks;
            DateTime maxDBStartDate = (t_Ticks.Count<t_Tick>() > 0) ? t_Ticks.Max<t_Tick, DateTime>(t => t.StartDate) : DateTime.MinValue;
            t_Ticks.InsertAllOnSubmit<t_Tick>(from t in this.Ticks
                where t.StartDate > maxDBStartDate
                select new t_Tick { StartDate = t.StartDate, Ask = t.Ask, Bid = t.Bid, Pair = this.fw.Pair });
            t_Ticks.Context.SubmitChanges();
        }
        this.Ticks = (from b in this.Ticks
            orderby b.StartDate
            orderby b.Row
            select b).Select<FXCoreWrapper.Tick, FXCoreWrapper.Tick>(((Func<FXCoreWrapper.Tick, int, FXCoreWrapper.Tick>) ((b, i) => new FXCoreWrapper.Tick { Ask = b.Ask, Bid = b.Bid, StartDate = b.StartDate, Row = (double) (i + 1), IsHistory = b.IsHistory }))).ToList<FXCoreWrapper.Tick>();
    }

    private void GetTimeFrame()
    {
        TimeSpan CS$0$0000 = (TimeSpan) (this.fw.ServerTime - this.TicksInTimeFrame.Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate));
        this.TimeFrame = ((int) CS$0$0000.TotalMinutes) / this.bsPeriodMin;
        base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, delegate {
            this.GetVolatility();
        });
    }

    private void GetTimeFrame_(int StartPeriod, double Margin)
    {
        Summary summary = this.fw.GetSummary();
        int startPeriod = StartPeriod * (this.moveTimeFrameByPos ? ((int) Lib.Max3(1.0, summary.BuyPositions, summary.SellPositions)) : 1);
        try
        {
            DateTime startTime = this.fw.ServerTime.AddMinutes((double) ((-startPeriod * this.bsPeriodMin) * 3));
            if (this.Rates == null)
            {
                this.Rates = this.fw.GetBars(this.bsPeriodMin, DateTime.Now.AddDays(-1.0));
            }
            this.GetBars<FXCoreWrapper.Rate>(this.bsPeriodMin, startTime, DateTime.FromOADate(0.0), ref this.Rates);
            double spreadShort = (from r in this.Rates
                where r.Row <= startPeriod
                select r).Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => r.Spread));
            double spreadLong = spreadShort;
            foreach (FXCoreWrapper.Rate rate in from r in this.Rates
                where r.Row > startPeriod
                select r)
            {
                spreadLong = ((spreadLong * startPeriod) + (rate.AskHigh - rate.AskLow)) / ((double) (++startPeriod));
                if ((spreadLong > (spreadShort * Margin)) || (spreadLong < (spreadShort / Margin)))
                {
                    break;
                }
            }
            this.TimeFrame = startPeriod;
            this.spreadAverage300InPips = Math.Round((double) (this.Rates.Take<FXCoreWrapper.Rate>((this.TimeFrame * 2)).Average<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (r => r.Spread))) / this.fw.PointSize), 1);
            base.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, delegate {
                this.GetVolatility();
            });
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    private DateTime GetTimeFrameStart()
    {
        return this.fw.ServerTime.AddMinutes((double) (-this.TimeFrame * this.bsPeriodMin));
    }

    private void GetVolatility()
    {
        try
        {
            DateTime startTime = this.fw.ServerTime.AddMinutes((double) (-this.TimeFrame * this.bsPeriodMin));
            List<FXCoreWrapper.Rate> Rates1 = this.fw.GetBars(1, startTime);
            List<FXCoreWrapper.Rate> Rates5 = this.fw.GetBars(5, startTime);
            List<FXCoreWrapper.Rate> Rates15 = this.fw.GetBars(15, startTime);
            double avg1 = Rates1.Average<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (r => r.Spread))) / this.fw.PointSize;
            double priceSpread = (from t in this.Ticks
                where t.StartDate >= startTime
                select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => (t.Ask - t.Bid)));
            this.spreadAverage5Min = Rates5.Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => (r.SpreadMin + priceSpread)));
            this.spreadAverage5MinInPips = this.spreadAverage5Min / this.fw.PointSize;
            this.spreadAverage15Min = Rates15.Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => (r.SpreadMin + priceSpread)));
            this.spreadAverage15MinInPips = this.spreadAverage15Min / this.fw.PointSize;
            this.volatility = Math.Round((double) (this.spreadAverage5MinInPips / avg1), 1);
            Lib.SetLabelText(this.lblVolatility, string.Format("{0:n1}/{1:n1}/{2:n1}={3:n1}>", new object[] { this.spreadAverage15MinInPips, this.spreadAverage5MinInPips, avg1, this.spreadAverage5MinInPips / avg1 }));
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    private RateDistance[] GetVoltage(IEnumerable<FXCoreWrapper.Tick> ticks)
    {
        double spread;
        var tickDistances = (from t in ticks
            orderby t.StartDate
            select t into t
            join t1 in ticks on t.Row equals t1.Row + 1.0
            select new { Distance = Math.Abs((double) (t.Ask - t1.Ask)), Ask = t.Ask, Bid = t.Bid, StartDate = t.StartDate.AddSeconds((double) -t.StartDate.Second) }).ToArray();
        double distance = 0.0;
        int i = 0;
        return (from t in from t in tickDistances
            orderby t.StartDate descending
            select t
            group t by t.StartDate into g
            let l = ++i
            select new { <>h__TransparentIdentifier30 = <>h__TransparentIdentifier30, MA = g.Skip(l).Take(5).DefaultIfEmpty().Average(delegate (<>f__AnonymousType0<double, double, double, DateTime> ga) {
                if (ga != null)
                {
                    return (ga.Ask + ga.Bid) / 2.0;
                }
                return 0.0;
            }) }).Select(delegate (<>f__AnonymousType3<<>f__AnonymousType2<IGrouping<DateTime, <>f__AnonymousType0<double, double, double, DateTime>>, int>, double> <>h__TransparentIdentifier31) {
            if (<>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate50 == null)
            {
                <>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate50 = t => t.Bid;
                if (<>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate51 == null)
                {
                    <>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate51 = t => t.Distance;
                }
            }
            return new RateDistance(spread = Math.Min((double) (<>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Max(((Func<<>f__AnonymousType0<double, double, double, DateTime>, double>) (t => t.Ask))) - <>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Min(((Func<<>f__AnonymousType0<double, double, double, DateTime>, double>) (t => t.Ask)))), (double) (<>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Max(((Func<<>f__AnonymousType0<double, double, double, DateTime>, double>) (t => t.Bid))) - <>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Min(<>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate50))), distance = ((spread > 0.0) ? (<>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Sum(<>c__DisplayClass4a.CS$<>9__CachedAnonymousMethodDelegate51) / spread) : 0.0) + distance, <>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Average((Func<<>f__AnonymousType0<double, double, double, DateTime>, double>) (t => t.Ask)), <>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Average((Func<<>f__AnonymousType0<double, double, double, DateTime>, double>) (t => t.Bid)), <>h__TransparentIdentifier31.MA, <>h__TransparentIdentifier31.<>h__TransparentIdentifier30.g.Key);
        }).ToArray<RateDistance>();
    }

    [DebuggerNonUserCode]
    public void InitializeComponent()
    {
        if (!this._contentLoaded)
        {
            this._contentLoaded = true;
            Uri resourceLocater = new Uri("/HedgeHog;component/charting.xaml", UriKind.Relative);
            Application.LoadComponent(this, resourceLocater);
        }
    }

    private void OnTicksChanged(FXCoreWrapper.Tick[] ticks, Volt voltageHight, Volt voltageCurr, double netBuy, double netSell)
    {
        this.OnTicksChanged(ticks, (voltageHight != null) ? voltageHight.Average : 0.0, (voltageCurr != null) ? voltageCurr.Average : 0.0, netBuy, netSell, (voltageHight != null) ? voltageHight.StartDate : DateTime.MinValue, (voltageCurr != null) ? voltageCurr.StartDate : DateTime.MinValue);
    }

    private void OnTicksChanged(FXCoreWrapper.Tick[] ticks, double voltageHight, double voltageCurr, double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr)
    {
        double[] CS$0$0000 = new double[2];
        this.OnTicksChanged(ticks, voltageHight, 0.0, 0.0, voltageCurr, netBuy, netSell, timeHigh, timeCurr, CS$0$0000, null);
    }

    private void OnTicksChanged(FXCoreWrapper.Tick[] ticks, double voltageHight, double voltageCurr, double priceMaxAverage, double priceMinAverage, double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverage, List<FXCoreWrapper.Volt> voltsByTick)
    {
        if (this.TicksChanged != null)
        {
            this.TicksChanged(this, new TickChangedEventArgs(ticks, voltageHight, voltageCurr, priceMaxAverage, priceMinAverage, netBuy, netSell, timeHigh, timeCurr, priceAverage, voltsByTick));
        }
    }

    private void priceCallBack(IAsyncResult res)
    {
        if ((res != null) && (res.AsyncState != null))
        {
            ((Action) res.AsyncState).EndInvoke(res);
        }
        if (this.runPrice)
        {
            this.fw_PriceChanged(null);
        }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ProcessPrice()
    {
        try
        {
            <>c__DisplayClass138 CS$<>8__locals139;
            bool CS$0$0000;
            bool CS$0$0001;
            double CS$0$0003;
            Summary summary;
            if ((this.fw == null) || (this.fw.Desk == null))
            {
                return;
            }
            int periodMin = this.bsPeriodMin;
            double waveMinRatio = this.waveMinRatio;
            double edgeMargin = this.edgeMargin;
            double profitMin = this.profitMin;
            double volatilityMin = this.volatilityMin;
            int voltageTimeFrame = this.voltageTimeFrame;
            Account account = this.fw.GetAccount();
            bool canAddTrade = true;
            this.CloseBuy = CS$0$0000 = false;
            this.CloseSell = CS$0$0001 = CS$0$0000;
            this.GoSell = this.GoBuy = CS$0$0001;
            this.StopLossSell = CS$0$0003 = 0.0;
            this.TakeProfitBuy = this.TakeProfitSell = CS$0$0003;
            this.LotsToTrade = 1;
            this.DencityRatio = 1.0;
            DateTime timeNow = DateTime.Now;
            if ((this.Ticks == null) || (this.Ticks.Count == 0))
            {
                this.Ticks = this.fw.GetTicks(this.spreadMinutesBack).ToList<FXCoreWrapper.Tick>();
                this.GetTimeFrame();
            }
            DateTime serverTime = this.fw.ServerTime;
            int bars = ((this.Rates == null) || (this.Rates.Count<FXCoreWrapper.Rate>() == 0)) ? this.TimeFrame : ((int) Math.Ceiling((double) ((serverTime - this.Rates.First<FXCoreWrapper.Rate>().StartDate).TotalMinutes / ((double) periodMin))));
            if (bars > 1)
            {
                this.GetTicks(this.fw.ServerTime.AddMinutes((double) (-this.TimeFrame * periodMin)).Round(), DateTime.FromOADate(0.0));
                this.GetTimeFrame();
                IEnumerable<FXCoreWrapper.Rate> rates = this.fw.GetBarsBase<FXCoreWrapper.Rate>(periodMin, serverTime.AddMinutes((double) (-bars * periodMin)).Round(), DateTime.FromOADate(0.0)).OfType<FXCoreWrapper.Rate>();
                this.Rates = (from r in (this.Rates == null) ? rates : rates.Union<FXCoreWrapper.Rate>(this.Rates)
                    orderby r.StartDate descending
                    select r).Select<FXCoreWrapper.Rate, FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, int, FXCoreWrapper.Rate>) ((r, i) => new FXCoreWrapper.Rate { AskClose = r.AskClose, AskHigh = r.AskHigh, AskLow = r.AskLow, AskOpen = r.AskOpen, BidClose = r.BidClose, BidHigh = r.BidHigh, BidLow = r.BidLow, BidOpen = r.BidOpen, StartDate = r.StartDate, ServerTime = r.ServerTime, Row = (double) i }))).ToList<FXCoreWrapper.Rate>();
                summary = this.fw.GetSummary() ?? new Summary { PriceCurrent = this.fw.GetPrice() };
            }
            else
            {
                summary = this.fw.GetSummary() ?? new Summary { PriceCurrent = this.fw.GetPrice() };
                FXCoreWrapper.Rate rateLast = this.Rates.First<FXCoreWrapper.Rate>();
                rateLast.AskClose = summary.PriceCurrent.Ask;
                rateLast.AskHigh = Math.Max(rateLast.AskHigh, summary.PriceCurrent.Ask);
                rateLast.AskLow = Math.Min(rateLast.AskLow, summary.PriceCurrent.Ask);
                rateLast.BidClose = summary.PriceCurrent.Bid;
                rateLast.BidHigh = Math.Max(rateLast.BidHigh, summary.PriceCurrent.Bid);
                rateLast.BidLow = Math.Min(rateLast.BidLow, summary.PriceCurrent.Bid);
                rateLast.ServerTime = serverTime;
                FXCoreWrapper.Tick <>g__initLocal90 = new FXCoreWrapper.Tick {
                    Ask = rateLast.AskClose,
                    Bid = rateLast.BidClose,
                    Row = this.Ticks.Max<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (b => (b.Row + 1.0))),
                    StartDate = serverTime
                };
                this.Ticks.Add(<>g__initLocal90);
            }
            Func<PriceBar, bool> filter = r => r.Row >= this.firstBarRow;
            FXCoreWrapper.Rate ratePrev = this.Rates.Skip<FXCoreWrapper.Rate>(1).FirstOrDefault<FXCoreWrapper.Rate>();
            FXCoreWrapper.Rate rateCurr = this.Rates.FirstOrDefault<FXCoreWrapper.Rate>();
            if (ratePrev == null)
            {
                return;
            }
            double rowCurr;
            double spreadAsk;
            double spreadBid;
            <>c__DisplayClass138 CS$<>8__locals139 = CS$<>8__locals139;
            DateTime startTime = DateTime.Now.AddMinutes((double) (-this.spreadMinutesBack * periodMin));
            double spreadAverage = (from b in this.Rates
                where b.StartDate >= startTime
                select b).Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => r.Spread));
            double spreadAverageInPips = Math.Round((double) (spreadAverage / this.fw.PointSize), 1);
            double askMax = double.MinValue;
            double askMin = double.MaxValue;
            double bidMax = double.MinValue;
            double bidMin = double.MaxValue;
            DateTime firstBarDate = DateTime.MaxValue;
            serverTime = this.fw.ServerTime;
            RateDistance[] rateDistances = this.GetVoltage(this.TicksInTimeFrame);
            PriceBar[] bsPeriods = (from r in (from r in this.Rates.Take<FXCoreWrapper.Rate>(this.TimeFrame).Select((Func<FXCoreWrapper.Rate, int, <>f__AnonymousType5<double, double, double, double, double, double, DateTime, double, double, double, double>>) ((r, i) => new { AskHigh = askMax = Math.Round(Math.Max(askMax, r.AskHigh), CS$<>8__locals139.<>4__this.fw.Digits), AskLow = askMin = Math.Round(Math.Min(askMin, r.AskLow), CS$<>8__locals139.<>4__this.fw.Digits), BidLow = bidMin = Math.Round(Math.Min(bidMin, r.BidLow), CS$<>8__locals139.<>4__this.fw.Digits), BidHigh = bidMax = Math.Round(Math.Max(bidMax, r.BidHigh), CS$<>8__locals139.<>4__this.fw.Digits), SpreadAsk = spreadAsk = Math.Max((double) (r.AskHigh - CS$<>8__locals139.<>4__this.Rates.Take<FXCoreWrapper.Rate>((i + 1)).Min<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (al => al.AskLow)))), (double) (CS$<>8__locals139.<>4__this.Rates.Take<FXCoreWrapper.Rate>((i + 1)).Max<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (al => al.AskHigh))) - r.AskLow)), SpreadBid = spreadBid = Math.Max((double) (r.BidHigh - CS$<>8__locals139.<>4__this.Rates.Take<FXCoreWrapper.Rate>((i + 1)).Min<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (al => al.BidLow)))), (double) (CS$<>8__locals139.<>4__this.Rates.Take<FXCoreWrapper.Rate>((i + 1)).Max<FXCoreWrapper.Rate>(((Func<FXCoreWrapper.Rate, double>) (al => al.BidHigh))) - r.BidLow)), StartDate = (i == 0) ? (firstBarDate = r.StartDate) : r.StartDate, Row = rowCurr = Math.Min((double) (((TimeSpan) (CS$<>8__locals139.serverTime - firstBarDate)).TotalMinutes / ((double) CS$<>8__locals139.periodMin)), (double) 1.0) + i, SpeedAsk = spreadAsk / (rowCurr * CS$<>8__locals139.periodMin), SpeedBid = spreadBid / (rowCurr * CS$<>8__locals139.periodMin), SpreadBar = (((r.AskHigh - r.AskLow) + r.BidHigh) - r.BidLow) / 2.0 }))
                join ri in rateDistances on r.StartDate equals ri.StartDate
                select new { AskHigh = r.AskHigh, AskLow = r.AskLow, BidLow = r.BidLow, BidHigh = r.BidHigh, SpreadAsk = r.SpreadAsk, SpreadBid = r.SpreadBid, SpeedAsk = r.SpeedAsk, SpeedBid = r.SpeedBid, Row = r.Row, StartDate = r.StartDate, SpreadBar = r.SpreadBar, Distance = ri.Distance, AverageAsk = ri.AverageAsk, AverageBid = ri.AverageBid }).Select((Func<<>f__AnonymousType6<double, double, double, double, double, double, double, double, double, DateTime, double, double, double, double>, int, PriceBar>) ((r, i) => new PriceBar { AskHigh = r.AskHigh, AskLow = r.AskLow, BidLow = r.BidLow, BidHigh = r.BidHigh, Spread = ((r.SpreadAsk + r.SpreadBid) / 2.0) / this.fw.PointSize, Speed = ((r.SpeedAsk + r.SpeedBid) / 2.0) / this.fw.PointSize, Distance = r.Distance, AverageAsk = Math.Round(r.AverageAsk, this.fw.Digits), AverageBid = Math.Round(r.AverageBid, this.fw.Digits), Row = r.Row, StartDate = r.StartDate }))
                orderby r.Volts descending
                select r).ThenByDescending<PriceBar, double>(r => r.Row).ToArray<PriceBar>();
            double timeSpanLast = DateTime.Now.Subtract(timeNow).TotalMilliseconds;
            if (this.showBarsScheduler != null)
            {
                this.showBarsScheduler.Cancel();
            }
            this.showBarsScheduler.Command = delegate {
                lock (CS$<>8__locals139.<>4__this.BarsDataSource)
                {
                    CS$<>8__locals139.<>4__this.Dispatcher.Invoke(DispatcherPriority.Send, delegate (object o) {
                        ObservableCollection<PriceBar> cv = (CS$<>8__locals139.<>4__this.dgBuySellBars.ItemsSource as ListCollectionView).SourceCollection as ObservableCollection<PriceBar>;
                        cv.Clear();
                        foreach (PriceBar bar in bsPeriods.Where<PriceBar>(CS$<>8__locals139.filter))
                        {
                            cv.Add(bar);
                        }
                        return null;
                    });
                }
            };
            Price price = summary.PriceCurrent;
            PriceBar barsBest = bsPeriods.Where<PriceBar>(filter).First<PriceBar>();
            double speedMax = bsPeriods.Where<PriceBar>(filter).Max<PriceBar>((Func<PriceBar, double>) (t => t.Speed));
            bool volatilityTrue = (this.spreadAverage10Min / this.spreadAverage5Min) >= volatilityMin;
            double upDown = Math.Round((double) (spreadAverageInPips / spreadAverageInPips), 1);
            this.CanTrade = volatilityTrue;
            bool goBuy = false;
            bool goSell = false;
            bool canBuy = false;
            bool canSell = false;
            double positionBuy = 0.0;
            double positionSell = 0.0;
            bool spreadTrue = false;
            bool fastBuy = false;
            bool fastSell = false;
            Action<Volt[]> forceTradeAction = delegate (Volt[] vBars) {
                if (CS$<>8__locals139.<>4__this.forceOpenTrade)
                {
                    CS$<>8__locals139.<>4__this.CanTrade = true;
                    if (CS$<>8__locals139.summary.BuyPositions > 0.0)
                    {
                        CS$<>8__locals139.<>4__this.GoBuy = true;
                    }
                    else if (CS$<>8__locals139.summary.SellPositions > 0.0)
                    {
                        CS$<>8__locals139.<>4__this.GoSell = true;
                    }
                    else
                    {
                        double bl = (from b in vBars
                            orderby b.Average
                            select b).First<Volt>().Average;
                        double bh = (from b in vBars
                            orderby b.Average
                            select b).Last<Volt>().Average;
                        if (Math.Abs((double) (positionBuy - bl)) < Math.Abs((double) (positionSell - bh)))
                        {
                            CS$<>8__locals139.<>4__this.GoBuy = true;
                        }
                        else
                        {
                            CS$<>8__locals139.<>4__this.GoSell = true;
                        }
                    }
                }
            };
            IEnumerable<FXCoreWrapper.Tick> tm = from t in this.Ticks
                where t.StartDate > serverTime.AddHours((double) ((-this.TimeFrame * periodMin) * 5))
                select t;
            DateTime tMax = tm.Max<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
            DateTime tMin = tm.Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
            TimeSpan CS$0$0010 = (TimeSpan) (tMax - tMin);
            int ticksPerMinuteAverageLong = (int) (((double) tm.Count<FXCoreWrapper.Tick>()) / CS$0$0010.TotalMinutes);
            tm = this.TicksInTimeFrame;
            int ticksPerMinuteAverageShort = tm.Count<FXCoreWrapper.Tick>() / (periodMin * this.TimeFrame);
            Func<bool, int> lotsToTrade_0 = buy => Math.Max(1, (buy ? ((int) summary.BuyPositions) : ((int) summary.SellPositions)) * this.positionsAddOn);
            Func<bool, int> lotsToTrade_1 = delegate (bool buy) {
                int pos = buy ? ((int) summary.BuyPositions) : ((int) summary.SellPositions);
                return ((pos * (pos + 1)) / 2) + 1;
            };
            Func<bool, int> lotsToTrade_2 = delegate (bool buy) {
                int pos = (buy ? ((int) summary.BuyPositions) : ((int) summary.SellPositions)) + 1;
                return (pos * (pos + 1)) / 2;
            };
            Func<bool, int>[] lotsToTradeFoos = new Func<bool, int>[] { lotsToTrade_0, lotsToTrade_1, lotsToTrade_2 };
            Action decideByVoltage_1 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class1 = CS$<>8__locals139;
                Volt bar1 = ((CS$<>8__locals139.<>4__this.voltages.Count == 0) || (barsBest.StartDate > CS$<>8__locals139.<>4__this.voltages.First<Volt>().StartDate)) ? new Volt(barsBest.StartDate, barsBest.Volts, barsBest.AverageAsk, barsBest.AverageBid) : null;
                Volt bar2 = (CS$<>8__locals139.<>4__this.voltages.Count == 0) ? null : CS$<>8__locals139.<>4__this.voltages.Where<Volt>(delegate (Volt b) {
                    if (bar1 != null)
                    {
                        return (Math.Abs((double) (bar1.Average - b.Average)) > class1.<>4__this.spreadAverage5Min);
                    }
                    return true;
                }).FirstOrDefault<Volt>();
                CS$<>8__locals139.<>4__this.OnTicksChanged(CS$<>8__locals139.<>4__this.Ticks.ToArray(), bar2, bar1, CS$<>8__locals139.summary.BuyAvgOpen, CS$<>8__locals139.summary.SellAvgOpen);
                if ((bar1 != null) && (bar2 != null))
                {
                    Volt[] barsHighLow = new Volt[] { bar1, bar2 };
                    Volt barHigh = (from b in barsHighLow
                        orderby b.AverageAsk
                        select b).Last<Volt>();
                    Volt barLow = (from b in barsHighLow
                        orderby b.AverageAsk
                        select b).First<Volt>();
                    positionBuy = Math.Round((double) ((price.Ask - barLow.AverageAsk) / (barHigh.AverageAsk - barLow.AverageAsk)), 2);
                    positionSell = Math.Round((double) ((barHigh.AverageBid - price.Bid) / (barHigh.AverageBid - barLow.AverageBid)), 2);
                    goBuy = (positionBuy > 0.0) && (positionBuy <= CS$<>8__locals139.edgeMargin);
                    goSell = (positionSell > 0.0) && (positionSell <= CS$<>8__locals139.edgeMargin);
                    canSell = canBuy = true;
                    CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                    CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                    CS$<>8__locals139.<>4__this.CloseBuy = CS$<>8__locals139.<>4__this.GoSell;
                    CS$<>8__locals139.<>4__this.CloseSell = CS$<>8__locals139.<>4__this.GoBuy;
                    if (CS$<>8__locals139.<>4__this.GoSell)
                    {
                        Trade trade = CS$<>8__locals139.<>4__this.fw.GetTradesToClose(false, (price.Spread * 2.0) / CS$<>8__locals139.<>4__this.fw.PointSize).FirstOrDefault<Trade>();
                        if (trade != null)
                        {
                            CS$<>8__locals139.<>4__this.fw.FixOrder_Close(trade.ID, CS$<>8__locals139.<>4__this.fw.Desk.FIX_CLOSE);
                            CS$<>8__locals139.<>4__this.GoSell = false;
                        }
                    }
                    if (CS$<>8__locals139.<>4__this.GoBuy)
                    {
                        Trade trade = CS$<>8__locals139.<>4__this.fw.GetTradesToClose(true, (price.Spread * 2.0) / CS$<>8__locals139.<>4__this.fw.PointSize).FirstOrDefault<Trade>();
                        if (trade != null)
                        {
                            CS$<>8__locals139.<>4__this.fw.FixOrder_Close(trade.ID, CS$<>8__locals139.<>4__this.fw.Desk.FIX_CLOSE);
                            CS$<>8__locals139.<>4__this.GoBuy = false;
                        }
                    }
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class1.<>4__this.lblOpenBuy, string.Format("{0:n0}", CS$<>8__locals13d.positionBuy * 100.0));
                        Lib.SetLabelText(class1.<>4__this.lblOpenSell, string.Format("{0:n0}", CS$<>8__locals13d.positionSell * 100.0));
                        class1.<>4__this.DC.BarHigh = Math.Round(barHigh.Average, class1.<>4__this.fw.Digits);
                        class1.<>4__this.DC.BarLow = Math.Round(barLow.Average, class1.<>4__this.fw.Digits);
                        class1.<>4__this.DC.VoltageSpread = (int) Math.Round((double) ((barHigh.Average - barLow.Average) / class1.<>4__this.fw.PointSize), 0);
                    }, new object[0]);
                }
            };
            Action decideByVoltage_2 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class1 = CS$<>8__locals139;
                Volt vBar1 = CS$<>8__locals139.<>4__this.voltages.FirstOrDefault<Volt>();
                Volt vBar2 = CS$<>8__locals139.<>4__this.voltages.Skip<Volt>(1).FirstOrDefault<Volt>();
                Volt barCurr = vBar1;
                Volt barPrev = (barCurr == vBar1) ? vBar2 : vBar1;
                CS$<>8__locals139.<>4__this.OnTicksChanged(CS$<>8__locals139.<>4__this.Ticks.ToArray(), barPrev, barCurr, CS$<>8__locals139.summary.BuyAvgOpen, CS$<>8__locals139.summary.SellAvgOpen);
                if ((barCurr != null) && (barPrev != null))
                {
                    positionBuy = Math.Round((double) (Math.Max((double) 0.0, (double) (price.Ask - barCurr.AverageAsk)) / (barCurr.AverageAsk - barPrev.AverageAsk)), 2);
                    positionSell = Math.Round((double) (Math.Max((double) 0.0, (double) (barCurr.AverageBid - price.Bid)) / (barPrev.AverageBid - barCurr.AverageBid)), 2);
                    goBuy = (CS$<>8__locals139.edgeMargin <= positionBuy) && (positionBuy <= (CS$<>8__locals139.edgeMargin * 2.0));
                    goSell = (CS$<>8__locals139.edgeMargin <= positionSell) && (positionSell <= (CS$<>8__locals139.edgeMargin * 2.0));
                    canSell = canBuy = true;
                    CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                    CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                    CS$<>8__locals139.<>4__this.CloseBuy = ((((CS$<>8__locals139.summary.BuyPositions + CS$<>8__locals139.summary.SellPositions) > 50.0) && (CS$<>8__locals139.summary.BuyNetPL > ((CS$<>8__locals139.profitMin * CS$<>8__locals139.summary.BuyLots) / 10000.0))) && goBuy) || CS$<>8__locals139.<>4__this.GoSell;
                    CS$<>8__locals139.<>4__this.CloseSell = ((((CS$<>8__locals139.summary.BuyPositions + CS$<>8__locals139.summary.SellPositions) > 50.0) && (CS$<>8__locals139.summary.SellNetPL > ((CS$<>8__locals139.profitMin * CS$<>8__locals139.summary.SellLots) / 10000.0))) && goSell) || CS$<>8__locals139.<>4__this.GoBuy;
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class1.<>4__this.lblOpenBuy, string.Format("{0:n0}", CS$<>8__locals13d.positionBuy * 100.0));
                        Lib.SetLabelText(class1.<>4__this.lblOpenSell, string.Format("{0:n0}", CS$<>8__locals13d.positionSell * 100.0));
                        class1.<>4__this.DC.BarHigh = Math.Round(barPrev.Average, class1.<>4__this.fw.Digits);
                        class1.<>4__this.DC.BarLow = Math.Round(barCurr.Average, class1.<>4__this.fw.Digits);
                        class1.<>4__this.DC.VoltageSpread = (int) Math.Round((double) ((barCurr.Average - barPrev.Average) / class1.<>4__this.fw.PointSize), 0);
                    }, new object[0]);
                }
            };
            Action decideByVoltage_3 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class1 = CS$<>8__locals139;
                Volt vBar1 = (from b in CS$<>8__locals139.<>4__this.voltages
                    orderby b.StartDate descending
                    select b).FirstOrDefault<Volt>();
                Volt vBar2 = (from b in CS$<>8__locals139.<>4__this.voltages
                    where Math.Abs((double) (b.Average - vBar1.Average)) >= class1.<>4__this.spreadAverage5Min
                    orderby b.StartDate descending
                    select b).FirstOrDefault<Volt>();
                IEnumerable<Volt> vBars = from b in new Volt[] { vBar1, vBar2 }
                    where b != null
                    select b;
                Volt barLow = (from b in vBars
                    orderby b.Average
                    select b).First<Volt>();
                Volt barHigh = (from b in vBars
                    orderby b.Average
                    select b).Last<Volt>();
                (from b in vBars
                    orderby b.Volts
                    select b).First<Volt>();
                (from b in vBars
                    orderby b.Volts
                    select b).Last<Volt>();
                if (barLow != null)
                {
                    CS$<>8__locals139.<>4__this.voltageAvgLow = new double?(Lib.CMA(CS$<>8__locals139.<>4__this.voltageAvgLow, 2.0, barLow.AverageBid));
                }
                if (barHigh != null)
                {
                    CS$<>8__locals139.<>4__this.voltageAvgHigh = new double?(Lib.CMA(CS$<>8__locals139.<>4__this.voltageAvgHigh, 2.0, barHigh.AverageAsk));
                }
                positionSell = Math.Round((double) ((price.Bid - CS$<>8__locals139.<>4__this.voltageAvgHigh.Value) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                positionBuy = Math.Round((double) ((CS$<>8__locals139.<>4__this.voltageAvgLow.Value - price.Ask) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                CS$<>8__locals139.<>4__this.DencityRatio = Math.Ceiling(spreadAverageInPips);
                if ((barHigh != barLow) && (barHigh != null))
                {
                    goBuy = positionBuy >= 0.0;
                    canBuy = (price.AskChangeDirection > 0) && positionBuy.Between(0.0, CS$<>8__locals139.<>4__this.DencityRatio);
                    CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                    goSell = positionSell >= 0.0;
                    canSell = (price.BidChangeDirection < 0) && positionSell.Between(0.0, CS$<>8__locals139.<>4__this.DencityRatio);
                    CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                    CS$<>8__locals139.<>4__this.DencityRatio = Math.Ceiling(spreadAverageInPips);
                    CS$<>8__locals139.<>4__this.LotsToTrade = Math.Max(1, (CS$<>8__locals139.<>4__this.GoBuy ? CS$<>8__locals139.summary.BuyPositions : (CS$<>8__locals139.<>4__this.GoSell ? CS$<>8__locals139.summary.SellPositions : 0.0)).ToInt());
                    CS$<>8__locals139.<>4__this.CloseSell = goBuy;
                    CS$<>8__locals139.<>4__this.CloseBuy = goSell;
                }
                CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                    class1.<>4__this.OnTicksChanged(class1.<>4__this.Ticks.ToArray(), class1.<>4__this.voltageAvgHigh.Value, class1.<>4__this.voltageAvgLow.Value, class1.summary.BuyAvgOpen, class1.summary.SellAvgOpen, (barHigh != null) ? barHigh.StartDate : DateTime.MinValue, (barLow != null) ? barLow.StartDate : DateTime.MinValue);
                    Lib.SetLabelText(class1.<>4__this.lblOpenBuy, string.Format("{0:n0}", CS$<>8__locals13d.positionBuy));
                    Lib.SetLabelText(class1.<>4__this.lblOpenSell, string.Format("{0:n0}", CS$<>8__locals13d.positionSell));
                    class1.<>4__this.DC.BarHigh = Math.Round(barHigh.Average, class1.<>4__this.fw.Digits);
                    class1.<>4__this.DC.BarLow = Math.Round(barLow.Average, class1.<>4__this.fw.Digits);
                    class1.<>4__this.DC.VoltageSpread = (int) Math.Round((double) ((barHigh.Average - barLow.Average) / class1.<>4__this.fw.PointSize), 0);
                }, new object[0]);
            };
            Action decideByVoltage_5 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class2 = CS$<>8__locals139;
                new Thread(delegate {
                    PriceBar bl = (from b in CS$<>8__locals13d.bsPeriods
                        orderby b.StartDate
                        select b).FirstOrDefault<PriceBar>();
                    if (bl != null)
                    {
                        class2.<>4__this.FindMaximas(bl.StartDate);
                    }
                }) { Priority = ThreadPriority.Lowest }.Start();
                if (CS$<>8__locals139.<>4__this.voltages.Count >= 2)
                {
                    Volt barLow = (from b in CS$<>8__locals139.<>4__this.voltages
                        orderby b.Average
                        select b).First<Volt>();
                    Volt barHigh = (from b in CS$<>8__locals139.<>4__this.voltages
                        orderby b.Average
                        select b).Last<Volt>();
                    Volt[] vBars = new Volt[] { barLow, barHigh };
                    Volt barLowVolts = (from b in vBars
                        orderby b.Volts
                        select b).First<Volt>();
                    Volt barHighVolts = (from b in vBars
                        orderby b.Volts
                        select b).Last<Volt>();
                    positionBuy = Math.Round((double) ((barLow.Average - price.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    positionSell = Math.Round((double) ((price.Average - barHigh.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    double priceAverageBuy = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= barLow.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    double priceAverageSell = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= barHigh.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    spreadTrue = CS$<>8__locals139.rateCurr.Spread > CS$<>8__locals139.ratePrev.Spread;
                    fastBuy = spreadTrue && ((CS$<>8__locals139.ratePrev.AskHigh - CS$<>8__locals139.rateCurr.AskLow) > CS$<>8__locals139.<>4__this.spreadAverage5Min);
                    fastSell = spreadTrue && ((CS$<>8__locals139.rateCurr.BidHigh - CS$<>8__locals139.ratePrev.BidLow) > CS$<>8__locals139.<>4__this.spreadAverage5Min);
                    if (fastBuy && (price.BidChangeDirection > 0))
                    {
                        CS$<>8__locals139.<>4__this.fw.CloseProfit(false, 1.0);
                    }
                    if (fastSell && (price.AskChangeDirection < 0))
                    {
                        CS$<>8__locals139.<>4__this.fw.CloseProfit(true, 1.0);
                    }
                    double corridorHeigth = Math.Max(CS$<>8__locals139.<>4__this.spreadAverage5MinInPips, Math.Min((double) ((from v in CS$<>8__locals139.<>4__this.voltagesCorridor
                        orderby v.Average
                        select v).Last<Volt>().Average - (from v in CS$<>8__locals139.<>4__this.voltagesCorridor
                        orderby v.Average
                        select v).First<Volt>().Average), (double) (barHigh.Average - barLow.Average)) / CS$<>8__locals139.<>4__this.fw.PointSize);
                    CS$<>8__locals139.<>4__this.DencityRatio = Math.Round(corridorHeigth, 0);
                    double priceAverage = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate > class2.serverTime.AddMinutes((double) (-class2.voltageTimeFrame * class2.periodMin))
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    CS$<>8__locals139.canAddTrade = (Math.Round(Math.Abs((double) (barHigh.Average - barLow.Average)), (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1)) >= Math.Round(CS$<>8__locals139.<>4__this.spreadAverage5Min, (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1))) && (ticksPerMinuteAverageShort > ticksPerMinuteAverageLong);
                    if (CS$<>8__locals139.canAddTrade)
                    {
                        Func<double, bool> canTrade = barAverage => Math.Abs((double) (priceAverage - barAverage)) <= (CS$<>8__locals13d.spreadAverage / 2.0);
                        goBuy = Math.Abs(positionBuy) <= (spreadAverageInPips / 2.0);
                        canBuy = canTrade(barLow.Average) && (Math.Abs((double) (priceAverageBuy - barLow.Average)) <= (spreadAverage / 2.0));
                        CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                        goSell = Math.Abs(positionSell) <= (spreadAverageInPips / 2.0);
                        canSell = canTrade(barHigh.Average) && (Math.Abs((double) (priceAverageSell - barHigh.Average)) < (spreadAverage / 2.0));
                        CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                        CS$<>8__locals139.<>4__this.CloseBuy = CS$<>8__locals139.<>4__this.closeOnReverseOnly ? CS$<>8__locals139.<>4__this.GoSell : goSell;
                        CS$<>8__locals139.<>4__this.CloseSell = CS$<>8__locals139.<>4__this.closeOnReverseOnly ? CS$<>8__locals139.<>4__this.GoBuy : goBuy;
                        CS$<>8__locals139.<>4__this.LotsToTrade = lotsToTradeFoos[CS$<>8__locals139.<>4__this.fooPosition](CS$<>8__locals139.<>4__this.GoBuy);
                    }
                    forceTradeAction(vBars);
                    CS$<>8__locals139.<>4__this.showCorridorScheduler.Command = delegate {
                        <>c__DisplayClass13c.<>c__DisplayClass15f CS$<>8__locals160 = (<>c__DisplayClass13c.<>c__DisplayClass15f) this;
                        <>c__DisplayClass13c classc1 = CS$<>8__locals13d;
                        <>c__DisplayClass138 class1 = class2;
                        Func<double, double, double, bool> showMe = (high, low, value) => value.Between(low, high);
                        if (!class2.<>4__this.showOtherCorridors)
                        {
                            new List<LineAndTime>();
                        }
                        else
                        {
                            (from v in (from v in class2.<>4__this.voltages
                                where showMe(CS$<>8__locals160.barHigh.Average - class1.<>4__this.fw.PointSize, CS$<>8__locals160.barLow.Average + class1.<>4__this.fw.PointSize, v.Average)
                                orderby v.Volts descending
                                select v).Take<Volt>(3) select new LineAndTime(v.Average, v.StartDate)).ToList<LineAndTime>();
                        }
                        DateTime timeMin = (from t in class2.<>4__this.Ticks
                            where t.StartDate > class1.serverTime.Round().AddMinutes((double) ((-class1.<>4__this.TimeFrame * class1.periodMin) - 5))
                            select t).Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
                        DateTime timeMax = class2.<>4__this.Ticks.Max<FXCoreWrapper.Tick, DateTime>(t => t.StartDate).Round();
                        IEnumerable<FXCoreWrapper.Tick> ticks = from t in class2.<>4__this.Ticks
                            where t.StartDate < timeMax
                            select t;
                        FXCoreWrapper.Tick[] tickLast = new FXCoreWrapper.Tick[] { new FXCoreWrapper.Tick { Ask = CS$<>8__locals13d.price.Ask, Bid = CS$<>8__locals13d.price.Bid, StartDate = CS$<>8__locals13d.price.Time } };
                        double priceAverageMax = (barHigh == barHighVolts) ? priceAverageSell : priceAverageBuy;
                        double priceAverageMin = (barHigh != barHighVolts) ? priceAverageSell : priceAverageBuy;
                        class2.<>4__this.OnTicksChanged((from t in ticks
                            where t.StartDate >= timeMin
                            select t).Union<FXCoreWrapper.Tick>(tickLast).ToArray<FXCoreWrapper.Tick>(), barHighVolts.Average, barLowVolts.Average, priceAverageMax, priceAverageMin, class2.summary.BuyAvgOpen, class2.summary.SellAvgOpen, barHighVolts.StartDate, barLowVolts.StartDate, new double[] { priceAverage, priceAverage }, class2.<>4__this.VoltagesByTick);
                    };
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class2.<>4__this.lblOpenBuy, string.Format("{0:n1}", CS$<>8__locals13d.positionBuy));
                        Lib.SetLabelText(class2.<>4__this.lblOpenSell, string.Format("{0:n1}", CS$<>8__locals13d.positionSell));
                        class2.<>4__this.DC.BarHigh = Math.Round(barHigh.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.BarLow = Math.Round(barLow.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.VoltageSpread = Math.Round((double) ((barLow.Average - barHigh.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageHigh = Math.Round((double) ((barHigh.Average - priceAverage) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageLow = Math.Round((double) ((barLow.Average - priceAverage) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageSell = Math.Round((double) ((barHigh.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageBuy = Math.Round((double) ((priceAverageBuy - barLow.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.VoltageCorridor = Math.Round(corridorHeigth, 1);
                        class2.<>4__this.DC.TicksPerMinuteAverageLong = CS$<>8__locals13d.ticksPerMinuteAverageLong;
                        class2.<>4__this.DC.TicksPerMinuteAverageShort = CS$<>8__locals13d.ticksPerMinuteAverageShort;
                    }, new object[0]);
                }
            };
            Action decideByVoltage_6 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class2 = CS$<>8__locals139;
                if (!CS$<>8__locals139.<>4__this.getVoltagesScheduler.IsRunning)
                {
                    CS$<>8__locals139.<>4__this.getVoltagesScheduler.Command = delegate {
                        CS$<>8__locals139.<>4__this.FindMaximasPeakAndValley();
                    };
                }
                if ((CS$<>8__locals139.<>4__this.PeakVolt != null) && (CS$<>8__locals139.<>4__this.ValleyVolt != null))
                {
                    Volt[] vBars = new Volt[] { CS$<>8__locals139.<>4__this.PeakVolt, CS$<>8__locals139.<>4__this.ValleyVolt };
                    (from b in vBars
                        orderby b.Volts
                        select b).First<Volt>();
                    (from b in vBars
                        orderby b.Volts
                        select b).Last<Volt>();
                    positionBuy = Math.Round((double) ((CS$<>8__locals139.<>4__this.ValleyVolt.Average - price.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    positionSell = Math.Round((double) ((price.Average - CS$<>8__locals139.<>4__this.PeakVolt.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    double priceAverageBuy = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.ValleyVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    double priceAverageSell = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.PeakVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    spreadTrue = true;
                    fastBuy = spreadTrue && ((CS$<>8__locals139.ratePrev.AskHigh - CS$<>8__locals139.rateCurr.AskLow) > CS$<>8__locals139.<>4__this.spreadAverage5Min);
                    fastSell = spreadTrue && ((CS$<>8__locals139.rateCurr.BidHigh - CS$<>8__locals139.ratePrev.BidLow) > CS$<>8__locals139.<>4__this.spreadAverage5Min);
                    if ((CS$<>8__locals139.<>4__this.fastClose && fastBuy) && (price.BidChangeDirection > 0))
                    {
                        double profitInPips = CS$<>8__locals139.profitMin;
                        if (CS$<>8__locals139.summary.SellNetPL > 0.0)
                        {
                            profitInPips = -1000.0;
                            CS$<>8__locals139.<>4__this.fw.CloseProfit(false, profitInPips);
                        }
                    }
                    if ((CS$<>8__locals139.<>4__this.fastClose && fastSell) && (price.AskChangeDirection < 0))
                    {
                        double profitInPips = CS$<>8__locals139.profitMin;
                        if (CS$<>8__locals139.summary.BuyNetPL > 0.0)
                        {
                            profitInPips = -1000.0;
                            CS$<>8__locals139.<>4__this.fw.CloseProfit(true, profitInPips);
                        }
                    }
                    CS$<>8__locals139.canAddTrade = Math.Round((double) (CS$<>8__locals139.<>4__this.PeakVolt.Average - CS$<>8__locals139.<>4__this.ValleyVolt.Average), (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1)) >= Math.Round(CS$<>8__locals139.<>4__this.spreadAverage5Min, (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1));
                    if (CS$<>8__locals139.canAddTrade)
                    {
                        Func<double, bool> goTrade = delegate (double position) {
                            if (!(class2.<>4__this.tradeHighLow || ((class2.summary.BuyPositions + class2.summary.SellPositions) == 0.0)))
                            {
                                return position.Between(0.0, CS$<>8__locals13d.spreadAverageInPips / 2.0);
                            }
                            return position.Between(CS$<>8__locals13d.spreadAverageInPips / 2.0, CS$<>8__locals13d.spreadAverageInPips);
                        };
                        Func<double, double, bool> canTrade = (priceAverage, barAverage) => Math.Abs((double) (priceAverage - barAverage)) <= (CS$<>8__locals13d.spreadAverage / 2.0);
                        goBuy = positionBuy > 0.0;
                        canBuy = !goTrade(positionBuy) ? false : canTrade(priceAverageBuy, CS$<>8__locals139.<>4__this.ValleyVolt.Average);
                        CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                        goSell = positionSell > 0.0;
                        canSell = !goTrade(positionSell) ? false : canTrade(priceAverageSell, CS$<>8__locals139.<>4__this.PeakVolt.Average);
                        CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                        if (CS$<>8__locals139.<>4__this.GoSell && (!CS$<>8__locals139.<>4__this.closeOnProfitOnly || (CS$<>8__locals139.summary.BuyNetPLPip >= CS$<>8__locals139.profitMin)))
                        {
                            CS$<>8__locals139.<>4__this.CloseBuy = true;
                        }
                        if ((goSell && !CS$<>8__locals139.<>4__this.closeOnReverseOnly) && (CS$<>8__locals139.summary.BuyNetPLPip >= CS$<>8__locals139.profitMin))
                        {
                            CS$<>8__locals139.<>4__this.CloseBuy = true;
                        }
                        if (CS$<>8__locals139.<>4__this.GoBuy && (!CS$<>8__locals139.<>4__this.closeOnProfitOnly || (CS$<>8__locals139.summary.SellNetPLPip >= CS$<>8__locals139.profitMin)))
                        {
                            CS$<>8__locals139.<>4__this.CloseSell = true;
                        }
                        if ((goBuy && !CS$<>8__locals139.<>4__this.closeOnReverseOnly) && (CS$<>8__locals139.summary.SellNetPLPip >= CS$<>8__locals139.profitMin))
                        {
                            CS$<>8__locals139.<>4__this.CloseSell = true;
                        }
                        CS$<>8__locals139.<>4__this.LotsToTrade = lotsToTradeFoos[CS$<>8__locals139.<>4__this.fooPosition](CS$<>8__locals139.<>4__this.GoBuy);
                    }
                    double corridorHeigth = Math.Max(Math.Max(CS$<>8__locals139.<>4__this.PeakVolt.AverageAsk - CS$<>8__locals139.<>4__this.ValleyVolt.AverageBid, CS$<>8__locals139.<>4__this.fw.GetMaxDistance(CS$<>8__locals139.<>4__this.GoBuy)) / CS$<>8__locals139.<>4__this.fw.PointSize, CS$<>8__locals139.<>4__this.spreadAverage5MinInPips);
                    CS$<>8__locals139.<>4__this.DencityRatio = Math.Round(corridorHeigth, 0);
                    forceTradeAction(vBars);
                    CS$<>8__locals139.<>4__this.showCorridorScheduler.Command = delegate {
                        <>c__DisplayClass13c classc1 = CS$<>8__locals13d;
                        <>c__DisplayClass138 class1 = class2;
                        DateTime timeMin = (from t in class2.<>4__this.Ticks
                            where t.StartDate > class1.serverTime.Round().AddMinutes((double) ((-class1.<>4__this.TimeFrame * class1.periodMin) - 5))
                            select t).Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
                        DateTime timeMax = class2.<>4__this.Ticks.Max<FXCoreWrapper.Tick, DateTime>(t => t.StartDate).Round();
                        IEnumerable<FXCoreWrapper.Tick> ticks = from t in class2.<>4__this.Ticks
                            where t.StartDate < timeMax
                            select t;
                        FXCoreWrapper.Tick[] tickLast = new FXCoreWrapper.Tick[] { new FXCoreWrapper.Tick { Ask = CS$<>8__locals13d.price.Ask, Bid = CS$<>8__locals13d.price.Bid, StartDate = CS$<>8__locals13d.price.Time } };
                        double average = class2.<>4__this.PeakVolt.Average;
                        double num2 = class2.<>4__this.ValleyVolt.Average;
                        class2.<>4__this.OnTicksChanged((from t in ticks
                            where t.StartDate >= timeMin
                            select t).Union<FXCoreWrapper.Tick>(tickLast).ToArray<FXCoreWrapper.Tick>(), class2.<>4__this.PeakVolt.Average, class2.<>4__this.ValleyVolt.Average, priceAverageSell, priceAverageBuy, class2.summary.BuyAvgOpen, class2.summary.SellAvgOpen, class2.<>4__this.PeakVolt.StartDate, class2.<>4__this.ValleyVolt.StartDate, new double[] { priceAverageBuy, priceAverageSell }, class2.<>4__this.VoltagesByTick);
                    };
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class2.<>4__this.lblOpenBuy, string.Format("{0:n1}", CS$<>8__locals13d.positionBuy));
                        Lib.SetLabelText(class2.<>4__this.lblOpenSell, string.Format("{0:n1}", CS$<>8__locals13d.positionSell));
                        class2.<>4__this.DC.BarHigh = Math.Round(class2.<>4__this.PeakVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.BarLow = Math.Round(class2.<>4__this.ValleyVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.VoltageSpread = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageHigh = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageLow = Math.Round((double) ((class2.<>4__this.ValleyVolt.Average - priceAverageBuy) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageSell = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageBuy = Math.Round((double) ((priceAverageBuy - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.VoltageCorridor = Math.Round(corridorHeigth, 1);
                        class2.<>4__this.DC.TicksPerMinuteAverageLong = CS$<>8__locals13d.ticksPerMinuteAverageLong;
                        class2.<>4__this.DC.TicksPerMinuteAverageShort = CS$<>8__locals13d.ticksPerMinuteAverageShort;
                    }, new object[0]);
                }
            };
            Action decideByVoltage_7 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class2 = CS$<>8__locals139;
                if (!CS$<>8__locals139.<>4__this.getVoltagesScheduler.IsRunning)
                {
                    CS$<>8__locals139.<>4__this.getVoltagesScheduler.Command = delegate {
                        CS$<>8__locals139.<>4__this.FindMaximasPeakAndValley();
                    };
                }
                if ((CS$<>8__locals139.<>4__this.PeakVolt != null) && (CS$<>8__locals139.<>4__this.ValleyVolt != null))
                {
                    Volt[] vBars = new Volt[] { CS$<>8__locals139.<>4__this.PeakVolt, CS$<>8__locals139.<>4__this.ValleyVolt };
                    (from b in vBars
                        orderby b.Volts
                        select b).First<Volt>();
                    (from b in vBars
                        orderby b.Volts
                        select b).Last<Volt>();
                    positionBuy = Math.Round((double) ((price.Average - CS$<>8__locals139.<>4__this.PeakVolt.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    positionSell = Math.Round((double) ((CS$<>8__locals139.<>4__this.ValleyVolt.Average - price.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    double priceAverageBuy = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.ValleyVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    double priceAverageSell = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.PeakVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    CS$<>8__locals139.canAddTrade = CS$<>8__locals139.<>4__this.PeakVolt.AverageBid > CS$<>8__locals139.<>4__this.ValleyVolt.AverageAsk;
                    if (CS$<>8__locals139.canAddTrade)
                    {
                        goBuy = positionBuy.Between(spreadAverageInPips / 2.0, spreadAverageInPips);
                        canBuy = goBuy && (CS$<>8__locals139.summary.BuyNetPL <= 0.0);
                        CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                        goSell = positionSell.Between(spreadAverageInPips / 2.0, spreadAverageInPips);
                        canSell = goSell && (CS$<>8__locals139.summary.SellNetPL <= 0.0);
                        CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                        CS$<>8__locals139.<>4__this.LotsToTrade = lotsToTradeFoos[CS$<>8__locals139.<>4__this.fooPosition](CS$<>8__locals139.<>4__this.GoBuy);
                    }
                    double corridorHeigth = Math.Max(Math.Max(CS$<>8__locals139.<>4__this.PeakVolt.AverageAsk - CS$<>8__locals139.<>4__this.ValleyVolt.AverageBid, CS$<>8__locals139.<>4__this.fw.GetMaxDistance(CS$<>8__locals139.<>4__this.GoBuy)) / CS$<>8__locals139.<>4__this.fw.PointSize, CS$<>8__locals139.<>4__this.spreadAverage5MinInPips);
                    CS$<>8__locals139.<>4__this.DencityRatio = Math.Round(corridorHeigth, 0);
                    CS$<>8__locals139.<>4__this.CloseBuy = (positionBuy < (spreadAverageInPips / 2.0)) && (CS$<>8__locals139.summary.BuyNetPL > 0.0);
                    CS$<>8__locals139.<>4__this.CloseSell = (positionSell < (spreadAverageInPips / 2.0)) && (CS$<>8__locals139.summary.SellNetPL > 0.0);
                    forceTradeAction(vBars);
                    CS$<>8__locals139.<>4__this.showCorridorScheduler.Command = delegate {
                        <>c__DisplayClass13c classc1 = CS$<>8__locals13d;
                        <>c__DisplayClass138 class1 = class2;
                        DateTime timeMin = (from t in class2.<>4__this.Ticks
                            where t.StartDate > class1.serverTime.Round().AddMinutes((double) ((-class1.<>4__this.TimeFrame * class1.periodMin) - 5))
                            select t).Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
                        DateTime timeMax = class2.<>4__this.Ticks.Max<FXCoreWrapper.Tick, DateTime>(t => t.StartDate).Round();
                        IEnumerable<FXCoreWrapper.Tick> ticks = from t in class2.<>4__this.Ticks
                            where t.StartDate < timeMax
                            select t;
                        FXCoreWrapper.Tick[] tickLast = new FXCoreWrapper.Tick[] { new FXCoreWrapper.Tick { Ask = CS$<>8__locals13d.price.Ask, Bid = CS$<>8__locals13d.price.Bid, StartDate = CS$<>8__locals13d.price.Time } };
                        double average = class2.<>4__this.PeakVolt.Average;
                        double num2 = class2.<>4__this.ValleyVolt.Average;
                        class2.<>4__this.OnTicksChanged((from t in ticks
                            where t.StartDate >= timeMin
                            select t).Union<FXCoreWrapper.Tick>(tickLast).ToArray<FXCoreWrapper.Tick>(), class2.<>4__this.PeakVolt.Average, class2.<>4__this.ValleyVolt.Average, priceAverageSell, priceAverageBuy, class2.summary.BuyAvgOpen, class2.summary.SellAvgOpen, class2.<>4__this.PeakVolt.StartDate, class2.<>4__this.ValleyVolt.StartDate, new double[] { priceAverageBuy, priceAverageSell }, class2.<>4__this.VoltagesByTick);
                    };
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class2.<>4__this.lblOpenBuy, string.Format("{0:n1}", CS$<>8__locals13d.positionBuy));
                        Lib.SetLabelText(class2.<>4__this.lblOpenSell, string.Format("{0:n1}", CS$<>8__locals13d.positionSell));
                        class2.<>4__this.DC.BarHigh = Math.Round(class2.<>4__this.PeakVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.BarLow = Math.Round(class2.<>4__this.ValleyVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.VoltageSpread = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageHigh = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageLow = Math.Round((double) ((class2.<>4__this.ValleyVolt.Average - priceAverageBuy) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageSell = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageBuy = Math.Round((double) ((priceAverageBuy - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.VoltageCorridor = Math.Round(corridorHeigth, 1);
                        class2.<>4__this.DC.TicksPerMinuteAverageLong = CS$<>8__locals13d.ticksPerMinuteAverageLong;
                        class2.<>4__this.DC.TicksPerMinuteAverageShort = CS$<>8__locals13d.ticksPerMinuteAverageShort;
                    }, new object[0]);
                }
            };
            Action decideByVoltage_8 = delegate {
                <>c__DisplayClass13c CS$<>8__locals13d = (<>c__DisplayClass13c) this;
                <>c__DisplayClass138 class2 = CS$<>8__locals139;
                if (!CS$<>8__locals139.<>4__this.getVoltagesScheduler.IsRunning)
                {
                    CS$<>8__locals139.<>4__this.getVoltagesScheduler.Command = delegate {
                        CS$<>8__locals139.<>4__this.spreadAverage5Min = CS$<>8__locals139.<>4__this.fw.GetMinuteTicks(CS$<>8__locals139.<>4__this.TicksInTimeFrame.ToArray<FXCoreWrapper.Tick>(), 5).Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => r.Spread));
                        CS$<>8__locals139.<>4__this.spreadAverage5MinInPips = CS$<>8__locals139.<>4__this.spreadAverage5Min / CS$<>8__locals139.<>4__this.fw.PointSize;
                        CS$<>8__locals139.<>4__this.spreadAverage10Min = CS$<>8__locals139.<>4__this.fw.GetMinuteTicks(CS$<>8__locals139.<>4__this.TicksInTimeFrame.ToArray<FXCoreWrapper.Tick>(), 10).Average<FXCoreWrapper.Rate>((Func<FXCoreWrapper.Rate, double>) (r => r.Spread));
                        CS$<>8__locals139.<>4__this.spreadAverage10MinInPips = CS$<>8__locals139.<>4__this.spreadAverage10Min / CS$<>8__locals139.<>4__this.fw.PointSize;
                        CS$<>8__locals139.<>4__this.FindMaximasPeakAndValley();
                    };
                }
                if ((CS$<>8__locals139.<>4__this.PeakVolt != null) && (CS$<>8__locals139.<>4__this.ValleyVolt != null))
                {
                    Volt[] vBars = new Volt[] { CS$<>8__locals139.<>4__this.PeakVolt, CS$<>8__locals139.<>4__this.ValleyVolt };
                    (from b in vBars
                        orderby b.Volts
                        select b).First<Volt>();
                    (from b in vBars
                        orderby b.Volts
                        select b).Last<Volt>();
                    double priceAverageBuy = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.ValleyVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    double priceAverageSell = (from t in CS$<>8__locals139.<>4__this.Ticks
                        where t.StartDate >= CS$<>8__locals139.<>4__this.PeakVolt.StartDate
                        select t).Average<FXCoreWrapper.Tick>((Func<FXCoreWrapper.Tick, double>) (t => t.PriceAvg));
                    positionBuy = Math.Round((double) ((CS$<>8__locals139.<>4__this.ValleyVolt.Average - price.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    positionSell = Math.Round((double) ((price.Average - CS$<>8__locals139.<>4__this.PeakVolt.Average) / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                    CS$<>8__locals139.canAddTrade = (CS$<>8__locals139.<>4__this.forceCloseTrade || CS$<>8__locals139.<>4__this.tradeHighLow) ? (CS$<>8__locals139.<>4__this.PeakVolt.AverageBid > CS$<>8__locals139.<>4__this.ValleyVolt.AverageAsk) : (Math.Round((double) (CS$<>8__locals139.<>4__this.PeakVolt.Average - CS$<>8__locals139.<>4__this.ValleyVolt.Average), (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1)) >= Math.Round(CS$<>8__locals139.<>4__this.spreadAverage5Min, (int) (CS$<>8__locals139.<>4__this.fw.Digits - 1)));
                    if (CS$<>8__locals139.canAddTrade)
                    {
                        Func<double, bool> goTrade = delegate (double position) {
                            if (!(class2.<>4__this.tradeHighLow || ((class2.summary.BuyPositions + class2.summary.SellPositions) == 0.0)))
                            {
                                return position.Between(0.0, CS$<>8__locals13d.spreadAverageInPips / 2.0);
                            }
                            return position.Between(CS$<>8__locals13d.spreadAverageInPips / 2.0, CS$<>8__locals13d.spreadAverageInPips);
                        };
                        goBuy = positionBuy > 0.0;
                        canBuy = goTrade(positionBuy);
                        CS$<>8__locals139.<>4__this.GoBuy = canBuy && goBuy;
                        goSell = positionSell > 0.0;
                        canSell = goTrade(positionSell);
                        CS$<>8__locals139.<>4__this.GoSell = canSell && goSell;
                        if (CS$<>8__locals139.<>4__this.GoSell && (!CS$<>8__locals139.<>4__this.closeOnProfitOnly || (CS$<>8__locals139.summary.BuyNetPLPip >= CS$<>8__locals139.profitMin)))
                        {
                            CS$<>8__locals139.<>4__this.CloseBuy = true;
                        }
                        if ((goSell && !CS$<>8__locals139.<>4__this.closeOnReverseOnly) && (CS$<>8__locals139.summary.BuyNetPLPip >= CS$<>8__locals139.profitMin))
                        {
                            CS$<>8__locals139.<>4__this.CloseBuy = true;
                        }
                        if (CS$<>8__locals139.<>4__this.GoBuy && (!CS$<>8__locals139.<>4__this.closeOnProfitOnly || (CS$<>8__locals139.summary.SellNetPLPip >= CS$<>8__locals139.profitMin)))
                        {
                            CS$<>8__locals139.<>4__this.CloseSell = true;
                        }
                        if ((goBuy && !CS$<>8__locals139.<>4__this.closeOnReverseOnly) && (CS$<>8__locals139.summary.SellNetPLPip >= CS$<>8__locals139.profitMin))
                        {
                            CS$<>8__locals139.<>4__this.CloseSell = true;
                        }
                        CS$<>8__locals139.<>4__this.LotsToTrade = lotsToTradeFoos[CS$<>8__locals139.<>4__this.fooPosition](CS$<>8__locals139.<>4__this.GoBuy);
                    }
                    double corridorHeigth = Math.Max(Math.Max(CS$<>8__locals139.<>4__this.PeakVolt.AverageAsk - CS$<>8__locals139.<>4__this.ValleyVolt.AverageBid, CS$<>8__locals139.<>4__this.fw.GetMaxDistance(CS$<>8__locals139.<>4__this.GoBuy)) / CS$<>8__locals139.<>4__this.fw.PointSize, CS$<>8__locals139.<>4__this.spreadAverage5MinInPips);
                    CS$<>8__locals139.<>4__this.DencityRatio = Math.Round(corridorHeigth, 0);
                    forceTradeAction(vBars);
                    CS$<>8__locals139.<>4__this.showCorridorScheduler.Command = delegate {
                        <>c__DisplayClass13c classc1 = CS$<>8__locals13d;
                        <>c__DisplayClass138 class1 = class2;
                        DateTime timeMin = (from t in class2.<>4__this.Ticks
                            where t.StartDate > class1.serverTime.Round().AddMinutes((double) ((-class1.<>4__this.TimeFrame * class1.periodMin) - 5))
                            select t).Min<FXCoreWrapper.Tick, DateTime>(t => t.StartDate);
                        DateTime timeMax = class2.<>4__this.Ticks.Max<FXCoreWrapper.Tick, DateTime>(t => t.StartDate).Round();
                        IEnumerable<FXCoreWrapper.Tick> ticks = from t in class2.<>4__this.Ticks
                            where t.StartDate < timeMax
                            select t;
                        FXCoreWrapper.Tick[] tickLast = new FXCoreWrapper.Tick[] { new FXCoreWrapper.Tick { Ask = CS$<>8__locals13d.price.Ask, Bid = CS$<>8__locals13d.price.Bid, StartDate = CS$<>8__locals13d.price.Time } };
                        double average = class2.<>4__this.PeakVolt.Average;
                        double num2 = class2.<>4__this.ValleyVolt.Average;
                        class2.<>4__this.OnTicksChanged((from t in ticks
                            where t.StartDate >= timeMin
                            select t).Union<FXCoreWrapper.Tick>(tickLast).ToArray<FXCoreWrapper.Tick>(), class2.<>4__this.PeakVolt.Average, class2.<>4__this.ValleyVolt.Average, priceAverageSell, priceAverageBuy, class2.summary.BuyAvgOpen, class2.summary.SellAvgOpen, class2.<>4__this.PeakVolt.StartDate, class2.<>4__this.ValleyVolt.StartDate, new double[] { priceAverageBuy, priceAverageSell }, class2.<>4__this.VoltagesByTick);
                    };
                    CS$<>8__locals139.<>4__this.Dispatcher.BeginInvoke(delegate {
                        Lib.SetLabelText(class2.<>4__this.lblVolatility, string.Format("{0:n1}/{1:n1}/{2:n1}={3:n1}/{4:n1}>", new object[] { class2.<>4__this.spreadAverage10MinInPips, class2.<>4__this.spreadAverage5MinInPips, CS$<>8__locals13d.spreadAverageInPips, class2.<>4__this.spreadAverage10MinInPips / class2.<>4__this.spreadAverage5MinInPips, class2.<>4__this.spreadAverage5MinInPips / CS$<>8__locals13d.spreadAverageInPips }));
                        Lib.SetLabelText(class2.<>4__this.lblOpenBuy, string.Format("{0:n1}", CS$<>8__locals13d.positionBuy));
                        Lib.SetLabelText(class2.<>4__this.lblOpenSell, string.Format("{0:n1}", CS$<>8__locals13d.positionSell));
                        class2.<>4__this.DC.BarHigh = Math.Round(class2.<>4__this.PeakVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.BarLow = Math.Round(class2.<>4__this.ValleyVolt.Average, class2.<>4__this.fw.Digits);
                        class2.<>4__this.DC.VoltageSpread = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageHigh = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageLow = Math.Round((double) ((class2.<>4__this.ValleyVolt.Average - priceAverageBuy) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageSell = Math.Round((double) ((class2.<>4__this.PeakVolt.Average - priceAverageSell) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.AverageBuy = Math.Round((double) ((priceAverageBuy - class2.<>4__this.ValleyVolt.Average) / class2.<>4__this.fw.PointSize), 1);
                        class2.<>4__this.DC.VoltageCorridor = Math.Round(corridorHeigth, 1);
                        class2.<>4__this.DC.TicksPerMinuteAverageLong = CS$<>8__locals13d.ticksPerMinuteAverageLong;
                        class2.<>4__this.DC.TicksPerMinuteAverageShort = CS$<>8__locals13d.ticksPerMinuteAverageShort;
                    }, new object[0]);
                }
            };
            int[] rowsLowHigh = new int[] { 2, 3, 4, 5 };
            FXCoreWrapper.Rate barLowF = (from b in this.Rates.Take<FXCoreWrapper.Rate>(((int) barsBest.Row) + 3)
                where Math.Round(b.AskLow, CS$<>8__locals139.<>4__this.fw.Digits) == barsBest.AskLow
                orderby b.Row
                select b).LastOrDefault<FXCoreWrapper.Rate>();
            FXCoreWrapper.Rate barHighF = (from b in this.Rates.Take<FXCoreWrapper.Rate>(((int) barsBest.Row) + 3)
                where Math.Round(b.BidHigh, CS$<>8__locals139.<>4__this.fw.Digits) == barsBest.BidHigh
                orderby b.Row
                select b).LastOrDefault<FXCoreWrapper.Rate>();
            int rowLow = (int) Math.Floor(barLowF.Row);
            int rowHight = (int) Math.Floor(barHighF.Row);
            rowsLowHigh.Contains<int>(rowLow);
            rowsLowHigh.Contains<int>(rowHight);
            if (account.PipsToMC > this.spreadAverage5MinInPips)
            {
                switch (this.fooNumber)
                {
                    case 1:
                        decideByVoltage_1();
                        goto Label_0B56;

                    case 2:
                        decideByVoltage_2();
                        goto Label_0B56;

                    case 3:
                        decideByVoltage_3();
                        goto Label_0B56;

                    case 5:
                        decideByVoltage_5();
                        goto Label_0B56;

                    case 6:
                        decideByVoltage_6();
                        goto Label_0B56;

                    case 7:
                        decideByVoltage_7();
                        goto Label_0B56;

                    case 8:
                        decideByVoltage_8();
                        goto Label_0B56;
                }
                throw new Exception("UnKNown Foo Number:" + this.fooNumber);
            }
            if (CS$<>9__CachedAnonymousMethodDelegate12e == null)
            {
                CS$<>9__CachedAnonymousMethodDelegate12e = t => t.Lots;
            }
            Trade firstTrade = this.fw.GetTrades().OrderBy<Trade, long>(CS$<>9__CachedAnonymousMethodDelegate12e).FirstOrDefault<Trade>();
            if (firstTrade != null)
            {
                this.fw.FixOrder_Close(firstTrade.ID);
                Thread.Sleep(0x1388);
            }
        Label_0B56:
            if (this.forceCloseTrade)
            {
                if (summary.BuyNetPLPip >= profitMin)
                {
                    this.fw.CloseProfit(true, -1000.0);
                }
                if (summary.SellNetPLPip >= profitMin)
                {
                    this.fw.CloseProfit(false, -1000.0);
                }
            }
            if (account.Hedging)
            {
                int profitPosCountMin = 2;
                if (summary.BuyPositions >= this.sellOnProfit)
                {
                    IOrderedEnumerable<Trade> profitTrades = from t in this.fw.GetTradesToClose(true, profitMin)
                        orderby t.GrossPL
                        select t;
                    if (profitTrades.Count<Trade>() >= profitPosCountMin)
                    {
                        this.fw.FixOrder_Close((from t in profitTrades
                            orderby t.GrossPL descending
                            select t).First<Trade>().ID);
                    }
                }
                if (summary.SellPositions >= this.sellOnProfit)
                {
                    IOrderedEnumerable<Trade> profitTrades = from t in this.fw.GetTradesToClose(false, profitMin)
                        orderby t.GrossPL
                        select t;
                    if (profitTrades.Count<Trade>() >= profitPosCountMin)
                    {
                        this.fw.FixOrder_Close((from t in profitTrades
                            orderby t.GrossPL descending
                            select t).First<Trade>().ID);
                    }
                }
            }
            int waveWeight = this.doWaveWeight ? ((int) Math.Round((double) (barsBest.Spread / spreadAverageInPips), 0)) : 0;
            int speedWieght = this.doSpeedWeight ? ((int) Math.Round(speedMax, 0)) : 0;
            if (!account.Hedging && (summary.BuyPositions > 0.0))
            {
                this.GoSell = false;
            }
            if (!account.Hedging- && (summary.SellPositions > 0.0))
            {
                this.GoBuy = false;
            }
            Lib.SetLabelText(this.lblPower, string.Format("{0:n1}", bsPeriods.Average<PriceBar>((Func<PriceBar, double>) (b => b.Volts))));
            Lib.SetLabelText(this.lblUpDown, string.Format("{0:n1}/{1:n1}={2:n1}>", spreadAverageInPips, this.spreadAverage300InPips, upDown));
            Lib.SetBackGround(this.lblOpenSell, new SolidColorBrush(this.GoSell ? Colors.PaleGreen : (this.CloseSell ? Colors.LightSalmon : (goSell ? Colors.Yellow : Colors.Transparent))));
            Lib.SetBackGround(this.lblOpenBuy, new SolidColorBrush(this.GoBuy ? Colors.PaleGreen : (this.CloseBuy ? Colors.LightSalmon : (goBuy ? Colors.Yellow : Colors.Transparent))));
            Lib.SetLabelText(this.lblServerTime, string.Format("{0:HH:mm:ss}/{1:n0}]", serverTime, timeSpanLast));
            if ((canAddTrade && this.CanTrade) || (canAddTrade && ((summary.BuyPositions + summary.SellPositions) > 0.0)))
            {
                Lib.SetBackGround(this.wpMain, new SolidColorBrush(Colors.Transparent));
            }
            else
            {
                Lib.SetBackGround(this.wpMain, new SolidColorBrush((this.CanTrade && canAddTrade) ? Colors.Transparent : Colors.BlanchedAlmond));
            }
            base.Dispatcher.BeginInvoke(delegate {
                CS$<>8__locals139.<>4__this.DC.RowLow = (price.Bid - barsBest.AverageAsk) / CS$<>8__locals139.<>4__this.fw.PointSize;
                CS$<>8__locals139.<>4__this.DC.RowHigh = (barsBest.AverageBid - price.Ask) / CS$<>8__locals139.<>4__this.fw.PointSize;
                CS$<>8__locals139.<>4__this.DC.RateCurrSpread = Math.Round((double) (CS$<>8__locals139.rateCurr.Spread / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                CS$<>8__locals139.<>4__this.DC.RatePrevSpread = Math.Round((double) (CS$<>8__locals139.ratePrev.Spread / CS$<>8__locals139.<>4__this.fw.PointSize), 1);
                CS$<>8__locals139.<>4__this.DC.SpeedBest = Math.Round(barsBest.Speed, 1);
                CS$<>8__locals139.<>4__this.DC.SpreadAverage = Math.Round(Math.Max(spreadAverageInPips, CS$<>8__locals139.<>4__this.spreadAverage300InPips), 1);
                CS$<>8__locals139.<>4__this.DC.SpeedWeight = speedWieght;
                CS$<>8__locals139.<>4__this.DC.WaveWeight = waveWeight;
                CS$<>8__locals139.<>4__this.DC.BestBarRow = Math.Round(barsBest.Row, 1);
                CS$<>8__locals139.<>4__this.DC.BuySell = CS$<>8__locals139.<>4__this.GoBuy ? ((bool?) true) : (CS$<>8__locals139.<>4__this.GoSell ? ((bool?) false) : null);
            }, new object[0]);
            if (this.PriceGridChanged != null)
            {
                this.PriceGridChanged();
            }
        }
        catch (ThreadAbortException)
        {
        }
        catch (Exception exc)
        {
            if (this.PriceGridError != null)
            {
                this.PriceGridError(exc);
            }
        }
    }

    public void ResetBars()
    {
        base.Dispatcher.Invoke(delegate {
            this.Rates = null;
            this.TimeFrameTime = DateTime.MinValue;
        }, new object[0]);
    }

    private void ShowSettings(object sender, MouseButtonEventArgs e)
    {
        this.popUpSettings.Placement = PlacementMode.Bottom;
        this.popUpSettings.PlacementTarget = sender as UIElement;
        this.popUpSettings.IsOpen = !this.popUpSettings.IsOpen;
    }

    [EditorBrowsable(EditorBrowsableState.Never), DebuggerNonUserCode]
    void IComponentConnector.Connect(int connectionId, object target)
    {
        switch (connectionId)
        {
            case 1:
                ((Charting) target).Initialized += new EventHandler(this.Window_Initialized);
                ((Charting) target).Closing += new CancelEventHandler(this.Window_Closing);
                return;

            case 2:
                this.wpMain = (StackPanel) target;
                return;

            case 3:
                this.popUpSettings = (Popup) target;
                return;

            case 4:
                this.chkTradeHighLow = (CheckBox) target;
                return;

            case 5:
                this.txtMaximasCount = (TextBox) target;
                return;

            case 6:
                this.txtSellOnProfit = (TextBox) target;
                return;

            case 7:
                this.txtPositionsAddOn = (TextBox) target;
                return;

            case 8:
                this.chkCloseOnReverseOnly = (CheckBox) target;
                return;

            case 9:
                this.chkCloseOnProfitOnly = (CheckBox) target;
                return;

            case 10:
                this.txtVoltageCMAPeriod = (TextBox) target;
                return;

            case 11:
                this.chkSaveVoltsToFile = (CheckBox) target;
                return;

            case 12:
                this.chkShowOtherCorridors = (CheckBox) target;
                return;

            case 13:
                this.chkSpeedWeight = (CheckBox) target;
                return;

            case 14:
                this.chkWaveWeight = (CheckBox) target;
                return;

            case 15:
                this.txtWaveMinRatio = (TextBox) target;
                return;

            case 0x10:
                this.txtEdgeMargin = (TextBox) target;
                return;

            case 0x11:
                this.txtProfitMin = (TextBox) target;
                return;

            case 0x12:
                this.chkDB = (CheckBox) target;
                return;

            case 0x13:
                this.chkFastClose = (CheckBox) target;
                return;

            case 20:
                this.cbRegressionMode = (ComboBox) target;
                return;

            case 0x15:
                ((Border) target).MouseDown += new MouseButtonEventHandler(this.ShowSettings);
                return;

            case 0x16:
                this.lblBSMinMax = (Label) target;
                return;

            case 0x17:
                this.txtBSPeriodMin = (TextBox) target;
                this.txtBSPeriodMin.TextChanged += new TextChangedEventHandler(this.txtBSPeriod_TextChanged);
                return;

            case 0x18:
                this.lblPeriodsMax = (Label) target;
                return;

            case 0x19:
                this.txtSpreadRatio = (TextBox) target;
                this.txtSpreadRatio.TextChanged += new TextChangedEventHandler(this.txtBSPeriod_TextChanged);
                return;

            case 0x1a:
                this.txtSpreadMinutesBack = (TextBox) target;
                this.txtSpreadMinutesBack.TextChanged += new TextChangedEventHandler(this.txtBSPeriod_TextChanged);
                return;

            case 0x1b:
                this.txtfirstBarRow = (TextBox) target;
                this.txtfirstBarRow.TextChanged += new TextChangedEventHandler(this.txtBSPeriod_TextChanged);
                return;

            case 0x1c:
                this.lblUpDown = (Label) target;
                return;

            case 0x1d:
                this.txtVoltageTimeFrame = (TextBox) target;
                return;

            case 30:
                this.lblVolatility = (Label) target;
                return;

            case 0x1f:
                this.txtVolatilityMin = (TextBox) target;
                this.txtVolatilityMin.TextChanged += new TextChangedEventHandler(this.txtBSPeriod_TextChanged);
                return;

            case 0x20:
                this.lblOpenBuy = (Label) target;
                return;

            case 0x21:
                this.lblOpenSell = (Label) target;
                return;

            case 0x22:
                this.lblMainWave = (Label) target;
                return;

            case 0x23:
                this.txtFoo = (TextBox) target;
                return;

            case 0x24:
                this.cbPositionFoo = (ComboBox) target;
                return;

            case 0x25:
                this.lblPower = (Label) target;
                return;

            case 0x26:
                this.chkOpenTrade = (CheckBox) target;
                return;

            case 0x27:
                this.chkCloseTrade = (CheckBox) target;
                return;

            case 40:
                ((Border) target).MouseDown += new MouseButtonEventHandler(this.Voltage_MouseDown);
                return;

            case 0x29:
                this.lblServerTime = (Label) target;
                return;

            case 0x2a:
                this.popVolts = (Popup) target;
                return;

            case 0x2b:
                ((DataGrid) target).AutoGeneratingColumn += new EventHandler<DataGridAutoGeneratingColumnEventArgs>(this.dgBuySellBars_AutoGeneratingColumn);
                return;

            case 0x2c:
                this.dgBuySellBars = (DataGrid) target;
                this.dgBuySellBars.AutoGeneratingColumn += new EventHandler<DataGridAutoGeneratingColumnEventArgs>(this.dgBuySellBars_AutoGeneratingColumn);
                return;
        }
        this._contentLoaded = true;
    }

    public double TakeProfitNet(double TakePropfitPips, Summary Summary, bool Buy)
    {
        return Math.Round((TakePropfitPips == 0.0) ? 0.0 : (Buy ? (Summary.BuyAvgOpen + (TakePropfitPips * this.fw.PointSize)) : (Summary.SellAvgOpen - (TakePropfitPips * this.fw.PointSize))), this.fw.Digits);
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        try
        {
            if (this.fw.ServerTime.AddMinutes(2.0) <= DateTime.Now)
            {
                this.fw.ReLogin();
            }
        }
        catch
        {
        }
    }

    private void txtBSPeriod_TextChanged(object sender, TextChangedEventArgs e)
    {
        this.ResetBars();
    }

    private void Voltage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.popVolts.Placement = PlacementMode.Bottom;
        this.popVolts.PlacementTarget = sender as UIElement;
        this.popVolts.IsOpen = !this.popVolts.IsOpen;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, delegate (object o) {
            base.Hide();
            return null;
        }, null);
    }

    private void Window_Initialized(object sender, EventArgs e)
    {
        this.dgBuySellBars.ItemsSource = new ListCollectionView(new ObservableCollection<PriceBar>());
    }

    // Properties
    public ObservableCollection<PriceBar> BarsDataSource
    {
        get
        {
            return this._barsDataSource;
        }
    }

    private int bsPeriodMax
    {
        set
        {
            Lib.SetLabelText(this.lblPeriodsMax, value);
        }
    }

    public int bsPeriodMin
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtBSPeriodMin);
        }
    }

    public bool CanTrade
    {
        get
        {
            return this._canTrade;
        }
        set
        {
            this._canTrade = value;
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs("CanTrade"));
            }
        }
    }

    public bool CloseBuy { get; protected set; }

    private bool closeOnProfitOnly
    {
        get
        {
            return Lib.GetChecked(this.chkCloseOnProfitOnly).Value;
        }
    }

    private bool closeOnReverseOnly
    {
        get
        {
            return Lib.GetChecked(this.chkCloseOnReverseOnly).Value;
        }
    }

    public bool CloseSell { get; protected set; }

    private ViewModel DC
    {
        get
        {
            return (base.DataContext as ViewModel);
        }
    }

    public double DencityRatio { get; protected set; }

    private bool doDB
    {
        get
        {
            return Lib.GetChecked(this.chkDB).Value;
        }
    }

    private bool doSpeedWeight
    {
        get
        {
            return Lib.GetChecked(this.chkSpeedWeight).Value;
        }
    }

    private bool doWaveWeight
    {
        get
        {
            return Lib.GetChecked(this.chkWaveWeight).Value;
        }
    }

    private double edgeMargin
    {
        get
        {
            return Lib.GetTextBoxTextDouble(this.txtEdgeMargin);
        }
    }

    private bool fastClose
    {
        get
        {
            return Lib.GetChecked(this.chkFastClose).Value;
        }
    }

    private bool fastTrade
    {
        get
        {
            return false;
        }
    }

    private int firstBarRow
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtfirstBarRow);
        }
    }

    private int fooNumber
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtFoo);
        }
    }

    private int fooPosition
    {
        get
        {
            return Lib.GetComboBoxIndex(this.cbPositionFoo);
        }
    }

    private bool forceCloseTrade
    {
        get
        {
            return Lib.GetChecked(this.chkCloseTrade).Value;
        }
        set
        {
            Lib.SetChecked(this.chkCloseTrade, value, true);
        }
    }

    private bool forceOpenTrade
    {
        get
        {
            return Lib.GetChecked(this.chkOpenTrade).Value;
        }
        set
        {
            Lib.SetChecked(this.chkOpenTrade, value, true);
        }
    }

    private FXCoreWrapper fw { get; set; }

    public bool GoBuy { get; protected set; }

    public bool GoSell { get; protected set; }

    private int maximasCount
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtMaximasCount);
        }
    }

    private bool moveTimeFrameByPos
    {
        get
        {
            return false;
        }
    }

    private string pair
    {
        get
        {
            return this.fw.Pair;
        }
    }

    private int positionsAddOn
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtPositionsAddOn);
        }
    }

    private double profitMin
    {
        get
        {
            return Lib.GetTextBoxTextDouble(this.txtProfitMin);
        }
    }

    private FXCoreWrapper.RegressionMode regressionMode
    {
        get
        {
            return (FXCoreWrapper.RegressionMode) Lib.GetComboBoxIndex(this.cbRegressionMode);
        }
    }

    private bool saveVoltsToFile
    {
        get
        {
            return Lib.GetChecked(this.chkSaveVoltsToFile).Value;
        }
    }

    private int sellOnProfit
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtSellOnProfit);
        }
    }

    private bool showOtherCorridors
    {
        get
        {
            return Lib.GetChecked(this.chkShowOtherCorridors).Value;
        }
    }

    private int spreadMinutesBack
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtSpreadMinutesBack);
        }
    }

    private double spreadRatio
    {
        get
        {
            return Lib.GetTextBoxTextDouble(this.txtSpreadRatio);
        }
    }

    public double StopLossBuy { get; protected set; }

    public double StopLossSell { get; protected set; }

    public double TakeProfitBuy { get; protected set; }

    public double TakeProfitSell { get; protected set; }

    private IEnumerable<FXCoreWrapper.Tick> TicksInTimeFrame
    {
        get
        {
            return (from t in this.Ticks
                orderby t.StartDate descending
                select t).Take<FXCoreWrapper.Tick>(this.spreadMinutesBack);
        }
    }

    private int TimeFrame
    {
        get
        {
            return this._timeFrame;
        }
        set
        {
            this._timeFrame = this.bsPeriodMax = value;
        }
    }

    private bool tradeHighLow
    {
        get
        {
            return Lib.GetChecked(this.chkTradeHighLow).Value;
        }
    }

    private double volatilityMin
    {
        get
        {
            return Lib.GetTextBoxTextDouble(this.txtVolatilityMin);
        }
    }

    private int voltageCMAPeriod
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtVoltageCMAPeriod);
        }
    }

    private int voltageTimeFrame
    {
        get
        {
            return Lib.GetTextBoxTextInt(this.txtVoltageTimeFrame);
        }
        set
        {
            Lib.SetTextBoxText(this.txtVoltageTimeFrame, value);
        }
    }

    private double waveMinRatio
    {
        get
        {
            return Lib.GetTextBoxTextDouble(this.txtWaveMinRatio);
        }
    }

    // Nested Types
    public class PriceBar
    {
        // Properties
        [DisplayName("")]
        public double AskHigh { get; set; }

        [DisplayName("")]
        public double AskLow { get; set; }

        [DisplayName("Avg")]
        public double Average
        {
            get
            {
                return ((this.AverageAsk + this.AverageBid) / 2.0);
            }
        }

        [DisplayName("")]
        public double AverageAsk { get; set; }

        [DisplayName("")]
        public double AverageBid { get; set; }

        [DisplayName("")]
        public double BidHigh { get; set; }

        [DisplayName("")]
        public double BidLow { get; set; }

        [DisplayName(""), DisplayFormat(DataFormatString="{0:n1}")]
        public double Distance { get; set; }

        [DisplayFormat(DataFormatString="{0:n1}"), DisplayName("")]
        public double Power
        {
            get
            {
                return (this.Spread * this.Speed);
            }
        }

        [DisplayName("Row"), DisplayFormat(DataFormatString="{0:n1}")]
        public double Row { get; set; }

        [DisplayFormat(DataFormatString="{0:n1}"), DisplayName("")]
        public double Speed { get; set; }

        [DisplayFormat(DataFormatString="{0:n0}"), DisplayName("")]
        public double Spread { get; set; }

        [DisplayFormat(DataFormatString="{0:dd HH:mm}"), DisplayName("Date")]
        public DateTime StartDate { get; set; }

        [DisplayName("Volts"), DisplayFormat(DataFormatString="{0:n3}")]
        public double Volts
        {
            get
            {
                return (this.Distance / this.Row);
            }
        }
    }

    public delegate void PriceGridErrorHandler(Exception exc);

    public class RateDistance
    {
        // Methods
        public RateDistance(double spread, double distance, double averageAsk, double averageBid, double MA, DateTime startDate)
        {
            this.Spread = spread;
            this.Distance = distance;
            this.AverageAsk = averageAsk;
            this.AverageBid = averageBid;
            this.MA = MA;
            this.StartDate = startDate;
        }

        // Properties
        public double AverageAsk { get; set; }

        public double AverageBid { get; set; }

        public double Distance { get; set; }

        public double MA { get; set; }

        public double Spread { get; set; }

        public DateTime StartDate { get; set; }
    }

    public class TickChangedEventArgs : EventArgs
    {
        // Fields
        public FXCoreWrapper.Tick[] Ticks;

        // Methods
        public TickChangedEventArgs(FXCoreWrapper.Tick[] ticks, double voltageHigh, double voltageCurr, double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr) : this(ticks, voltageHigh, voltageCurr, 0.0, 0.0, netBuy, netSell, timeHigh, timeCurr, CS$0$0000, null)
        {
            double[] CS$0$0000 = new double[2];
        }

        public TickChangedEventArgs(FXCoreWrapper.Tick[] ticks, double voltageHigh, double voltageCurr, double priceMaxAverage, double priceMinAverage, double netBuy, double netSell, DateTime timeHigh, DateTime timeCurr, double[] priceAverage, List<FXCoreWrapper.Volt> voltsByTick)
        {
            this.Ticks = ticks;
            this.VoltageHigh = voltageHigh;
            this.VoltageCurr = voltageCurr;
            this.PriceMaxAverage = priceMaxAverage;
            this.PriceMinAverage = priceMinAverage;
            this.NetBuy = netBuy;
            this.NetSell = netSell;
            this.TimeHigh = timeHigh;
            this.TimeCurr = timeCurr;
            this.PriceAverage = priceAverage;
            this.VoltsByTick = voltsByTick;
        }

        // Properties
        public double NetBuy { get; set; }

        public double NetSell { get; set; }

        public double[] PriceAverage { get; set; }

        public double PriceMaxAverage { get; set; }

        public double PriceMinAverage { get; set; }

        public DateTime TimeCurr { get; set; }

        public DateTime TimeHigh { get; set; }

        public double VoltageCurr { get; set; }

        public double VoltageHigh { get; set; }

        public List<FXCoreWrapper.Volt> VoltsByTick { get; set; }
    }

    public class Volt
    {
        // Methods
        public Volt(DateTime startDate, double volts, double averageAsk, double averageBid)
        {
            this.StartDate = startDate;
            this.Volts = volts;
            this.AverageAsk = averageAsk;
            this.AverageBid = averageBid;
        }

        // Properties
        public double Average
        {
            get
            {
                return ((this.AverageAsk + this.AverageBid) / 2.0);
            }
        }

        [DisplayName("")]
        public double AverageAsk { get; set; }

        [DisplayName("")]
        public double AverageBid { get; set; }

        [DisplayName("Date"), DisplayFormat(DataFormatString="{0:dd HH:mm}")]
        public DateTime StartDate { get; set; }

        [DisplayFormat(DataFormatString="{0:n3}")]
        public double Volts { get; set; }
    }
}

