using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	/// <summary>
	/// A simple <see cref="TaskCompletionSource{T}"/> wrapper that implements <see cref="IAwaiter{T}"/>.
	/// </summary>
	class CompletionSourceAwaiter<T>: IAwaiter<T>
	{
		private TaskCompletionSource<T> _completionSource;

		public CompletionSourceAwaiter( CancellationToken cancellationToken )
		{
			_completionSource = new TaskCompletionSource<T>();

			cancellationToken.Register(
				state =>
				{
					TaskCompletionSource<T> awaiter = state as TaskCompletionSource<T>;
					awaiter.TrySetCanceled();
				},
				_completionSource,
				useSynchronizationContext : false );
		}

		#region IAwaiter<T> Members

		public bool TrySetResult( T result )
		{
			return _completionSource.TrySetResult( result );
		}

		public Task<T> Task
		{
			get { return _completionSource.Task; }
		}

		#endregion
	}
}
