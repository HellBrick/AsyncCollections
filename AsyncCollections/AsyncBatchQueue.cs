using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe collection that groups the items added to it into batches and allows consuming them asynchronously.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the collection.</typeparam>
	public class AsyncBatchQueue<T> : IAsyncBatchCollection<T>
	{
		private volatile Batch _currentBatch;
		private readonly AsyncQueue<IReadOnlyList<T>> _batchQueue = new AsyncQueue<IReadOnlyList<T>>();

		/// <summary>
		/// Initializes a new instance of <see cref="AsyncBatchQueue"/> that produces batches of a specified size.
		/// </summary>
		/// <param name="batchSize">Amount of the items contained in an output batch.</param>
		public AsyncBatchQueue( int batchSize )
		{
			if ( batchSize <= 0 )
				throw new ArgumentOutOfRangeException( "batchSize", batchSize, "Batch size must be a positive integer." );

			BatchSize = batchSize;
			_currentBatch = new Batch( this );
		}

		/// <summary>
		/// Gets amount of items contained in an output batch.
		/// </summary>
		public int BatchSize { get; }

		/// <summary>
		/// Gets the number of flushed batches currently available for consuming.
		/// </summary>
		public int Count => _batchQueue.Count;

		/// <summary>
		/// Adds an item to the collection. Flushes the new batch to be available for consuming if amount of the pending items has reached <see cref="BatchSize"/>.
		/// </summary>
		/// <param name="item"></param>
		public void Add( T item )
		{
			SpinWait spin = new SpinWait();

			while ( !_currentBatch.TryAdd( item ) )
				spin.SpinOnce();
		}

		/// <summary>
		/// Removes and returns a batch from the collection in an asynchronous manner.
		/// </summary>
		public ValueTask<IReadOnlyList<T>> TakeAsync() => TakeAsync( CancellationToken.None );

		/// <summary>
		/// Removes and returns a batch from the collection in an asynchronous manner.
		/// </summary>
		public ValueTask<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken ) => _batchQueue.TakeAsync( cancellationToken );

		/// <summary>
		/// <para>Forces a new batch to be created and made available for consuming even if amount of the pending items has not reached <see cref="BatchSize"/> yet.</para>
		/// <para>Does nothing if there are no pending items to flush.</para>
		/// </summary>
		public void Flush()
		{
			SpinWait spin = new SpinWait();
			while ( !_currentBatch.TryFlush() )
				spin.SpinOnce();
		}

		public IEnumerator<IReadOnlyList<T>> GetEnumerator() => _batchQueue.GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		private class Batch : IReadOnlyList<T>
		{
			private readonly AsyncBatchQueue<T> _queue;
			private readonly T[] _items;
			private readonly bool[] _finalizationFlags;
			private int _lastReservationIndex = -1;
			private int _count = -1;

			public Batch( AsyncBatchQueue<T> queue )
			{
				_queue = queue;
				_items = new T[ _queue.BatchSize ];
				_finalizationFlags = new bool[ _queue.BatchSize ];
			}

			public bool TryAdd( T item )
			{
				int index = Interlocked.Increment( ref _lastReservationIndex );

				//	The following is true if someone has beaten us to the last slot and we have to wait until the next batch comes along.
				if ( index >= _queue.BatchSize )
					return false;

				//	The following is true if we've taken the last slot, which means we're obligated to flush the current batch and create a new one.
				if ( index == _queue.BatchSize - 1 )
					FlushInternal( _queue.BatchSize );

				//	The full fence prevents setting finalization flag before the actual item value is written.
				_items[ index ] = item;
				Interlocked.MemoryBarrier();
				_finalizationFlags[ index ] = true;

				return true;
			}

			public bool TryFlush()
			{
				int expectedPreviousReservation = Volatile.Read( ref _lastReservationIndex );

				//	We don't flush if the batch doesn't have any items or if another thread is about to flush it.
				//	However, we report success to avoid unnecessary spinning.
				if ( expectedPreviousReservation < 0 || expectedPreviousReservation >= _queue.BatchSize - 1 )
					return true;

				int previousReservation = Interlocked.CompareExchange( ref _lastReservationIndex, _queue.BatchSize, expectedPreviousReservation );

				//	Flush reservation has succeeded.
				if ( expectedPreviousReservation == previousReservation )
				{
					FlushInternal( previousReservation + 1 );
					return true;
				}

				//	The following is true if someone has completed the batch by the time we tried to flush it.
				//	Therefore the batch will be flushed anyway even if we don't do anything.
				//	The opposite means someone has slipped in an update and we have to spin.
				return previousReservation >= _queue.BatchSize;
			}

			private void FlushInternal( int count )
			{
				_count = count;
				_queue._currentBatch = new Batch( _queue );

				//	The full fence ensures that the current batch will never be added to the queue before _count is set.
				Interlocked.MemoryBarrier();

				_queue._batchQueue.Add( this );
			}

			private T GetItemWithoutValidation( int index )
			{
				SpinWait spin = new SpinWait();
				while ( !_finalizationFlags[ index ] )
				{
					spin.SpinOnce();

					//	The full fence prevents caching any part of _finalizationFlags[ index ] expression.
					Interlocked.MemoryBarrier();
				}

				//	The full fence prevents reading item value before finalization flag is set.
				Interlocked.MemoryBarrier();
				return _items[ index ];
			}

			public T this[ int index ]
			{
				get
				{
					if ( index >= Count )
						throw new IndexOutOfRangeException();

					return GetItemWithoutValidation( index );
				}
			}

			public int Count => _count;

			public IEnumerator<T> GetEnumerator()
			{
				for ( int i = 0; i < Count; i++ )
					yield return GetItemWithoutValidation( i );
			}

			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
		}
	}
}