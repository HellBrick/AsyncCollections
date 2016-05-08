using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
			itemTask.IsCompleted.Should().BeTrue();
			itemTask.Result.Should().Be( 42 );
		}

		[TestMethod]
		public async Task AddingItemCompletesPendingTask()
		{
			var itemTask = Collection.TakeAsync( CancellationToken.None );
			itemTask.IsCompleted.Should().BeFalse();

			Collection.Add( 42 );
			( await itemTask ).Should().Be( 42 );
		}

		[TestMethod]
		public void TakeWithCanceledTokenReturnsCanceledTask()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			cancelSource.Cancel();
			Collection.Add( 42 );

			Task<int> itemTask = Collection.TakeAsync( cancelSource.Token );
			itemTask.IsCanceled.Should().BeTrue( "The task should have been canceled." );
		}

		[TestMethod]
		public void CancelledTakeCancelsTask()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			var itemTask = Collection.TakeAsync( cancelSource.Token );
			cancelSource.Cancel();

			Func<Task> asyncAct = () => itemTask;
			asyncAct.ShouldThrow<TaskCanceledException>();

			Collection.Add( 42 );
			Collection.Count.Should().Be( 1 );
			Collection.AwaiterCount.Should().Be( 0 );
		}

		[TestMethod]
		public void ContinuationIsNotInlinedOnAddThread()
		{
			Task<int> takeTask = TakeAndReturnContinuationThreadIdAsync();
			int addThreadID = Thread.CurrentThread.ManagedThreadId;
			Collection.Add( 42 );
			int continuationThreadID = takeTask.GetAwaiter().GetResult();

			addThreadID.Should().NotBe( continuationThreadID, "TakeAsync() continuation shouldn't have been inlined on the Add() thread." );
		}

		private async Task<int> TakeAndReturnContinuationThreadIdAsync()
		{
			await Collection.TakeAsync().ConfigureAwait( false );
			return Thread.CurrentThread.ManagedThreadId;
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
			Collection.Count.Should().Be( 0 );
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
