using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HellBrick.Collections.Internal;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe collection that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the collection.</typeparam>
	public class AsyncCollection<T> : IAsyncCollection<T>
	{
		private readonly IProducerConsumerCollection<T> _itemQueue;
		private readonly ConcurrentQueue<IAwaiter<T>> _awaiterQueue = new ConcurrentQueue<IAwaiter<T>>();

		//	_queueBalance < 0 means there are free awaiters and not enough items.
		//	_queueBalance > 0 means the opposite is true.
		private long _queueBalance = 0;

		/// <summary>
		/// Initializes a new instance of <see cref="AsyncCollection"/> with a specified <see cref="IProducerConsumerCollection{T}"/> as an underlying item storage.
		/// </summary>
		/// <param name="itemQueue">The collection to use as an underlying item storage. MUST NOT be accessed elsewhere.</param>
		public AsyncCollection( IProducerConsumerCollection<T> itemQueue )
		{
			_itemQueue = itemQueue;
			_queueBalance = _itemQueue.Count;
		}

		public int Count => _itemQueue.Count;

		/// <summary>
		/// Gets an amount of pending item requests.
		/// </summary>
		public int AwaiterCount => _awaiterQueue.Count;

		/// <summary>
		/// Adds an item to the collection.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void Add( T item )
		{
			while ( !TryAdd( item ) ) ;
		}

		/// <summary>
		/// Tries to add an item to the collection.
		/// May fail if an awaiter that's supposed to receive the item is cancelled. If this is the case, the TryAdd() method must be called again.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		/// <returns>True if the item was added to the collection; false if the awaiter was cancelled and the operation must be retried.</returns>
		private bool TryAdd( T item )
		{
			long balanceAfterCurrentItem = Interlocked.Increment( ref _queueBalance );
			SpinWait spin = new SpinWait();

			if ( balanceAfterCurrentItem > 0 )
			{
				//	Items are dominating, so we can safely add a new item to the queue.
				while ( !_itemQueue.TryAdd( item ) )
					spin.SpinOnce();

				return true;
			}
			else
			{
				//	There's at least one awaiter available or being added as we're speaking, so we're giving the item to it.

				IAwaiter<T> awaiter;

				while ( !_awaiterQueue.TryDequeue( out awaiter ) )
					spin.SpinOnce();

				//	Returns false if the cancellation occurred earlier.
				return awaiter.TrySetResult( item );
			}
		}

		/// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
		public ValueTask<T> TakeAsync( CancellationToken cancellationToken )
			=> cancellationToken.IsCancellationRequested
			? CanceledValueTask<T>.Value
			: TakeAsync( new CompletionSourceAwaiterFactory<T>( cancellationToken ) );

		private ValueTask<T> TakeAsync<TAwaiterFactory>( TAwaiterFactory awaiterFactory ) where TAwaiterFactory : IAwaiterFactory<T>
		{
			long balanceAfterCurrentAwaiter = Interlocked.Decrement( ref _queueBalance );

			if ( balanceAfterCurrentAwaiter < 0 )
			{
				//	Awaiters are dominating, so we can safely add a new awaiter to the queue.
				IAwaiter<T> awaiter = awaiterFactory.CreateAwaiter();
				_awaiterQueue.Enqueue( awaiter );
				return awaiter.Task;
			}
			else
			{
				//	There's at least one item available or being added, so we're returning it directly.

				T item;
				SpinWait spin = new SpinWait();

				while ( !_itemQueue.TryTake( out item ) )
					spin.SpinOnce();

				return new ValueTask<T>( item );
			}
		}

		public override string ToString() => $"Count = {Count}, Awaiters = {AwaiterCount}";

		public IEnumerator<T> GetEnumerator() => _itemQueue.GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _itemQueue.GetEnumerator();

		#region Static

		internal const int TakeFromAnyMaxCollections = BitArray32.BitCapacity;

		/// <summary>
		/// Removes and returns an item from one of the specified collections in an asynchronous manner.
		/// </summary>
		public static ValueTask<AnyResult<T>> TakeFromAnyAsync( AsyncCollection<T>[] collections ) => TakeFromAnyAsync( collections, CancellationToken.None );

		/// <summary>
		/// Removes and returns an item from one of the specified collections in an asynchronous manner.
		/// </summary>
		public static ValueTask<AnyResult<T>> TakeFromAnyAsync( AsyncCollection<T>[] collections, CancellationToken cancellationToken )
		{
			if ( collections == null )
				throw new ArgumentNullException( "collections" );

			if ( collections.Length <= 0 || collections.Length > TakeFromAnyMaxCollections )
				throw new ArgumentException( String.Format( "The collection array can't contain less than 1 or more than {0} collections.", TakeFromAnyMaxCollections ), "collections" );

			if ( cancellationToken.IsCancellationRequested )
				return CanceledValueTask<AnyResult<T>>.Value;

			ExclusiveCompletionSourceGroup<T> exclusiveSources = new ExclusiveCompletionSourceGroup<T>();

			//	Fast route: we attempt to take from the top-priority queues that have any items.
			//	If the fast route succeeds, we avoid allocating and queueing a bunch of awaiters.
			for ( int i = 0; i < collections.Length; i++ )
			{
				if ( collections[ i ].Count > 0 )
				{
					AnyResult<T>? result = TryTakeFast( exclusiveSources, collections[ i ], i );
					if ( result.HasValue )
						return new ValueTask<AnyResult<T>>( result.Value );
				}
			}

			//	No luck during the fast route; just queue the rest of awaiters.
			for ( int i = 0; i < collections.Length; i++ )
			{
				AnyResult<T>? result = TryTakeFast( exclusiveSources, collections[ i ], i );
				if ( result.HasValue )
					return new ValueTask<AnyResult<T>>( result.Value );
			}

			//	None of the collections had any items. The order doesn't matter anymore, it's time to start the competition.
			exclusiveSources.UnlockCompetition( cancellationToken );
			return new ValueTask<AnyResult<T>>( exclusiveSources.Task );
		}

		private static AnyResult<T>? TryTakeFast( ExclusiveCompletionSourceGroup<T> exclusiveSources, AsyncCollection<T> collection, int index )
		{
			//	This can happen if the awaiter has already been created during the fast route.
			if ( exclusiveSources.IsAwaiterCreated( index ) )
				return null;

			ValueTask<T> collectionTask = collection.TakeAsync( exclusiveSources.CreateAwaiterFactory( index ) );

			//	One of the collections already had an item and returned it directly
			if ( collectionTask != null && collectionTask.IsCompleted )
			{
				exclusiveSources.MarkAsResolved();
				return new AnyResult<T>( collectionTask.Result, index );
			}
			else
				return null;
		}

		#endregion
	}
}
