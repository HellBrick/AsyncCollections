﻿using System;
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
	public abstract class AsyncCollectionTest
	{
		private IAsyncCollection<int> _collection;

		[TestInitialize]
		public void Initialize()
		{
			_collection = CreateCollection();
		}

		protected abstract IAsyncCollection<int> CreateCollection();

		[TestMethod]
		public void TakingItemFromNonEmptyCollectionCompletesImmediately()
		{
			_collection.Add( 42 );
			var itemTask = _collection.TakeAsync( CancellationToken.None );
			Assert.IsTrue( itemTask.IsCompleted );
			Assert.AreEqual( 42, itemTask.Result );
		}

		[TestMethod]
		public void AddingItemCompletesPendingTask()
		{
			var itemTask = _collection.TakeAsync( CancellationToken.None );
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
			int totalItemCount = itemCount * producerThreads;
			int consumerThreads = 2;
			CancellationTokenSource cancelSource = new CancellationTokenSource();

			List<Task> consumerTasks = new List<Task>();			

			for ( int i = 0; i < consumerThreads; i++ )
			{
				int consumerID = i;
				var consumerTask = Task.Run(
					async () =>
					{
						try
						{
							while ( itemsTaken < totalItemCount )
							{
								int item = await _collection.TakeAsync( cancelSource.Token );
								int itemsTakenLocal = Interlocked.Increment( ref itemsTaken );
								if ( itemsTakenLocal == totalItemCount )
									cancelSource.Cancel();

								Debug.WriteLine( "{0} ( - {1} by {2} )", _collection, item, consumerID );
							}
						}
						catch ( OperationCanceledException )
						{
							//	This is expected
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

			await Task.WhenAll( consumerTasks );
			Assert.AreEqual( 0, _collection.Count );
		}
	}

	[TestClass]
	public class AsyncQueueTest: AsyncCollectionTest
	{
		protected override IAsyncCollection<int> CreateCollection()
		{
			return new AsyncQueue<int>();
		}
	}

	[TestClass]
	public class AsyncStackTest: AsyncCollectionTest
	{
		protected override IAsyncCollection<int> CreateCollection()
		{
			return new AsyncStack<int>();
		}
	}

    [TestClass]
    public class AsyncPriorityQueueTest : AsyncCollectionTest
    {
        protected override IAsyncCollection<int> CreateCollection()
        {
            return new AsyncPriorityQueue<int>();
        }
    }
}
