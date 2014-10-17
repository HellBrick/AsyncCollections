using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

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
			Assert.IsFalse( takeTask.IsCompleted );

			_queue.Add( array[ index ] );
			var batch = await takeTask;

			CollectionAssert.AreEqual( array, batch.ToList() );
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

			CollectionAssert.AreEqual( array, batch.ToList() );
		}

		[TestMethod]
		public async Task TimerFlushesPendingItems()
		{
			TimeSpan flushPeriod = TimeSpan.FromMilliseconds( 500 );
			_queue = new AsyncBatchQueue<int>( 9999, flushPeriod );
			_queue.Add( 42 );

			await Task.Delay( flushPeriod + flushPeriod );
			var batch = await _queue.TakeAsync();
			CollectionAssert.AreEqual( new[] { 42 }, batch.ToList() );
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

			Assert.AreEqual( insertThreads * itemsPerThread, itemsTaken );
		}
	}
}
