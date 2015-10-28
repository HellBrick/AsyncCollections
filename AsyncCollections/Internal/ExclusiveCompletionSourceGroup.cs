using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	/// <summary>
	/// A set of exclusive awaiters that allows only one of the awaiters to be completed.
	/// </summary>
	internal class ExclusiveCompletionSourceGroup<T>
	{
		private int _completedSource = State.Locked;
		private readonly TaskCompletionSource<AnyResult<T>> _realCompetionSource = new TaskCompletionSource<AnyResult<T>>();
		private readonly Task<AnyResult<T>> _task;
		private BitVector32 _awaitersCreated = new BitVector32();

		public ExclusiveCompletionSourceGroup()
		{
			_task = _realCompetionSource.Task.WithYield();
		}

		public Task<AnyResult<T>> Task
		{
			get { return _task; }
		}

		public bool IsAwaiterCreated( int index ) => _awaitersCreated[ BitVector32.CreateMask( index ) ];

		public Factory CreateAwaiterFactory( int index ) => new Factory( this, index );

		private IAwaiter<T> CreateAwaiter( int index )
		{
			int mask = BitVector32.CreateMask( index );
			_awaitersCreated[ mask ] = true;
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
			Interlocked.MemoryBarrier();

			cancellationToken.Register(
				state =>
				{
					ExclusiveCompletionSourceGroup<T> group = state as ExclusiveCompletionSourceGroup<T>;
					if ( Interlocked.CompareExchange( ref group._completedSource, State.Canceled, State.Unlocked ) == State.Unlocked )
						group._realCompetionSource.SetCanceled();
				},
				this,
				useSynchronizationContext: false );
		}
		private static class State
		{
			public const int Locked = -1;
			public const int Unlocked = -2;
			public const int Canceled = Int32.MinValue;
		}

		private class ExclusiveCompletionSource : IAwaiter<T>
		{
			private readonly ExclusiveCompletionSourceGroup<T> _group;
			private readonly int _id;

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
						_group._realCompetionSource.SetResult( new AnyResult<T>( result, _id ) );
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

		public struct Factory : IAwaiterFactory<T>, IEquatable<Factory>
		{
			private readonly ExclusiveCompletionSourceGroup<T> _group;
			private readonly int _index;

			public Factory( ExclusiveCompletionSourceGroup<T> group, int index )
			{
				_group = group;
				_index = index;
			}

			public IAwaiter<T> CreateAwaiter() => _group.CreateAwaiter( _index );

			#region IEquatable<Factory>

			public override int GetHashCode()
			{
				unchecked
				{
					const int prime = -1521134295;
					int hash = 12345701;
					hash = hash * prime + EqualityComparer<ExclusiveCompletionSourceGroup<T>>.Default.GetHashCode( _group );
					hash = hash * prime + EqualityComparer<int>.Default.GetHashCode( _index );
					return hash;
				}
			}

			public bool Equals( Factory other ) => EqualityComparer<ExclusiveCompletionSourceGroup<T>>.Default.Equals( _group, other._group ) && EqualityComparer<int>.Default.Equals( _index, other._index );
			public override bool Equals( object obj ) => obj is Factory && Equals( (Factory) obj );

			public static bool operator ==( Factory x, Factory y ) => x.Equals( y );
			public static bool operator !=( Factory x, Factory y ) => !x.Equals( y );

			#endregion
		}
	}
}