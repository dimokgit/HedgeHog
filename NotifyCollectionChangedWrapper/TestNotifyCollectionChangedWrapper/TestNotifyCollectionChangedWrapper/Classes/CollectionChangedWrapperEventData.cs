//=============================================================================
// Date		: March 11, 2010
//	Author	: Anthony Paul Ortiz
//	License	: CPOL
//=============================================================================

#region Using Statements

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using System.Collections.Specialized;

#endregion Using Statements

namespace NotifyCollectionChangedWrapper
	{
	internal class CollectionChangedWrapperEventData
		{
		#region Public Properties

		public Dispatcher Dispatcher
			{
			get;
			set;
			}

		public Action<NotifyCollectionChangedEventArgs> Action
			{
			get;
			set;
			}

		#endregion Public Properties

		#region Constructor

		public CollectionChangedWrapperEventData(Dispatcher dispatcher, Action<NotifyCollectionChangedEventArgs> action)
			{
			Dispatcher = dispatcher;
			Action = action;
			}

		#endregion Constructor
		}
	}
