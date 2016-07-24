using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	public class TplBatchBlockAdapter<T> : IAsyncBatchCollection<T>
	{
		private readonly BatchBlock<T> _batchBlock;

		public TplBatchBlockAdapter( int batchSize )
		{
			_batchBlock = new BatchBlock<T>( batchSize );
		}

		public int BatchSize => _batchBlock.BatchSize;
		public int Count => _batchBlock.OutputCount;

		public void Add( T item ) => _batchBlock.Post( item );
		public ValueTask<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken ) => new ValueTask<IReadOnlyList<T>>( RecieveAsync( cancellationToken ) );
		private async Task<IReadOnlyList<T>> RecieveAsync( CancellationToken cancellationToken ) => await _batchBlock.ReceiveAsync( cancellationToken ).ConfigureAwait( false );
		public void Flush() => _batchBlock.TriggerBatch();

		public IEnumerator<IReadOnlyList<T>> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
