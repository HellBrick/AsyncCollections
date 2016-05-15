using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	class BlockingCollectionAdapter<T>: IAsyncCollection<T>
	{
		private readonly BlockingCollection<T> _collection;

		public BlockingCollectionAdapter()
		{
			_collection = new BlockingCollection<T>();
		}

		#region IAsyncCollection<T> Members

		public int AwaiterCount
		{
			get { throw new NotImplementedException(); }
		}

		public void Add( T item )
		{
			_collection.Add( item );
		}

		public Task<T> TakeAsync( System.Threading.CancellationToken cancellationToken )
		{
			T item = _collection.Take( cancellationToken );
			return Task.FromResult( item );
		}

		#endregion

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		#endregion

		#region IReadOnlyCollection<T> Members

		public int Count
		{
			get { throw new NotImplementedException(); }
		}

		#endregion
	}
}
