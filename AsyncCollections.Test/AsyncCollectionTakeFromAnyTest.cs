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
	public class AsyncCollectionTakeFromAnyTest
	{
		private AsyncQueue<int>[] _collections;

		[TestInitialize]
		public void Initialize()
		{
			_collections = new AsyncQueue<int>[ 2 ] { new AsyncQueue<int>(), new AsyncQueue<int>() };
		}

		[TestMethod]
		public async Task ReturnsItemFromSecondIfFirstIsEmpty()
		{
			_collections[ 1 ].Add( 42 );

			var result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );
			Assert.AreEqual( 42, result.Value );
			Assert.AreEqual( 1, result.CollectionIndex );
		}

		[TestMethod]
		public async Task NoUnnecessaryAwaitersAreQueued()
		{
			_collections[ 1 ].Add( 42 );

			var result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );
			Assert.AreEqual( 0, _collections[ 0 ].AwaiterCount );
		}

		[TestMethod]
		public async Task RespectsCollectionOrder()
		{
			_collections[ 0 ].Add( 42 );
			_collections[ 1 ].Add( 24 );

			var result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );
			Assert.AreEqual( 42, result.Value );
			Assert.AreEqual( 0, result.CollectionIndex );
		}

		[TestMethod]
		public void ReturnsItemIfItIsAddedLater()
		{
			var task = AsyncCollection<int>.TakeFromAnyAsync( _collections );
			Assert.IsFalse( task.IsCompleted );

			_collections[ 1 ].Add( 42 );
			Assert.IsTrue( task.IsCompleted );
			Assert.AreEqual( 42, task.Result.Value );
			Assert.AreEqual( 1, task.Result.CollectionIndex );
		}

		[TestMethod]
		public void CancelsTaskWhenTokenIsCanceled()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			var task = AsyncCollection<int>.TakeFromAnyAsync( _collections, cancelSource.Token );

			cancelSource.Cancel();
			Assert.IsTrue( task.IsCanceled );

			_collections[ 0 ].Add( 42 );
			_collections[ 1 ].Add( 64 );
			Assert.AreEqual( 1, _collections[ 0 ].Count );
			Assert.AreEqual( 1, _collections[ 1 ].Count );
		}
	}
}
