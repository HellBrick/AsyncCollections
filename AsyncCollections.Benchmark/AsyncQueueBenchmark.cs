using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Jobs;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	[Config( typeof( BenchmarkConfig ) )]
	public class AsyncQueueBenchmark
	{
		[Params( 1, 3 )]
		public int ConsumerTasks { get; set; }

		[Params( 1, 3 )]
		public int ProducerTasks { get; set; }

		private const int _itemsAddedPerThread = 10000;

		[Benchmark( Description = "AsyncQueue" )]
		public void HellBrickAsyncQueue() => DdosQueue( new AsyncQueue<int>() );

		[Benchmark( Description = "AsyncCollection( ConcurrentQueue )" )]
		public void HellBrickAsyncCollection() => DdosQueue( new AsyncCollection<int>( new ConcurrentQueue<int>() ) );

		[Benchmark( Description = "Nito.AsyncEx.AsyncCollection" )]
		public void NitoAsyncCollection() => DdosQueue( new NitoAsyncCollectionAdapter<int>() );

		[Benchmark( Description = "System.Concurrent.BlockingCollection" )]
		public void SystemBlockingCollection() => DdosQueue( new BlockingCollectionAdapter<int>() );

		[Benchmark( Description = "System.Threading.Tasks.Dataflow.BufferBlock" )]
		public void DataflowBufferBlock() => DdosQueue( new TplDataflowAdapter<int>() );

		private void DdosQueue( IAsyncCollection<int> queue )
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

		private static void RunProducer( IAsyncCollection<int> queue )
		{
			for ( int i = 0; i < _itemsAddedPerThread; i++ )
			{
				int item = 42;
				queue.Add( item );
			}
		}

		private static async Task RunConsumerAsync( IAsyncCollection<int> queue, IntHolder itemsTakeHolder, int itemsAddedTotal, CancellationTokenSource cancelSource )
		{
			try
			{
				CancellationToken cancelToken = cancelSource.Token;

				while ( true )
				{
					int item = await queue.TakeAsync( cancelToken ).ConfigureAwait( false );
					int itemsTakenLocal = Interlocked.Increment( ref itemsTakeHolder.Value );

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
