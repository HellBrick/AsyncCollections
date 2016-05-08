using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	internal class AsyncQueueBenchmark
	{
		private const int _consumerThreadCount = 3;
		private const int _producerThreadCount = 3;
		private const int _itemsAddedPerThread = 10000;
		private const int _itemsAddedTotal = _producerThreadCount * _itemsAddedPerThread;

		private readonly BenchmarkCompetition _competition;

		private IAsyncCollection<int> _currentQueue;

		public AsyncQueueBenchmark()
		{
			_competition = new BenchmarkCompetition();
			AddTask( "HellBrick.AsyncCollections.AsyncQueue", () => new AsyncQueue<int>() );
			AddTask( "Nito.AsyncEx.AsyncCollection", () => new NitoAsyncCollectionAdapter<int>() );
			AddTask( "System.Concurrent.BlockingCollection", () => new BlockingCollectionAdapter<int>() );
			AddTask( "System.Threading.Tasks.Dataflow.BufferBlock", () => new TplDataflowAdapter<int>() );
		}

		private void AddTask( string name, Func<IAsyncCollection<int>> factoryMethod )
		{
			_competition.AddTask(
				name,
				initialize : () => Initialize( factoryMethod ),
				clean : CleanUp,
				action : () => DdosCurrentQueue() );
		}

		private void Initialize( Func<IAsyncCollection<int>> factoryMethod )
		{
			_currentQueue = factoryMethod();
		}

		private void CleanUp()
		{
			_currentQueue = null;
		}

		private void DdosCurrentQueue()
		{
			DdosQueue( _currentQueue );
		}

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

		private static void RunProducer( IAsyncCollection<int> queue )
		{
			for ( int i = 0; i < _itemsAddedPerThread; i++ )
			{
				int item = 42;
				queue.Add( item );
			}
		}

		private class IntHolder
		{
			public int Value;
		}

		public void Run()
		{
			_competition.Run();
		}
	}
}
