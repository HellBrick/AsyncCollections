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
	public struct AnyResult<T> : IEquatable<AnyResult<T>>
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

		public override int GetHashCode()
		{
			unchecked
			{
				const int prime = -1521134295;
				int hash = 12345701;
				hash = hash * prime + EqualityComparer<T>.Default.GetHashCode( Value );
				hash = hash * prime + EqualityComparer<int>.Default.GetHashCode( CollectionIndex );
				return hash;
			}
		}

		public bool Equals( AnyResult<T> other ) => EqualityComparer<T>.Default.Equals( Value, other.Value ) && EqualityComparer<int>.Default.Equals( CollectionIndex, other.CollectionIndex );
		public override bool Equals( object obj ) => obj is AnyResult<T> && Equals( (AnyResult<T>) obj );

		public static bool operator ==( AnyResult<T> x, AnyResult<T> y ) => x.Equals( y );
		public static bool operator !=( AnyResult<T> x, AnyResult<T> y ) => !x.Equals( y );
	}
}
