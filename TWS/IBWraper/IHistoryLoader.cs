using IBApi;
using System;
using System.Collections.Generic;

namespace IBApp {
  public delegate TDM DataMapDelegate<TDM>(DateTime date, double open, double high, double low, double close, long volume, int count);
  public delegate IHistoryLoader HistoryLoaderDelegate<T>(IBClientCore ibClient
  , Contract contract
  , int periodsBack
  , DateTime endDate
  , TimeSpan duration
  , TimeUnit timeUnit
  , BarSize barSize
  , DataMapDelegate<T> map
  , Action<ICollection<T>> done
  , Action<ICollection<T>> dataEnd
  , Action<Exception> error) where T : HedgeHog.Bars.BarBaseDate;
  public abstract class IHistoryLoader {
    public static IHistoryLoader Factory<T>(IBClientCore ibClient
      , Contract contract
      , int periodsBack
      , DateTime endDate
      , TimeSpan duration
      , TimeUnit timeUnit
      , BarSize barSize
      , DataMapDelegate<T> map
      , Action<ICollection<T>> done
      , Action<ICollection<T>> dataEnd
      , Action<Exception> error) where T : HedgeHog.Bars.BarBaseDate {
      return new HistoryLoader<T>(ibClient
        , contract
        , periodsBack
        , endDate
        , duration
        , timeUnit
        , barSize
        , map
        , done
        , dataEnd
        , error);
    }
    public static IHistoryLoader Factory_Slow<T>(IBClientCore ibClient
      , Contract contract
      , int periodsBack
      , DateTime endDate
      , TimeSpan duration
      , TimeUnit timeUnit
      , BarSize barSize
      , DataMapDelegate<T> map
      , Action<ICollection<T>> done
      , Action<ICollection<T>> dataEnd
      , Action<Exception> error) where T : HedgeHog.Bars.BarBaseDate {
      return new HistoryLoader_Slow<T>(ibClient
        , contract
        , periodsBack
        , endDate
        , duration
        , timeUnit
        , barSize
        , map
        , done
        , dataEnd
        , error);
    }
  }
}