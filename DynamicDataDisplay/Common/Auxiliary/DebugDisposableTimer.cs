using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Microsoft.Research.DynamicDataDisplay.Common.Auxiliary
{
	public sealed class DebugDisposableTimer : IDisposable
	{
		private readonly string name;
		Stopwatch timer;
		public DebugDisposableTimer(string name)
		{
#if DEBUG
			this.name = name;
			timer = Stopwatch.StartNew();
#endif
		}

		#region IDisposable Members

		public void Dispose()
		{
#if DEBUG
			//var duration = timer.ElapsedMilliseconds;
			//Debug.WriteLine(name + ": elapsed " + duration + " ms.");
			timer.Stop();
#endif
		}

		#endregion
	}
}
