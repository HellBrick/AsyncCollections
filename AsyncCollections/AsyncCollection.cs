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
	/// <typeparam name="TItem">The type of the items contained in the collection.</typeparam>
	/// <typeparam name="TItemQueue">The type of the producer/consumer collection to use as an internal item storage.</typeparam>
	public class AsyncCollection<TItem>: IAsyncCollection<TItem>
	{
		private IProducerConsumerCollection<TItem> _itemQueue;
        private ConcurrentQueue<ItemConsumer<TItem>> _awaiterQueue = new ConcurrentQueue<ItemConsumer<TItem>>();

        protected AsyncCollection(IProducerConsumerCollection<TItem> itemQueue)
        {
            _itemQueue = itemQueue;
        }

		//	_queueBalance < 0 means there are free awaiters and not enough items.
		//	_queueBalance > 0 means the opposite is true.
		private long _queueBalance = 0;

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
		public void Add( TItem item )
		{
			while ( !TryAdd( item ) ) ;
		}

		/// <summary>
		/// Tries to add an item to the collection.
		/// May fail if an awaiter that's supposed to receive the item is cancelled. If this is the case, the TryAdd() method must be called again.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		/// <returns>True if the item was added to the collection; false if the awaiter was cancelled and the operation must be retried.</returns>
		private bool TryAdd( TItem item )
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

                ItemConsumer<TItem> awaiter;

				while ( !_awaiterQueue.TryDequeue( out awaiter ) )
					spin.SpinOnce();

				//	Returns false if the cancellation occurred earlier.
                return awaiter(item);
			}
		}

		/// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
        public void TakeAsync(ItemConsumer<TItem> consumer)
		{
			long balanceAfterCurrentAwaiter = Interlocked.Decrement( ref _queueBalance );

			if ( balanceAfterCurrentAwaiter < 0 )
			{
				//	Awaiters are dominating, so we can safely add a new awaiter to the queue.
                _awaiterQueue.Enqueue( consumer );
			}
			else
			{
				//	There's at least one item available or being added, so we're returning it directly.
				TItem item;
				SpinWait spin = new SpinWait();
				while ( !_itemQueue.TryTake( out item ) )
					spin.SpinOnce();

                if (!consumer(item))
                    throw new InvalidOperationException("In TakeAsync, item must be consumed");
			}
		}

		public override string ToString()
		{
			return String.Format( "Count = {0}, Awaiters = {1}", Count, AwaiterCount );
		}

		#endregion

		#region IEnumerable<T> Members

		public IEnumerator<TItem> GetEnumerator()
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
}
