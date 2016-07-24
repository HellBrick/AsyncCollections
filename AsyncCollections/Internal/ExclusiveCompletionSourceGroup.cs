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
		private BitArray32 _awaitersCreated = BitArray32.Empty;
		private CancellationRegistrationHolder _cancellationRegistrationHolder;

		public ExclusiveCompletionSourceGroup()
		{
			Task = _realCompetionSource.Task.WithYield();
		}

		public Task<AnyResult<T>> Task { get; }

		public bool IsAwaiterCreated( int index ) => _awaitersCreated.IsBitSet( index );
		public Factory CreateAwaiterFactory( int index ) => new Factory( this, index );

		private IAwaiter<T> CreateAwaiter( int index )
		{
			_awaitersCreated = _awaitersCreated.WithBitSet( index );
			return new ExclusiveCompletionSource( this, index );
		}

		public void MarkAsResolved() => Interlocked.CompareExchange( ref _completedSource, State.Canceled, State.Unlocked );

		public void UnlockCompetition( CancellationToken cancellationToken )
		{
			CancellationTokenRegistration registration = cancellationToken
				.Register
				(
					state =>
					{
						ExclusiveCompletionSourceGroup<T> group = state as ExclusiveCompletionSourceGroup<T>;

						/// There are 2 cases here.
						/// 
						/// #1: The token is canceled before <see cref="UnlockCompetition(CancellationToken)"/> is called, but after the token is validated higher up the stack.
						/// Is this is the case, the cancellation callbak will be called synchronously while <see cref="_completedSource"/> is still set to <see cref="State.Locked"/>.
						/// So the competition will never progress to <see cref="State.Unlocked"/> and we have to check for this explicitly.
						/// 
						/// #2: We're canceled after the competition has been unlocked.
						/// If this is the case, we have a simple race against the awaiters to progress from <see cref="State.Unlocked"/> to <see cref="State.Canceled"/>.
						if ( group.TryTransitionToCanceledIfStateIs( State.Locked ) || group.TryTransitionToCanceledIfStateIs( State.Unlocked ) )
							group._realCompetionSource.SetCanceled();
					},
					this,
					useSynchronizationContext: false
				);

			// We can't do volatile reads/writes on a custom value type field, so we have to wrap the registration into a holder instance.
			// But there's no point in allocating the wrapper if the token can never be canceled.
			if ( cancellationToken.CanBeCanceled )
				Volatile.Write( ref _cancellationRegistrationHolder, new CancellationRegistrationHolder( registration ) );

			// If the cancellation was processed synchronously, the state will already be set to Canceled and we must *NOT* unlock the competition.
			Interlocked.CompareExchange( ref _completedSource, State.Unlocked, State.Locked );
		}

		private bool TryTransitionToCanceledIfStateIs( int requiredState ) => Interlocked.CompareExchange( ref _completedSource, State.Canceled, requiredState ) == requiredState;

		private static class State
		{
			public const int Locked = -1;
			public const int Unlocked = -2;
			public const int Canceled = Int32.MinValue;
		}

		private class CancellationRegistrationHolder
		{
			public CancellationRegistrationHolder( CancellationTokenRegistration registration )
			{
				Registration = registration;
			}

			public CancellationTokenRegistration Registration { get; }
		}

		private class ExclusiveCompletionSource : IAwaiter<T>
		{
			private static readonly ValueTask<T> _neverEndingTask = new ValueTask<T>( new TaskCompletionSource<T>().Task );
			private readonly ExclusiveCompletionSourceGroup<T> _group;
			private readonly int _id;

			public ExclusiveCompletionSource( ExclusiveCompletionSourceGroup<T> group, int id )
			{
				_group = group;
				_id = id;
			}

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

						//	This also means we're the ones responsible for disposing the cancellation registration.
						//	It's important to remember the holder can be null if the token is non-cancellable.
						Volatile.Read( ref _group._cancellationRegistrationHolder )?.Registration.Dispose();
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

			//	The value will never be actually used.
			public ValueTask<T> Task => _neverEndingTask;
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