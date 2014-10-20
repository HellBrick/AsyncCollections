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
	public interface IAsyncCollection<T>: IEnumerable<T>, System.Collections.ICollection
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
		void TakeAsync( ItemConsumer<T> consumer );
	}

	public static class AsyncCollectionExtensions
	{
        private static void SetTaskCompletionSourceCancelled<T>(object state)
        {
            ((TaskCompletionSource<T>)state).TrySetCanceled();
        }

        /// <summary>
		/// Removes and returns an item from the collection in an asynchronous manner.
		/// </summary>
		public static Task<T> TakeAsync<T>( this IAsyncCollection<T> collection )
		{
			return collection.TakeAsync( CancellationToken.None );
		}

        /// <summary>
        /// Removes and returns an item from the collection in an asynchronous manner.
        /// </summary>
        public static Task<T> TakeAsync<T>( this IAsyncCollection<T> collection, CancellationToken cancellationToken )
        {
            var tcs = new TaskCompletionSource<T>();
            collection.TakeAsync( tcs.TrySetResult );
            cancellationToken.Register(SetTaskCompletionSourceCancelled<T>, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Removes and returns an item from the one of collections in an asynchronous manner.
        /// </summary>
        public static Task<T> TakeAsyncFromAny<T>(this IEnumerable<IAsyncCollection<T>> collections, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<T>();
            var ready = new ManualResetEventSlim();

            foreach (var collection in collections)
            {
                bool inTakeAsync = true;
                collection.TakeAsync(item =>
                {
                    if (!inTakeAsync) // Workaround of "In TakeAsync, item must be consumed" error
                    {
                        var r = ready;
                        if (r != null) r.Wait();
                    }
                    return tcs.TrySetResult(item);
                });

                if (tcs.Task.Status == TaskStatus.RanToCompletion)
                    return tcs.Task;
                inTakeAsync = false;
            }

            ready.Set();
            cancellationToken.Register(SetTaskCompletionSourceCancelled<T>, tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Removes and returns an item from the one of collections in an asynchronous manner.
        /// </summary>
        public static Task<T> TakeAsyncFromAny<T>(this IEnumerable<IAsyncCollection<T>> collections)
        {
            return collections.TakeAsyncFromAny(CancellationToken.None);
        }
    }
}