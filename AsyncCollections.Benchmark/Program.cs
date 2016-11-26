using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using HellBrick.Collections;

namespace HellBrick.AsyncCollections.Benchmark
{
	internal class Program
	{
		private static void Main( string[] args )
		{
			var q = new ConcurrentPriorityQueue<int, int>( 10, 4.0 );
			q.TryAdd( new KeyValuePair<int, int>( 5, 5 ) );
			q.TryAdd( new KeyValuePair<int, int>( 4, 4 ) );
			q.TryAdd( new KeyValuePair<int, int>( 1, 1 ) );
			q.TryAdd( new KeyValuePair<int, int>( 3, 3 ) );
			q.TryAdd( new KeyValuePair<int, int>( 2, 2 ) );

			//new BenchmarkSwitcher( EnumerateBenchmarks().ToArray() ).Run();
		}

		private static IEnumerable<Type> EnumerateBenchmarks()
		{
			yield return typeof( AsyncQueueBenchmark );
			yield return typeof( AsyncBatchQueueBenchmark );
		}
	}
}
