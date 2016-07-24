using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe queue with a bounded number of priority levels.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the queue.</typeparam>
	public class AsyncBoundedPriorityQueue<T> : AsyncCollection<PrioritizedItem<T>>, IAsyncCollection<T>
	{
		private readonly Func<T, int> _priorityResolver;

		/// <summary>
		/// <para>Initializes a new <see cref="AsyncBoundedPriorityQueue{T}"/> instance with a specified number of priority levels.</para>
		/// <para>The items will be inserted at the lowest priority by default.</para>
		/// </summary>
		/// <param name="priorityLevels">An amount of priority levels to support.</param>
		public AsyncBoundedPriorityQueue( int priorityLevels )
			: this( priorityLevels, _ => priorityLevels - 1 )
		{
		}

		/// <summary>
		/// Initializes a new <see cref="AsyncBoundedPriorityQueue{T}"/> instance with a specified number of priority levels and a specified priority resolver.
		/// </summary>
		/// <param name="priorityLevels">An amount of priority levels to support.</param>
		/// <param name="priorityResolver">The delegate to use to determine the default item priority.
		/// Must return an integer between 0 (top priority) and <paramref name="priorityLevels"/> - 1 (low priority).
		/// </param>
		public AsyncBoundedPriorityQueue( int priorityLevels, Func<T, int> priorityResolver )
			: base( new ConcurrentBoundedPriorityQueue( priorityLevels ) )
		{
			PriorityLevels = priorityLevels;
			_priorityResolver = priorityResolver;
		}

		/// <summary>
		/// Gets the amount of priority levels the collection supports.
		/// </summary>
		public int PriorityLevels { get; }

		/// <summary>
		/// Adds an item to the collection at the highest priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void AddTopPriority( T item ) => Add( item, 0 );

		/// <summary>
		/// Adds an item to the collection at the lowest priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void AddLowPriority( T item ) => Add( item, PriorityLevels - 1 );

		/// <summary>
		/// Adds an item to the collection at a specified priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		/// <param name="priority">The priority of the item, with 0 being the top priority.</param>
		public void Add( T item, int priority )
		{
			if ( priority < 0 || priority > PriorityLevels )
				throw new ArgumentOutOfRangeException( nameof( priority ), priority, $"Priority can't be less than 0 or bigger than {PriorityLevels - 1}." );

			Add( new PrioritizedItem<T>( item, priority ) );
		}

		/// <summary>
		/// Adds an item to the collection at default priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void Add( T item ) => Add( item, _priorityResolver( item ) );

		/// <summary>
		/// Removes and returns an item with the highest priority from the collection in an asynchronous manner.
		/// </summary>
		public ValueTask<PrioritizedItem<T>> TakeAsync() => TakeAsync( CancellationToken.None );

		/// <summary>
		/// Removes and returns an item with the highest priority from the collection in an asynchronous manner.
		/// </summary>
		ValueTask<T> IAsyncCollection<T>.TakeAsync( System.Threading.CancellationToken cancellationToken )
		{
			ValueTask<PrioritizedItem<T>> prioritizedItemTask = TakeAsync( cancellationToken );
			return prioritizedItemTask.IsCompletedSuccessfully ? new ValueTask<T>( prioritizedItemTask.Result.Item ) : new ValueTask<T>( UnwrapAsync( prioritizedItemTask ) );
		}

		private async Task<T> UnwrapAsync( ValueTask<PrioritizedItem<T>> prioritizedItemTask )
		{
			PrioritizedItem<T> result = await prioritizedItemTask.ConfigureAwait( false );
			return result.Item;
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => ( this as IEnumerable<PrioritizedItem<T>> ).Select( prioritizedItem => prioritizedItem.Item ).GetEnumerator();

		private class ConcurrentBoundedPriorityQueue : IProducerConsumerCollection<PrioritizedItem<T>>
		{
			private readonly ConcurrentQueue<T>[] _itemQueues;

			public ConcurrentBoundedPriorityQueue( int priorityLevels )
			{
				if ( priorityLevels < 0 )
					throw new ArgumentOutOfRangeException( nameof( priorityLevels ), priorityLevels, "Amount of priority levels can't be less than 0." );

				_itemQueues = Enumerable.Range( 0, priorityLevels ).Select( _ => new ConcurrentQueue<T>() ).ToArray();
			}

			public int Count => _itemQueues.Sum( q => q.Count );

			public bool TryAdd( PrioritizedItem<T> item )
			{
				_itemQueues[ item.Priority ].Enqueue( item.Item );
				return true;
			}

			public bool TryTake( out PrioritizedItem<T> item )
			{
				for ( int priority = 0; priority < _itemQueues.Length; priority++ )
				{
					T itemValue;
					if ( _itemQueues[ priority ].TryDequeue( out itemValue ) )
					{
						item = new PrioritizedItem<T>( itemValue, priority );
						return true;
					}
				}

				item = default( PrioritizedItem<T> );
				return false;
			}

			public IEnumerator<PrioritizedItem<T>> GetEnumerator()
				=> _itemQueues
				.SelectMany( ( queue, index ) => queue.Select( item => new PrioritizedItem<T>( item, index ) ) )
				.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
			bool ICollection.IsSynchronized => false;
			object ICollection.SyncRoot => null;

			void ICollection.CopyTo( Array array, int index )
			{
				throw new NotSupportedException();
			}

			void IProducerConsumerCollection<PrioritizedItem<T>>.CopyTo( PrioritizedItem<T>[] array, int index )
			{
				throw new NotSupportedException();
			}

			PrioritizedItem<T>[] IProducerConsumerCollection<PrioritizedItem<T>>.ToArray()
			{
				throw new NotSupportedException();
			}
		}
	}
}
