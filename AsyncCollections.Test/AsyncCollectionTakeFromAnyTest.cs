using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
			result.Value.Should().Be( 42 );
			result.CollectionIndex.Should().Be( 1 );
		}

		[TestMethod]
		public async Task NoUnnecessaryAwaitersAreQueued()
		{
			_collections[ 1 ].Add( 42 );

			var result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );
			_collections[ 0 ].AwaiterCount.Should().Be( 0 );
		}

		[TestMethod]
		public async Task RespectsCollectionOrder()
		{
			_collections[ 0 ].Add( 42 );
			_collections[ 1 ].Add( 24 );

			var result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );
			result.Value.Should().Be( 42 );
			result.CollectionIndex.Should().Be( 0 );
		}

		[TestMethod]
		public async Task ReturnsItemIfItIsAddedLater()
		{
			var task = AsyncCollection<int>.TakeFromAnyAsync( _collections );
			task.IsCompleted.Should().BeFalse();

			_collections[ 1 ].Add( 42 );
			var result = await task;
			result.Value.Should().Be( 42 );
			result.CollectionIndex.Should().Be( 1 );
		}

		[TestMethod]
		public void CancelsTaskWhenTokenIsCanceled()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			var task = AsyncCollection<int>.TakeFromAnyAsync( _collections, cancelSource.Token );

			cancelSource.Cancel();
			task.IsCanceled.Should().BeTrue();

			_collections[ 0 ].Add( 42 );
			_collections[ 1 ].Add( 64 );
			_collections[ 0 ].Count.Should().Be( 1 );
			_collections[ 1 ].Count.Should().Be( 1 );
		}

		[TestMethod]
		public void DoesNothingIfTokenIsCanceledBeforeMethodCall()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			cancelSource.Cancel();
			_collections[ 0 ].Add( 42 );

			var task = AsyncCollection<int>.TakeFromAnyAsync( _collections, cancelSource.Token );

			task.IsCanceled.Should().BeTrue();
			_collections[ 0 ].Count.Should().Be( 1 );
		}
	}
}
