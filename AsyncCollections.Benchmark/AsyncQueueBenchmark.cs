using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	[Config( typeof( Config ) )]
	public class AsyncQueueBenchmark
	{
		private const int _consumerThreadCount = 3;
		private const int _producerThreadCount = 3;
		private const int _itemsAddedPerThread = 10000;
		private const int _itemsAddedTotal = _producerThreadCount * _itemsAddedPerThread;

		private class Config : ManualConfig
		{
			public Config()
			{
				Add( Job.RyuJitX64.WithLaunchCount( 1 ) );
			}
		}

		[Benchmark( Description = "HellBrick.AsyncCollections.AsyncQueue" )]
		public void HellBrickAsyncQueue() => DdosQueue( new AsyncQueue<int>() );

		[Benchmark( Description = "Nito.AsyncEx.AsyncCollection" )]
		public void NitoAsyncCollection() => DdosQueue( new NitoAsyncCollectionAdapter<int>() );

		[Benchmark( Description = "System.Concurrent.BlockingCollection" )]
		public void SystemBlockingCollection() => DdosQueue( new BlockingCollectionAdapter<int>() );

		[Benchmark( Description = "System.Threading.Tasks.Dataflow.BufferBlock" )]
		public void DataflowBufferBlock() => DdosQueue( new TplDataflowAdapter<int>() );

		private static void DdosQueue( IAsyncCollection<int> queue )
		{
			IntHolder itemsTakenHolder = new IntHolder() { Value = 0 };
			CancellationTokenSource consumerCancelSource = new CancellationTokenSource();
			Task[] consumerTasks = Enumerable.Range( 0, _consumerThreadCount )
				.Select( _ => Task.Run( () => RunConsumerAsync( queue, itemsTakenHolder, consumerCancelSource ) ) )
				.ToArray();

			Task[] producerTasks = Enumerable.Range( 0, _producerThreadCount )
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

		private static async Task RunConsumerAsync( IAsyncCollection<int> queue, IntHolder itemsTakeHolder, CancellationTokenSource cancelSource )
		{
			try
			{
				CancellationToken cancelToken = cancelSource.Token;

				while ( true )
				{
					int item = await queue.TakeAsync( cancelToken ).ConfigureAwait( false );
					int itemsTakenLocal = Interlocked.Increment( ref itemsTakeHolder.Value );

					if ( itemsTakenLocal >= _itemsAddedTotal )
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
