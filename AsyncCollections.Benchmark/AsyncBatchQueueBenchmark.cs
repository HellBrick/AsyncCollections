using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Jobs;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	[Config( typeof( BenchmarkConfig ) )]
	public class AsyncBatchQueueBenchmark
	{
		[Params( 1, 3 )]
		public int ConsumerTasks { get; set; }

		[Params( 1, 3 )]
		public int ProducerTasks { get; set; }

		private const int _itemsAddedPerThread = 9999;
		private const int _batchSize = 32;

		[Benchmark( Description = "HellBrick.AsyncCollections.AsyncBatchQueue" )]
		public void HellBrickAsyncBatchQueue() => DdosQueue( new AsyncBatchQueue<int>( _batchSize ) );

		[Benchmark( Description = "System.Threading.Tasks.Dataflow.BatchBlock" )]
		public void DataflowBatchBlock() => DdosQueue( new TplBatchBlockAdapter<int>( _batchSize ) );

		private void DdosQueue( IAsyncBatchCollection<int> queue )
		{
			int itemsAddedTotal = ProducerTasks * _itemsAddedPerThread;
			IntHolder itemsTakenHolder = new IntHolder() { Value = 0 };
			CancellationTokenSource consumerCancelSource = new CancellationTokenSource();
			Task[] consumerTasks = Enumerable.Range( 0, ConsumerTasks )
				.Select( _ => Task.Run( () => RunConsumerAsync( queue, itemsTakenHolder, itemsAddedTotal, consumerCancelSource ) ) )
				.ToArray();

			Task[] producerTasks = Enumerable.Range( 0, ProducerTasks )
				.Select( _ => Task.Run( () => RunProducer( queue ) ) )
				.ToArray();

			Task.WaitAll( producerTasks );
			Task.WaitAll( consumerTasks );
		}

		private static void RunProducer( IAsyncBatchCollection<int> queue )
		{
			for ( int i = 0; i < _itemsAddedPerThread; i++ )
			{
				int item = 42;
				queue.Add( item );
			}

			queue.Flush();
		}

		private static async Task RunConsumerAsync( IAsyncBatchCollection<int> queue, IntHolder itemsTakeHolder, int itemsAddedTotal, CancellationTokenSource cancelSource )
		{
			try
			{
				CancellationToken cancelToken = cancelSource.Token;

				while ( true )
				{
					IReadOnlyList<int> items = await queue.TakeAsync( cancelToken ).ConfigureAwait( false );
					int itemsTakenLocal = Interlocked.Add( ref itemsTakeHolder.Value, items.Count );

					if ( itemsTakenLocal >= itemsAddedTotal )
						cancelSource.Cancel();
				}
			}
			catch ( OperationCanceledException )
			{
			}
		}

		private class IntHolder
		{
			public int Value;
		}
	}
}
