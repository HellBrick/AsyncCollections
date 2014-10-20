using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public struct AnyResult<T>
	{
		public AnyResult( T result, int collectionIndex )
		{
			_result = result;
			_collectionIndex = collectionIndex;
		}

		private T _result;
		public T Result
		{
			get { return _result; }
		}

		private int _collectionIndex;
		public int CollectionIndex
		{
			get { return _collectionIndex; }
		}
	}
}
