using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal interface IAwaiterFactory<T>
	{
		IAwaiter<T> CreateAwaiter();
	}

	internal struct InstanceAwaiterFactory<T> : IAwaiterFactory<T>, IEquatable<InstanceAwaiterFactory<T>>
	{
		private readonly IAwaiter<T> _awaiter;

		public InstanceAwaiterFactory( IAwaiter<T> awaiter )
		{
			_awaiter = awaiter;
		}

		public IAwaiter<T> CreateAwaiter() => _awaiter;

		#region IEquatable<InstanceAwaiterFactory<T>>

		public override int GetHashCode() => EqualityComparer<IAwaiter<T>>.Default.GetHashCode( _awaiter );
		public bool Equals( InstanceAwaiterFactory<T> other ) => EqualityComparer<IAwaiter<T>>.Default.Equals( _awaiter, other._awaiter );
		public override bool Equals( object obj ) => obj is InstanceAwaiterFactory<T> && Equals( (InstanceAwaiterFactory<T>) obj );

		public static bool operator ==( InstanceAwaiterFactory<T> x, InstanceAwaiterFactory<T> y ) => x.Equals( y );
		public static bool operator !=( InstanceAwaiterFactory<T> x, InstanceAwaiterFactory<T> y ) => !x.Equals( y );

		#endregion
	}
}
