using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.AsyncCollections.Benchmark
{
	internal class Program
	{
		private static void Main( string[] args )
		{
			var competition = new AsyncQueueBenchmark();
			competition.Run();
		}
	}
}
