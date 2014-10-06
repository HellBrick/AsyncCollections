using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HellBrick.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HellBrick.Collections.Test
{
	[TestClass]
	public class AsyncCollectionTest
	{
		private AsyncQueue<int> _collection;

		[TestInitialize]
		public void Initialize()
		{
			_collection = new AsyncQueue<int>();
		}

		[TestMethod]
		public void TakingItemFromNonEmptyCollectionCompletesImmediately()
		{
			_collection.Add( 42 );
			var itemTask = _collection.TakeAsync();
			Assert.IsTrue( itemTask.IsCompleted );
			Assert.AreEqual( 42, itemTask.Result );
		}

		[TestMethod]
		public void AddingItemCompletesPendingTask()
		{
			var itemTask = _collection.TakeAsync();
			Assert.IsFalse( itemTask.IsCompleted );

			_collection.Add( 42 );
			Assert.IsTrue( itemTask.IsCompleted );
			Assert.AreEqual( 42, itemTask.Result );
		}

		[TestMethod]
		public void CancelledTakeCancelsTask()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			var itemTask = _collection.TakeAsync( cancelSource.Token );
			cancelSource.Cancel();
			Assert.IsTrue( itemTask.IsCanceled );

			_collection.Add( 42 );
			Assert.AreEqual( 1, _collection.Count );
			Assert.AreEqual( 0, _collection.AwaiterCount );
		}

		[TestMethod]
		[Timeout( 30000 )]
		public async Task RandomMultithreadingOperationsDontCrash()
		{
			int itemsTaken = 0;
			int itemCount = 100;
			int producerThreads = 4;
			int consumerThreads = 2;
			CancellationTokenSource cancelSource = new CancellationTokenSource();

			List<Task> consumerTasks = new List<Task>();

			for ( int i = 0; i < consumerThreads; i++ )
			{
				int consumerID = i;
				var consumerTask = Task.Run(
					async () =>
					{
						while ( !cancelSource.IsCancellationRequested || itemsTaken < itemCount * producerThreads )
						{
							int item = await _collection.TakeAsync();
							Interlocked.Increment( ref itemsTaken );
							Debug.WriteLine( "{0} ( - {1} by {2} )", _collection, item, consumerID );
						}
					} );

				consumerTasks.Add( consumerTask );
			}

			List<Task> producerTasks = new List<Task>();

			for ( int i = 0; i < producerThreads; i++ )
			{
				int producerID = i;

				var producerTask = Task.Run(
					() =>
					{
						for ( int j = 0; j < itemCount; j++ )
						{
							int item = producerID * itemCount + j;	//	some kind of a unique item ID
							_collection.Add( item );
							Debug.WriteLine( _collection );
						}
					} );

				producerTasks.Add( producerTask );
			}

			await Task.WhenAll( producerTasks );
			cancelSource.Cancel();

			await Task.WhenAll( consumerTasks );
			Assert.AreEqual( 0, _collection.Count );
		}
	}
}
