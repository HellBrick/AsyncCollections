using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public class AsyncBatchQueue<T>
	{
		private int _batchSize;
		private Batch _currentBatch;
		private AsyncQueue<IReadOnlyList<T>> _batchQueue = new AsyncQueue<IReadOnlyList<T>>();

		public AsyncBatchQueue( int batchSize )
		{
			_batchSize = batchSize;
			_currentBatch = new Batch( this );
		}

		public void Add( T item )
		{
			SpinWait spin = new SpinWait();

			while ( !_currentBatch.TryAdd( item ) )
				spin.SpinOnce();
		}

		public Task<IReadOnlyList<T>> TakeAsync()
		{
			return TakeAsync( CancellationToken.None );
		}

		public Task<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken )
		{
			return _batchQueue.TakeAsync( cancellationToken );
		}

		public void Flush()
		{
			SpinWait spin = new SpinWait();
			while ( !_currentBatch.TryFlush() )
				spin.SpinOnce();
		}

		private class Batch: IReadOnlyList<T>
		{
			private AsyncBatchQueue<T> _queue;
			private T[] _items;
			private bool[] _finalizationFlags;
			private int _lastReservationIndex = -1;
			private int _count = 0;

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
				{
					_queue._currentBatch = new Batch( _queue );
					_queue._batchQueue.Add( this );
				}

				_items[ index ] = item;
				_finalizationFlags[ index ] = true;
				Interlocked.Increment( ref _count );

				return true;
			}

			public bool TryFlush()
			{
				throw new NotImplementedException();
			}

			private bool IsFinalized
			{
				get { return _count == _queue._batchSize; }
			}

			private T GetItemWithoutValidation( int index )
			{
				if ( !IsFinalized )
				{
					SpinWait spin = new SpinWait();
					while ( !_finalizationFlags[ index ] )
						spin.SpinOnce();
				}

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
				get { return _queue._batchSize; }
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
