using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace HedgeHog {
  class UI {
    public double minumumTimeByFractalWaveRatio = 2;

    public bool _chkIsDemo;
    public bool isDemo { get { return _chkIsDemo; } }

    public bool _chkNormalizeFractals;
    public bool normalizeFractals { get { return _chkNormalizeFractals; } }

    public bool _chkUseOverlapShort;
    public bool useOverlapShort { get { return _chkUseOverlapShort; } }

    public bool _chkUseOverlapLast;
    public bool useOverlapLast { get { return _chkUseOverlapLast; } }

    public bool _chkTradeByFractal;
    public bool tradeByFractal { get { return _chkTradeByFractal; } }


    public bool _chkVerboseLogging;
    public bool verboseLogging { get { return _chkVerboseLogging; } }

    public bool? _chkCorridorByMinimumVolatility;
    public bool? corridorByMinimumVolatility {
      get { return _chkCorridorByMinimumVolatility.HasValue ? _chkCorridorByMinimumVolatility : null; }
      set { _chkCorridorByMinimumVolatility = value.HasValue ? value : null; }
    }

    public int _txtVolatilityWieght;
    public int volatilityWieght { get { return _txtVolatilityWieght; } }

    public int _txtVolatilityWieght1;
    public int volatilityWieght1 { get { return _txtVolatilityWieght1; } }

    public int _txtWavePolynomeOrder;
    public int wavePolynomeOrder { get { return _txtWavePolynomeOrder; } }

    public double _txtWaveRatioMinimum;
    public double waveRatioMinimum { get { return _txtWaveRatioMinimum; } }
    public int _txtHighMinutesHoursBack;
    public int highMinutesHoursBack { get { return _txtHighMinutesHoursBack; } }

    public int _txtCorridorHeightMinutes;
    public int corridorHeightMinutes { get { return _txtCorridorHeightMinutes; } }


    public string _txtCorridorHeightMinutesSchedule;
    public int corridorHeightMinutesBySchedule {
      get {
        if (_txtCorridorHeightMinutesSchedule == null) return corridorHeightMinutes;
        var ms = Regex.Matches(_txtCorridorHeightMinutesSchedule, @"(?<timeFrom>[^-]+)-(?<timeTo>[^=]+)=(?<minutes>\d+)");
        var time = DateTime.Now.TimeOfDay;
        var min = ms.Cast<Match>().SingleOrDefault(
          m => time.Between(DateTime.Parse(m.Groups["timeFrom"].Value).TimeOfDay, DateTime.Parse(m.Groups["timeTo"].Value).TimeOfDay)
            );
        try {
          return min == null ? corridorHeightMinutes : int.Parse(min.Groups["minutes"].Value);
        } catch (Exception exc) {
          if (MessageBox.Show(exc + Environment.NewLine + "Clear schedule?", "Schedule Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            _txtCorridorHeightMinutesSchedule = "";
          return corridorHeightMinutes;
        }
      }
    }

    public int _txtCorridorMinimumPercent;
    public int corridorMinimumPercent { get { return _txtCorridorMinimumPercent; } }


    public int _txtTimeFrameMinutesMinimum;
    public int timeFrameMinutesMinimum { get { return _txtTimeFrameMinutesMinimum; } }

    public int _txtTimeFrameMinutesMaximum;
    public int timeFrameMinutesMaximum { get { return _txtTimeFrameMinutesMaximum; } }

    public double _txtFractalPadding;
    public double fractalPadding { get { return _txtFractalPadding / 100.0; } }

    public double _txtFractalShortPercent;
    public int fractalMinutes { get { return _txtFractalShortPercent.ToInt(); } }

    public string _txtRsiTicks;
    public int rsiTicks {
      get {
        return !_txtRsiTicks.Contains('*')
          ? int.Parse(_txtRsiTicks)
          : int.Parse(_txtRsiTicks.Split('*')[0]) * int.Parse(_txtRsiTicks.Split('*')[1]);
      }
    }

    public int rsiTicksDelay { get { return int.Parse(_txtRsiTicks.Split('*')[0]); } }

    public bool _chkCorridorByUpDownRatio;
    public bool corridorByUpDownRatio { get { return _chkCorridorByUpDownRatio; } }

    public string _txtAcount;
    public string Account { get { return _txtAcount; } }

    public int _txtTicksBack;
    public int ticksBack { get { return _txtTicksBack; } }

    public int _txtWavesCountBig;
    public int wavesCountBig { get { return _txtWavesCountBig; } }

    public int _txtWavesCountSmall;
    public int wavesCountSmall { get { return _txtWavesCountSmall; } }

    public string _txtTimeFrameTimeStart;
    public DateTime timeFrameTimeStart {
      get {
        var s = _txtTimeFrameTimeStart + "";
        DateTime d;
        if (Regex.IsMatch(s, @"\d+/\d+/\d{2,} \d\d:\d\d(:\d\d)?") && DateTime.TryParse(s + "", out d)) return d;
        return DateTime.FromOADate(0);
      }
    }

    public int _txtPeakTradeMarginLow;
    public double peakTradeMarginLow { get { return _txtPeakTradeMarginLow; } }

    public int _txtPeakTradeMarginHigh;
    public double peakTradeMarginHigh { get { return _txtPeakTradeMarginHigh; } }

    public int _txtMass1Mass0TradeRatio;
    public double mass1Mass0TradeRatio { get { return _txtMass1Mass0TradeRatio/100.0; } }
        
    public bool _chkSaveVoltageToFile;
    public bool SaveVoltageToFile { get { return _chkSaveVoltageToFile; } }

    public bool _chkGroupTicks;
    public bool groupTicks { get { return _chkGroupTicks; } }

    public bool _chkCachePriceHeight;
    public bool cachePriceHeight { get { return _chkCachePriceHeight; } }

  }
}
