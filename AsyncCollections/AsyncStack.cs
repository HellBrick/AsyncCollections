using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe stack that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="TItem">The type of the items contained in the stack.</typeparam>
    public class AsyncStack<T> : AsyncCollection<T>
    {
        public AsyncStack() : base(new ConcurrentStack<T>()) { }
        public AsyncStack(IEnumerable<T> collection) : base(new ConcurrentStack<T>(collection)) { }
    }
}
