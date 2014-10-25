using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents an item retrieved from one of the asynchronous collections.
	/// </summary>
	public struct AnyResult<T>
	{
		private T _value;
		private int _collectionIndex;

		public AnyResult( T value, int collectionIndex )
		{
			_value = value;
			_collectionIndex = collectionIndex;
		}

		/// <summary>
		/// Gets the item retrieved from a collection.
		/// </summary>
		public T Value
		{
			get { return _value; }
		}

		/// <summary>
		/// Gets the index of the collection the item was retrieved from.
		/// </summary>
		public int CollectionIndex
		{
			get { return _collectionIndex; }
		}
	}
}
