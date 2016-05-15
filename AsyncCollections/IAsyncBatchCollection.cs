using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public interface IAsyncBatchCollection<T> : IReadOnlyCollection<IReadOnlyList<T>>
	{
		int BatchSize { get; }

		void Add( T item );
		void Flush();
		Task<IReadOnlyList<T>> TakeAsync( CancellationToken cancellationToken );
	}

	public static class AsyncBatchCollectionExtensions
	{
		public static Task<IReadOnlyList<T>> TakeAsync<T>( this IAsyncBatchCollection<T> collection ) => collection.TakeAsync( CancellationToken.None );
	}
}