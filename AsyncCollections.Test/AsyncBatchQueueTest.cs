using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Threading;
using FluentAssertions;

namespace HellBrick.Collections.Test
{
	[TestClass]
	public class AsyncBatchQueueTest
	{
		AsyncBatchQueue<int> _queue;

		[TestCleanup]
		public void CleanUp()
		{
			if ( _queue != null )
				_queue.Dispose();
		}

		[TestMethod]
		[ExpectedException( typeof( ArgumentOutOfRangeException ) )]
		public void ThrowsOnIncorrectBatchSize()
		{
			_queue = new AsyncBatchQueue<int>( 0 );
		}

		[TestMethod]
		public async Task FlushesWhenBatchSizeIsReached()
		{
			int[] array = { 0, 1, 42 };
			int index = 0;

			_queue = new AsyncBatchQueue<int>( array.Length );
			for ( ; index < array.Length - 1; index++ )
				_queue.Add( array[ index ] );

			var takeTask = _queue.TakeAsync();
			takeTask.IsCompleted.Should().BeFalse();

			_queue.Add( array[ index ] );
			var batch = await takeTask;

			batch.Should().BeEqualTo( array );
		}

		[TestMethod]
		public async Task ManualFlushWorks()
		{
			int[] array = { 0, 1, 42 };

			_queue = new AsyncBatchQueue<int>( 50 );
			foreach ( var item in array )
				_queue.Add( item );

			_queue.Flush();
			var batch = await _queue.TakeAsync();

			batch.Should().BeEqualTo( array );
		}

		[TestMethod]
		public async Task TimerFlushesPendingItems()
		{
			TimeSpan flushPeriod = TimeSpan.FromMilliseconds( 500 );
			_queue = new AsyncBatchQueue<int>( 9999, flushPeriod );
			_queue.Add( 42 );

			await Task.Delay( flushPeriod + flushPeriod );
			var batch = await _queue.TakeAsync();
			batch.Should().BeEqualTo( new[] { 42 } );
		}

		[TestMethod]
		public async Task MultithreadingInsertsDontCrash()
		{
			int insertThreads = 4;
			int itemsPerThread = 100;

			_queue = new AsyncBatchQueue<int>( 11 );

			List<Task> insertTasks = Enumerable.Range( 1, insertThreads )
				.Select(
					_ => Task.Run(
						() =>
						{
							for ( int i = 0; i < itemsPerThread; i++ )
								_queue.Add( 42 );
						} ) )
				.ToList();

			await Task.WhenAll( insertTasks );
			_queue.Flush();

			int itemsTaken = 0;
			while ( _queue.Count > 0 )
				itemsTaken += ( await _queue.TakeAsync() ).Count;

			itemsTaken.Should().Be( insertThreads * itemsPerThread );
		}

		[TestMethod]
		public async Task NoRaceBetweenFlushOnAddAndOnDemand()
		{
			const int attempts = 100 * 1000;
			const int batchSize = 5;
			_queue = new AsyncBatchQueue<int>( batchSize );

			for ( int attemptNumber = 0; attemptNumber < attempts; attemptNumber++ )
			{
				AddAllItemsButOne( batchSize );

				using ( ManualResetEvent trigger = new ManualResetEvent( initialState: false ) )
				{
					Task addTask = Task.Run
					(
						() =>
						{
							trigger.WaitOne();
							_queue.Add( 666 );
						}
					);

					Task flushTask = Task.Run
					(
						() =>
						{
							trigger.WaitOne();
							_queue.Flush();
						}
					);

					trigger.Set();
					await addTask;
					await flushTask;

					IReadOnlyList<int> batch = await _queue.TakeAsync();
					List<int> allItems = batch.ToList();

					// This happens if Flush occurred before Add, which means there's another item from Add left unflushed.
					// Gotta flush once more to extract it.
					if ( batch.Count < batchSize )
					{
						_queue.Flush();
						IReadOnlyList<int> secondBatch = await _queue.TakeAsync();
						allItems.AddRange( secondBatch );
					}

					allItems.Count.Should().BeLessOrEqualTo( batchSize, $"Double flush detected at attempt #{attemptNumber}. Items: {String.Join( ", ", allItems )}" );
				}
			}
		}

		private void AddAllItemsButOne( int batchSize )
		{
			for ( int itemIndex = 0; itemIndex < batchSize - 1; itemIndex++ )
				_queue.Add( itemIndex );
		}
	}
}
