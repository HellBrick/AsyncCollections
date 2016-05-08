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

		private BenchmarkCompetition _competition;

		private IAsyncCollection<int> _currentQueue;
		private Task[] _consumerTasks;
		private Task[] _producerTasks;
		private CancellationTokenSource _cancelSource;
		private int _itemsTaken;

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
			_itemsTaken = 0;
			_cancelSource = new CancellationTokenSource();
		}

		private void CleanUp()
		{
			_currentQueue = null;
			_consumerTasks = null;
			_producerTasks = null;
			_cancelSource = null;
		}

		private void DdosCurrentQueue()
		{
			_consumerTasks = Enumerable.Range( 0, _consumerThreadCount )
				.Select( _ => Task.Run( () => RunConsumerAsync() ) )
				.ToArray();

			_producerTasks = Enumerable.Range( 0, _producerThreadCount )
				.Select( _ => Task.Run( () => RunProducer() ) )
				.ToArray();

			Task.WaitAll( _producerTasks );
			Task.WaitAll( _consumerTasks );
		}

		private async Task RunConsumerAsync()
		{
			try
			{
				CancellationToken cancelToken = _cancelSource.Token;

				while ( _itemsTaken < _itemsAddedTotal && !cancelToken.IsCancellationRequested )
				{
					int item = await _currentQueue.TakeAsync( cancelToken );
					int itemsTakenLocal = Interlocked.Increment( ref _itemsTaken );

					if ( itemsTakenLocal >= _itemsAddedTotal )
					{
						_cancelSource.Cancel();
						break;
					}
				}
			}
			catch ( OperationCanceledException )
			{
			}
		}

		private void RunProducer()
		{
			for ( int i = 0; i < _itemsAddedPerThread; i++ )
			{
				int item = 42;
				_currentQueue.Add( item );
			}
		}

		public void Run()
		{
			_competition.Run();
		}
	}
}
