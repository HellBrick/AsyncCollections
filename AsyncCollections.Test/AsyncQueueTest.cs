using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace HellBrick.Collections.Test
{
	public class AsyncQueueTest : AsyncCollectionTest<AsyncQueue<int>>
	{
		private const int _itemsToOverflowSegment = AsyncQueue<int>.SegmentSize + 1;

		protected override AsyncQueue<int> CreateCollection() => new AsyncQueue<int>();

		[Theory]
		[InlineData( 0, 0 )]
		[InlineData( 5, 3 )]
		[InlineData( 3, 5 )]
		[InlineData( 7, 7 )]
		[InlineData( AsyncQueue<int>.SegmentSize, 1 )]
		[InlineData( 1, AsyncQueue<int>.SegmentSize )]
		[InlineData( _itemsToOverflowSegment, 1 )]
		[InlineData( 1, _itemsToOverflowSegment )]
		[InlineData( _itemsToOverflowSegment * 2, 1 )]
		[InlineData( 1, _itemsToOverflowSegment * 2 )]
		public void CountsAreCorrect( int itemsInserted, int awaitersInserted )
		{
			InsertItems( Enumerable.Range( 0, itemsInserted ).ToArray() );
			InsertAwaiters( awaitersInserted );

			int itemAwaiterBalance = itemsInserted - awaitersInserted;

			Collection.Count.Should().Be( Math.Max( 0, itemAwaiterBalance ) );
			Collection.AwaiterCount.Should().Be( Math.Max( 0, -1 * itemAwaiterBalance ) );
		}

		[Fact]
		public void CountsAreCorrectIfItemTailLagsBehind()
		{
			InsertAwaiters( _itemsToOverflowSegment );

			Collection.Count.Should().Be( 0 );
			Collection.AwaiterCount.Should().Be( _itemsToOverflowSegment );

			InsertItems( Enumerable.Range( 0, 1 ).ToArray() );

			Collection.Count.Should().Be( 0 );
			Collection.AwaiterCount.Should().Be( _itemsToOverflowSegment - 1 );
		}

		[Fact]
		public void CountsAreCorrectIfAwaiterTailLagsBehind()
		{
			InsertItems( Enumerable.Range( 0, _itemsToOverflowSegment ).ToArray() );

			Collection.AwaiterCount.Should().Be( 0 );
			Collection.Count.Should().Be( _itemsToOverflowSegment );

			InsertAwaiters( 1 );

			Collection.AwaiterCount.Should().Be( 0 );
			Collection.Count.Should().Be( _itemsToOverflowSegment - 1 );
		}

		[Fact]
		public void CountsAreCorrectIfTailsMatch()
		{
			InsertAwaiters( _itemsToOverflowSegment + 1 );
			InsertItems( Enumerable.Range( 0, _itemsToOverflowSegment ).ToArray() );

			Collection.Count.Should().Be( 0 );
			Collection.AwaiterCount.Should().Be( 1 );

			Collection.Add( 42 );

			Collection.Count.Should().Be( 0 );
			Collection.AwaiterCount.Should().Be( 0 );

			Collection.Add( 64 );

			Collection.Count.Should().Be( 1 );
			Collection.AwaiterCount.Should().Be( 0 );
		}

		[Theory]
		[InlineData( Order.ItemsFirst )]
		[InlineData( Order.AwaitersFirst )]
		public async Task EverythingWorksIfSegmentIsFilledByOneKindOfItems( Order insertionOrder )
		{
			int[] items = Enumerable.Range( 0, _itemsToOverflowSegment ).ToArray();
			ValueTask<int>[] tasks = null;

			switch ( insertionOrder )
			{
				case Order.ItemsFirst:
					InsertItems( items );
					tasks = InsertAwaiters( items.Length );
					break;

				case Order.AwaitersFirst:
					tasks = InsertAwaiters( items.Length );
					InsertItems( items );
					break;
			}

			tasks.Should().OnlyContain( t => t.IsCompleted );
			int[] values = await Task.WhenAll( tasks.Select( t => t.AsTask() ) ).ConfigureAwait( true );
			values.Should().BeEquivalentTo( items ).And.BeInAscendingOrder();
		}

		[Fact]
		public void EnumeratorReturnsItemsInCorrectOrder()
		{
			int[] items = Enumerable.Range( 0, _itemsToOverflowSegment * 2 ).ToArray();
			InsertItems( items );
			Collection.Should().BeEquivalentTo( items ).And.BeInAscendingOrder();
		}

		[Fact]
		public void EnumeratorDoesNotReturnItemsThatHaveBeenRemovedBetweenMoveNextCalls()
		{
			InsertItems( 1, 2, 3 );
			using ( var enumerator = Collection.GetEnumerator() )
			{
				enumerator.MoveNext().Should().BeTrue();
				enumerator.Current.Should().Be( 1 );

				InsertAwaiters( 2 );

				enumerator.MoveNext().Should().BeTrue();
				enumerator.Current.Should().Be( 3 );
			}
		}

		private ValueTask<int>[] InsertAwaiters( int awaiterCount ) => Enumerable.Repeat( 0, awaiterCount ).Select( _ => Collection.TakeAsync() ).ToArray();

		private void InsertItems( params int[] items )
		{
			foreach ( int item in items )
				Collection.Add( item );
		}

		public enum Order
		{
			ItemsFirst,
			AwaitersFirst
		}
	}
}
