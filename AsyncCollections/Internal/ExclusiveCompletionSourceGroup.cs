using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	class ExclusiveCompletionSourceGroup<T>
	{
		private int _completedSource = State.Locked;
		private TaskCompletionSource<T> _realCompetionSource = new TaskCompletionSource<T>();

		public int CompletedSourceIndex
		{
			get { return _completedSource; }
		}

		public Task<T> Task
		{
			get { return _realCompetionSource.Task; }
		}

		public IAwaiter<T> CreateAwaiter( int index )
		{
			return new ExclusiveCompletionSource( this, index );
		}

		public void MarkAsResolved()
		{
			Interlocked.CompareExchange( ref _completedSource, State.Canceled, State.Unlocked );
		}

		public void UnlockCompetition( CancellationToken cancellationToken )
		{
			_completedSource = State.Unlocked;

			//	The full fence prevents cancellation callback registration (and therefore the cancellation) from being executed before the competition is unlocked.
			Thread.MemoryBarrier();

			cancellationToken.Register(
				state =>
				{
					ExclusiveCompletionSourceGroup<T> group = state as ExclusiveCompletionSourceGroup<T>;
					if ( Interlocked.CompareExchange( ref group._completedSource, State.Canceled, State.Unlocked ) == State.Unlocked )
						_realCompetionSource.SetCanceled();
				},
				this,
				useSynchronizationContext : false );
		}
		private static class State
		{
			public const int Locked = -1;
			public const int Unlocked = -2;
			public const int Canceled = Int32.MinValue;
		}

		private class ExclusiveCompletionSource: IAwaiter<T>
		{
			private ExclusiveCompletionSourceGroup<T> _group;
			private int _id;

			public ExclusiveCompletionSource( ExclusiveCompletionSourceGroup<T> group, int id )
			{
				_group = group;
				_id = id;
			}

			#region IAwaiter<T> Members

			public bool TrySetResult( T result )
			{
				SpinWait spin = new SpinWait();

				while ( true )
				{
					int completedSource = Interlocked.CompareExchange( ref _group._completedSource, _id, State.Unlocked );

					if ( completedSource == State.Unlocked )
					{
						//	We are the champions!
						_group._realCompetionSource.SetResult( result );
						return true;
					}

					if ( completedSource == State.Locked )
					{
						//	The competition has not started yet.
						spin.SpinOnce();
						continue;
					}

					//	Everything else means we've lost the competition and another completion source has got the result
					return false;
				}
			}

			public Task<T> Task
			{
				//	The value will never be actually used.
				get { return null; }
			}

			#endregion
		}
	}
}