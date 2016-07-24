using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public class TimerAsyncBatchQueue<T> : IAsyncBatchCollection<T>, IDisposable
	{
		private readonly Timer _flushTimer;
		private readonly IAsyncBatchCollection<T> _innerCollection;

		public TimerAsyncBatchQueue( IAsyncBatchCollection<T> innerCollection, TimeSpan flushPeriod )
		{
			_innerCollection = innerCollection;
			_flushTimer = new Timer( _ => Flush(), null, flushPeriod, flushPeriod );
		}

		public int BatchSize => _innerCollection.BatchSize;
		public int Count => _innerCollection.Count;
		public void Add( T item ) => _innerCollection.Add( item );
		public ValueTask<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken ) => _innerCollection.TakeAsync( cancellationToken );
		public void Flush() => _innerCollection.Flush();
		public IEnumerator<IReadOnlyList<T>> GetEnumerator() => _innerCollection.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => ( _innerCollection as IEnumerable ).GetEnumerator();

		public void Dispose() => _flushTimer.Dispose();
	}
}
