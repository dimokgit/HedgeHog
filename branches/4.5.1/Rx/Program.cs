using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using HedgeHog;
using System.Reactive.Linq;

namespace Rx {
  class GroupBy_Simple {
    static IEnumerable<ConsoleKeyInfo> KeyPresses() {
      for (; ; ) {
        var currentKey = Console.ReadKey(true);

        if (currentKey.Key == ConsoleKey.Enter)
          yield break;
        else
          yield return currentKey;
      }
    }
    static void Main() {
      var timeToStop = new ManualResetEvent(false);
      var keyPresses = KeyPresses().ToObservable();

      var groupedKeyPresses =
          keyPresses.GroupByUntil(k => new { k.Key, k.KeyChar }, g => Observable.Timer(DateTime.Now.Round().AddMinutes(1)));
      //group k by k.Key into keyPressGroup
      //    select keyPressGroup;

      Console.WriteLine("Press Enter to stop.  Now bang that keyboard!");

      groupedKeyPresses.Subscribe(keyPressGroup => {
        int numberPresses = 0;

        keyPressGroup.TakeLast(1).Subscribe(keyPress => {
          Console.WriteLine(
              "You pressed the {0}/{2} key {1} time(s)!",
              keyPress.Key,
              ++numberPresses,keyPress.KeyChar);
        },
        () => timeToStop.Set());
      });

      timeToStop.WaitOne();
    }
  }

}
