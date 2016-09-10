using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HedgeHog {
  public static class TaskMonad {
    public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> source) {
      return Task.WhenAll(source);
    }
    public static Task WhenAll(this IEnumerable<Task> source) {
      return Task.WhenAll(source);
    }
    public static async Task WhenAllSequiential(this IEnumerable<Task> tasks) {
      foreach(var task in tasks) {
        await task;
      }
    }
    public static async Task<IList<T>> WhenAllSequiential<T>(this IEnumerable<Task<T>> tasks) {
      List<T> list = new List<T>();
      foreach(var task in tasks) {
        list.Add(await task);
      }
      return list;
    }
    public static Task<T> Unit<T>(this T value) {
      return Task.FromResult(value);
    }
    public static async Task<V> Bind<U, V>(this Task<U> m, Func<U, Task<V>> k) {
      return await k(await m);
    }
    public static async Task<V> SelectMany<T, U, V>(this Task<T> source, Func<T, Task<U>> selector, Func<T, U, V> resultSelector) {
      T t = await source;
      U u = await selector(t);
      return resultSelector(t, u);
    }
    public static async Task<U> Select<T, U>(this Task<T> source, Func<T, U> selector) {
      T t = await source;
      return selector(t);
    }

    private static readonly TaskFactory _myTaskFactory = new
         TaskFactory(CancellationToken.None, TaskCreationOptions.None,
         TaskContinuationOptions.None, TaskScheduler.Default);

    // Microsoft.AspNet.Identity.AsyncHelper
    public static TResult RunSync<TResult>(Func<Task<TResult>> func) {
      CultureInfo cultureUi = CultureInfo.CurrentUICulture;
      CultureInfo culture = CultureInfo.CurrentCulture;
      return _myTaskFactory.StartNew<Task<TResult>>(delegate {
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = cultureUi;
        return func();
      }).Unwrap<TResult>().GetAwaiter().GetResult();
    }
  }
}