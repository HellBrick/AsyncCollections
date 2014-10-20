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
		private const int _resolved = Int32.MaxValue;
		private const int _unlocked = -2;
		private const int _locked = -1;

		private int _completedSource = _locked;
		private TaskCompletionSource<T> _realCompetionSource = new TaskCompletionSource<T>();

		public ExclusiveCompletionSourceGroup( int count )
		{
			Sources = new ExclusiveCompletionSource[ count ];
			for ( int i = 0; i < count; i++ )
				Sources[ i ] = new ExclusiveCompletionSource( this, i );
		}

		public IAwaiter<T>[] Sources { get; private set; }

		public int CompletedSourceIndex
		{
			get { return _completedSource; }
		}

		public Task<T> Task
		{
			get { return _realCompetionSource.Task; }
		}

		public void MarkAsResolved()
		{
			Interlocked.CompareExchange( ref _completedSource, _resolved, _unlocked );
		}

		public void UnlockCompetition( CancellationToken cancellationToken )
		{
			_completedSource = _unlocked;
			
			//	The full fence prevents cancellation callback registration (and therefore the cancellation) from being executed before the competition is unlocked.
			Thread.MemoryBarrier();

			cancellationToken.Register(
				state => ( state as ExclusiveCompletionSourceGroup<T> ).TrySetCanceled(),
				this,
				useSynchronizationContext : false );
		}

		public bool TrySetCanceled()
		{
			bool success = Interlocked.CompareExchange( ref _completedSource, _resolved, _unlocked ) == _unlocked;
			if ( success )
				_realCompetionSource.SetCanceled();

			return success;
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
					int completedSource = Interlocked.CompareExchange( ref _group._completedSource, _id, _unlocked );

					if ( completedSource == _unlocked )
					{
						//	We are the champions!
						_group._realCompetionSource.SetResult( result );
						return true;
					}

					if ( completedSource == _locked )
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