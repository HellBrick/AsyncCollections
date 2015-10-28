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
		public AnyResult( T value, int collectionIndex )
		{
			Value = value;
			CollectionIndex = collectionIndex;
		}

		/// <summary>
		/// Gets the item retrieved from a collection.
		/// </summary>
		public T Value { get; }

		/// <summary>
		/// Gets the index of the collection the item was retrieved from.
		/// </summary>
		public int CollectionIndex { get; }
	}
}
