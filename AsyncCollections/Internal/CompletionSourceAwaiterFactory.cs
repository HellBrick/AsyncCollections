using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal struct CompletionSourceAwaiterFactory<T> : IAwaiterFactory<T>, IEquatable<CompletionSourceAwaiterFactory<T>>
	{
		private readonly CancellationToken _cancellationToken;

		public CompletionSourceAwaiterFactory( CancellationToken cancellationToken )
		{
			_cancellationToken = cancellationToken;
		}

		public IAwaiter<T> CreateAwaiter() => new CompletionSourceAwaiter( _cancellationToken );

		/// <summary>
		/// A simple <see cref="TaskCompletionSource{T}"/> wrapper that implements <see cref="IAwaiter{T}"/>.
		/// </summary>
		private class CompletionSourceAwaiter : IAwaiter<T>
		{
			private readonly TaskCompletionSource<T> _completionSource;
			private readonly Task<T> _task;

			public CompletionSourceAwaiter( CancellationToken cancellationToken )
			{
				_completionSource = new TaskCompletionSource<T>();
				_task = _completionSource.Task.WithYield();

				cancellationToken.Register(
					state =>
					{
						TaskCompletionSource<T> awaiter = state as TaskCompletionSource<T>;
						awaiter.TrySetCanceled();
					},
					_completionSource,
					useSynchronizationContext: false );
			}

			#region IAwaiter<T> Members

			public bool TrySetResult( T result )
			{
				return _completionSource.TrySetResult( result );
			}

			public Task<T> Task
			{
				get { return _task; }
			}

			#endregion
		}

		#region IEquatable<CompletionSourceAwaiterFactory<T>>

		public override int GetHashCode() => EqualityComparer<CancellationToken>.Default.GetHashCode( _cancellationToken );
		public bool Equals( CompletionSourceAwaiterFactory<T> other ) => _cancellationToken == other._cancellationToken;
		public override bool Equals( object obj ) => obj is CompletionSourceAwaiterFactory<T> && Equals( (CompletionSourceAwaiterFactory<T>) obj );

		public static bool operator ==( CompletionSourceAwaiterFactory<T> x, CompletionSourceAwaiterFactory<T> y ) => x.Equals( y );
		public static bool operator !=( CompletionSourceAwaiterFactory<T> x, CompletionSourceAwaiterFactory<T> y ) => !x.Equals( y );

		#endregion
	}
}
