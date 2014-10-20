using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	class AsyncQueueBenchmark
	{
		private const int _consumerThreadCount = 3;
		private const int _producerThreadCount = 3;
		private const int _itemsAddedPerThread = 10000;
		private const int _itemsAddedTotal = _producerThreadCount * _itemsAddedPerThread;

		private BenchmarkCompetition _competition;

		private object _currentQueue;
		private Task[] _consumerTasks;
		private Task[] _producerTasks;
		private CancellationTokenSource _cancelSource;
		private int _itemsTaken;

        public AsyncQueueBenchmark()
        {
            _competition = new BenchmarkCompetition();
            AddTask("HellBrick.AsyncCollections.AsyncQueue", () => new AsyncQueue<int>(), (q, v) => q.Add(v), (q, t) => q.TakeAsync(t));
            AddTask("Nito.AsyncEx.AsyncCollection", () => new Nito.AsyncEx.AsyncCollection<int>(), (q, v) => q.Add(v), (q, t) => q.TakeAsync(t));
            AddTask("System.Concurrent.BlockingCollection", () => new BlockingCollection<int>(), (q, v) => q.Add(v), (q, t) => Task.FromResult(q.Take(t)));
            AddTask("System.Threading.Tasks.Dataflow.BufferBlock", () => new BufferBlock<int>(), (q, v) => q.Post(v), (q, t) => q.ReceiveAsync(t));
        }

		private void AddTask<T>( string name, Func<T> create, Action<T, int> produce, Func<T, CancellationToken, Task<int>> consume )
		{
			_competition.AddTask(
				name,
				initialize : () => Initialize( create ),
				clean : CleanUp,
				action : () => DdosCurrentQueue( produce, consume ) );
		}

		private void Initialize<T>( Func<T> factoryMethod )
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

        private void DdosCurrentQueue<T>( Action<T, int> produce, Func<T, CancellationToken, Task<int>> consume )
		{
			_consumerTasks = Enumerable.Range( 0, _consumerThreadCount )
                .Select(_ => Task.Run(() => RunConsumerAsync(consume)))
				.ToArray();

			_producerTasks = Enumerable.Range( 0, _producerThreadCount )
                .Select(_ => Task.Run(() => RunProducer(produce)))
				.ToArray();

			Task.WaitAll( _producerTasks );
			Task.WaitAll( _consumerTasks );
		}

        private async Task RunConsumerAsync<T>( Func<T, CancellationToken, Task<int>> consume )
		{
			try
			{
				CancellationToken cancelToken = _cancelSource.Token;

				while ( _itemsTaken < _itemsAddedTotal && !cancelToken.IsCancellationRequested )
				{
                    int item = await consume((T)_currentQueue, cancelToken);
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

        private void RunProducer<T>( Action<T, int> produce )
		{
			for ( int i = 0; i < _itemsAddedPerThread; i++ )
			{
				int item = 42;
                produce((T)_currentQueue, item);
			}
		}

		public void Run()
		{
			_competition.Run();
		}
	}
}
