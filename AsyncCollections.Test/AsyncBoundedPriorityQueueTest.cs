using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using HellBrick.Collections;
using Xunit;

namespace HellBrick.Collections.Test
{
	public class AsyncBoundedPriorityQueueTest: AsyncCollectionTest<AsyncBoundedPriorityQueue<int>>
	{
		protected override AsyncBoundedPriorityQueue<int> CreateCollection()
		{
			return new AsyncBoundedPriorityQueue<int>( 2 );
		}

		[Fact]
		public async Task ReturnsLowPriorityIfNoHighPriorityIsAvailable()
		{
			Collection.Add( 42, 1 );
			var result = await Collection.TakeAsync().ConfigureAwait( true );
			result.Should().Be( new PrioritizedItem<int>( 42, 1 ) );
		}

		[Fact]
		public async Task RespectsPriority()
		{
			Collection.Add( 42, 0 );
			Collection.Add( 999, 1 );

			var result = await Collection.TakeAsync().ConfigureAwait( true );
			result.Should().Be( new PrioritizedItem<int>( 42, 0 ) );
		}
	}
}
