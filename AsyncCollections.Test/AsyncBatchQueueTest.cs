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
	}
}
