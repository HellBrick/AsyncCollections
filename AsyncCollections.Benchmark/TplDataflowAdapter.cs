using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	class TplDataflowAdapter<T>: IAsyncCollection<T>
	{
		private readonly BufferBlock<T> buffer = new BufferBlock<T>();

		public int AwaiterCount
		{
			get { throw new NotSupportedException(); }
		}

		public void Add( T item )
		{
			buffer.Post( item );
		}

		public Task<T> TakeAsync( CancellationToken cancellationToken )
		{
			return buffer.ReceiveAsync( cancellationToken );
		}

		public IEnumerator<T> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		public int Count
		{
			get { throw new NotImplementedException(); }
		}
	}
}
