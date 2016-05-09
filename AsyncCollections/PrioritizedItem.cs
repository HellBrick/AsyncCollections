using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public struct PrioritizedItem<T> : IEquatable<PrioritizedItem<T>>
	{
		public PrioritizedItem( T item, int priority )
		{
			Item = item;
			Priority = priority;
		}

		public T Item { get; }
		public int Priority { get; }

		public override string ToString() => $"{nameof( Item )}: {Item}, {nameof( Priority )}: {Priority}";

		public override int GetHashCode()
		{
			unchecked
			{
				const int prime = -1521134295;
				int hash = 12345701;
				hash = hash * prime + EqualityComparer<T>.Default.GetHashCode( Item );
				hash = hash * prime + EqualityComparer<int>.Default.GetHashCode( Priority );
				return hash;
			}
		}

		public bool Equals( PrioritizedItem<T> other ) => EqualityComparer<T>.Default.Equals( Item, other.Item ) && EqualityComparer<int>.Default.Equals( Priority, other.Priority );
		public override bool Equals( object obj ) => obj is PrioritizedItem<T> && Equals( (PrioritizedItem<T>) obj );

		public static bool operator ==( PrioritizedItem<T> x, PrioritizedItem<T> y ) => x.Equals( y );
		public static bool operator !=( PrioritizedItem<T> x, PrioritizedItem<T> y ) => !x.Equals( y );
	}
}
