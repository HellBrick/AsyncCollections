using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe queue that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="TItem">The type of the items contained in the queue.</typeparam>
	public class AsyncQueue<T>: AsyncCollection<T, ConcurrentQueue<T>>
	{
	}
}
