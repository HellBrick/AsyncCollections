using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe collection that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the collection.</typeparam>
	public interface IAsyncCollection<T> : IReadOnlyCollection<T>
	{
		/// <summary>
		/// Gets an amount of pending item requests.
		/// </summary>
		int AwaiterCount { get; }

		/// <summary>
		/// Adds an item to the collection.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		void Add( T item );

		/// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
		ValueTask<T> TakeAsync( CancellationToken cancellationToken );
	}

	public static class AsyncCollectionExtensions
	{
		/// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
		public static ValueTask<T> TakeAsync<T>( this IAsyncCollection<T> collection )
		{
			return collection.TakeAsync( CancellationToken.None );
		}
	}
}