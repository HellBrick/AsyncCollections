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
	public class AsyncBatchQueue<T>: IEnumerable<IReadOnlyList<T>>
	{
		private int _batchSize;
		private volatile Batch _currentBatch;
		private AsyncQueue<IReadOnlyList<T>> _batchQueue = new AsyncQueue<IReadOnlyList<T>>();

		#region Construction

		/// <summary>
		/// Initializes a new instance of <see cref="AsyncBatchQueue"/> that produces batches of a specified size.
		/// </summary>
		/// <param name="batchSize">Amount of the items contained in an output batch.</param>
		public AsyncBatchQueue( int batchSize )
		{
			if ( batchSize <= 0 )
				throw new ArgumentOutOfRangeException( "batchSize", batchSize, "Batch size must be a positive integer." );

			_batchSize = batchSize;
			_currentBatch = new Batch( this );
		}

		#endregion

		#region Public

		/// <summary>
		/// Gets amount of items contained in an output batch.
		/// </summary>
		public int BatchSize
		{
			get { return _batchSize; }
		}

		/// <summary>
		/// Gets the number of flushed batches currently available for consuming.
		/// </summary>
		public int Count
		{
			get { return _batchQueue.Count; }
		}

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
		public Task<IReadOnlyList<T>> TakeAsync()
		{
			return TakeAsync( CancellationToken.None );
		}

		/// <summary>
		/// Removes and returns a batch from the collection in an asynchronous manner.
		/// </summary>
		public Task<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken )
		{
			return _batchQueue.TakeAsync( cancellationToken );
		}

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

		#endregion

		#region IEnumerable<IReadOnlyList<T>> Members

		public IEnumerator<IReadOnlyList<T>> GetEnumerator()
		{
			return _batchQueue.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		#endregion

		private class Batch: IReadOnlyList<T>
		{
			private AsyncBatchQueue<T> _queue;
			private T[] _items;
			private bool[] _finalizationFlags;
			private int _lastReservationIndex = -1;
			private int _count = -1;

			public Batch( AsyncBatchQueue<T> queue )
			{
				_queue = queue;
				_items = new T[ _queue._batchSize ];
				_finalizationFlags = new bool[ _queue._batchSize ];
			}

			public bool TryAdd( T item )
			{
				int index = Interlocked.Increment( ref _lastReservationIndex );

				//	The following is true if someone has beaten us to the last slot and we have to wait until the next batch comes along.
				if ( index >= _queue._batchSize )
					return false;

				//	The following is true if we've taken the last slot, which means we're obligated to flush the current batch and create a new one.
				if ( index == _queue._batchSize - 1 )
					FlushInternal( _queue._batchSize );

				_items[ index ] = item;
				_finalizationFlags[ index ] = true;

				return true;
			}

			public bool TryFlush()
			{
				int expectedPreviousReservation = _lastReservationIndex;

				//	We don't flush if the batch doesn't have any items.
				//	However, we report success to avoid unnecessary spinning.
				if ( expectedPreviousReservation < 0 )
					return true;

				int previousReservation = Interlocked.CompareExchange( ref _lastReservationIndex, _queue._batchSize, expectedPreviousReservation );

				//	Flush reservation has succeeded.
				if ( expectedPreviousReservation == previousReservation )
				{
					FlushInternal( previousReservation + 1 );
					return true;
				}

				//	The following is true if someone has completed the batch by the time we tried to flush it.
				//	Therefore the batch will be flushed anyway even if we don't do anything.
				//	The opposite means someone has slipped in an update and we have to spin.
				return previousReservation >= _queue._batchSize;
			}

			private void FlushInternal( int count )
			{
				_count = count;
				_queue._currentBatch = new Batch( _queue );

				//	The full fence ensures that the current batch will never be added to the queue before _count is set.
				Thread.MemoryBarrier();

				_queue._batchQueue.Add( this );
			}

			private T GetItemWithoutValidation( int index )
			{
				SpinWait spin = new SpinWait();
				while ( !_finalizationFlags[ index ] )
					spin.SpinOnce();

				return _items[ index ];
			}

			#region IReadOnlyList<T> Members

			public T this[ int index ]
			{
				get
				{
					if ( index >= Count )
						throw new IndexOutOfRangeException();

					return GetItemWithoutValidation( index );
				}
			}

			#endregion

			#region IReadOnlyCollection<T> Members

			public int Count
			{
				get { return _count; }
			}

			#endregion

			#region IEnumerable<T> Members

			public IEnumerator<T> GetEnumerator()
			{
				for ( int i = 0; i < Count; i++ )
					yield return GetItemWithoutValidation( i );
			}

			#endregion

			#region IEnumerable Members

			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
			{
				return this.GetEnumerator();
			}

			#endregion
		}
	}
}