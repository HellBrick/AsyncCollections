using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace HellBrick.AsyncCollections.Benchmark
{
	internal class Program
	{
		private static void Main( string[] args ) => new BenchmarkSwitcher( EnumerateBenchmarks().ToArray() ).Run();

		private static IEnumerable<Type> EnumerateBenchmarks()
		{
			yield return typeof( AsyncQueueBenchmark );
			yield return typeof( AsyncBatchQueueBenchmark );
		}
	}
}
