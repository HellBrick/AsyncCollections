using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using HellBrick.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HellBrick.Collections.Test
{
	[TestClass]
	public class AsyncBoundedPriorityQueueTest: AsyncCollectionTest<AsyncBoundedPriorityQueue<int>>
	{
		protected override AsyncBoundedPriorityQueue<int> CreateCollection()
		{
			return new AsyncBoundedPriorityQueue<int>( 2 );
		}

		[TestMethod]
		public async Task ReturnsLowPriorityIfNoHighPriorityIsAvailable()
		{
			Collection.Add( 42, 1 );
			var result = await Collection.TakeAsync();
			result.Should().Be( 42 );
		}

		[TestMethod]
		public async Task RespectsPriority()
		{
			Collection.Add( 42, 0 );
			Collection.Add( 999, 1 );

			var result = await Collection.TakeAsync();
			result.Should().Be( 42 );
		}
	}
}
