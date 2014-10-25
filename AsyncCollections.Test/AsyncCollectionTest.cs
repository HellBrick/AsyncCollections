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
	public abstract class AsyncCollectionTest<TAsyncCollection> where TAsyncCollection: IAsyncCollection<int>
	{
		protected TAsyncCollection Collection { get; private set; }

		[TestInitialize]
		public void Initialize()
		{
			Collection = CreateCollection();
		}

		protected abstract TAsyncCollection CreateCollection();

		[TestMethod]
		public void TakingItemFromNonEmptyCollectionCompletesImmediately()
		{
			Collection.Add( 42 );
			var itemTask = Collection.TakeAsync( CancellationToken.None );
			Assert.IsTrue( itemTask.IsCompleted );
			Assert.AreEqual( 42, itemTask.Result );
		}

		[TestMethod]
		public void AddingItemCompletesPendingTask()
		{
			var itemTask = Collection.TakeAsync( CancellationToken.None );
			Assert.IsFalse( itemTask.IsCompleted );

			Collection.Add( 42 );
			Assert.IsTrue( itemTask.IsCompleted );
			Assert.AreEqual( 42, itemTask.Result );
		}

		[TestMethod]
		public void CancelledTakeCancelsTask()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			var itemTask = Collection.TakeAsync( cancelSource.Token );
			cancelSource.Cancel();
			Assert.IsTrue( itemTask.IsCanceled );

			Collection.Add( 42 );
			Assert.AreEqual( 1, Collection.Count );
			Assert.AreEqual( 0, Collection.AwaiterCount );
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
								int item = await Collection.TakeAsync( cancelSource.Token );
								int itemsTakenLocal = Interlocked.Increment( ref itemsTaken );
								if ( itemsTakenLocal == totalItemCount )
									cancelSource.Cancel();

								Debug.WriteLine( "{0} ( - {1} by {2} )", Collection, item, consumerID );
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
							Collection.Add( item );
							Debug.WriteLine( Collection );
						}
					} );

				producerTasks.Add( producerTask );
			}

			await Task.WhenAll( producerTasks );

			await Task.WhenAll( consumerTasks );
			Assert.AreEqual( 0, Collection.Count );
		}
	}

	[TestClass]
	public class AsyncQueueTest: AsyncCollectionTest<AsyncQueue<int>>
	{
		protected override AsyncQueue<int> CreateCollection()
		{
			return new AsyncQueue<int>();
		}
	}

	[TestClass]
	public class AsyncStackTest: AsyncCollectionTest<AsyncStack<int>>
	{
		protected override AsyncStack<int> CreateCollection()
		{
			return new AsyncStack<int>();
		}
	}
}
