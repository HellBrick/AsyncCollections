using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe collection that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the collection.</typeparam>
	public class AsyncCollection<T>: IAsyncCollection<T>
	{
		private IProducerConsumerCollection<T> _itemQueue;
		private ConcurrentQueue<TaskCompletionSource<T>> _awaiterQueue = new ConcurrentQueue<TaskCompletionSource<T>>();

		//	_queueBalance < 0 means there are free awaiters and not enough items.
		//	_queueBalance > 0 means the opposite is true.
		private long _queueBalance = 0;

		protected AsyncCollection( IProducerConsumerCollection<T> itemQueue )
		{
			_itemQueue = itemQueue;
			_queueBalance = _itemQueue.Count;
		}

		#region IAsyncCollection<T> members

		/// <summary>
		/// Gets an amount of pending item requests.
		/// </summary>
		public int AwaiterCount
		{
			get { return _awaiterQueue.Count; }
		}

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

				TaskCompletionSource<T> awaiter;

				while ( !_awaiterQueue.TryDequeue( out awaiter ) )
					spin.SpinOnce();

				//	Returns false if the cancellation occurred earlier.
				return awaiter.TrySetResult( item );
			}
		}

		/// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
		public Task<T> TakeAsync( CancellationToken cancellationToken )
		{
			long balanceAfterCurrentAwaiter = Interlocked.Decrement( ref _queueBalance );

			if ( balanceAfterCurrentAwaiter < 0 )
			{
				//	Awaiters are dominating, so we can safely add a new awaiter to the queue.

				var taskSource = new TaskCompletionSource<T>();
				_awaiterQueue.Enqueue( taskSource );

				cancellationToken.Register(
					state =>
					{
						//	It's enough to call TrySetCancelled() here.
						//	The balance correction will be taken care of in the Add() method that will retreive the current awaiter from the queue.
						TaskCompletionSource<T> awaiter = state as TaskCompletionSource<T>;
						awaiter.TrySetCanceled();
					},
					taskSource,
					useSynchronizationContext : false );

				return taskSource.Task;
			}
			else
			{
				//	There's at least one item available or being added, so we're returning it directly.

				T item;
				SpinWait spin = new SpinWait();

				while ( !_itemQueue.TryTake( out item ) )
					spin.SpinOnce();

				return Task.FromResult( item );
			}
		}

		public override string ToString()
		{
			return String.Format( "Count = {0}, Awaiters = {1}", Count, AwaiterCount );
		}

		#endregion

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			return _itemQueue.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return _itemQueue.GetEnumerator();
		}

		#endregion

		#region ICollection Members

		public int Count
		{
			get { return _itemQueue.Count; }
		}

		public void CopyTo( Array array, int index )
		{
			( _itemQueue as System.Collections.ICollection ).CopyTo( array, index );
		}

		bool System.Collections.ICollection.IsSynchronized
		{
			get { return false; }
		}

		object System.Collections.ICollection.SyncRoot
		{
			get { throw new NotSupportedException(); }
		}

		#endregion
	}

	[Obsolete( "AsyncCollection<T> should be used directly instead." )]
	public class AsyncCollection<TItem, TItemQueue>: AsyncCollection<TItem>
		where TItemQueue: IProducerConsumerCollection<TItem>, new()
	{
		public AsyncCollection()
			: base( new TItemQueue() )
		{
		}
	}
}
