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
	/// <typeparam name="T">The type of the items contained in the stack.</typeparam>
	public class AsyncStack<T> : AsyncCollection<T>
	{
		/// <summary>
		/// Initializes a new empty instance of <see cref="AsyncStack{T}"/>.
		/// </summary>
		public AsyncStack() : base( new ConcurrentStack<T>() ) { }

		/// <summary>
		/// Initializes a new instance of <see cref="AsyncStack{T}"/> that contains elements copied from a specified collection.
		/// </summary>
		/// <param name="collection">The collection whose elements are copied to the new stack.</param>
		public AsyncStack( IEnumerable<T> collection ) : base( new ConcurrentStack<T>( collection ) ) { }
	}
}
