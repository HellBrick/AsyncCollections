using System;
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
	public class AsyncBoundedPriorityQueue<T> : IAsyncCollection<T>
	{
		private readonly Func<T, int> _priorityResolver;
		private readonly AsyncQueue<T>[] _priorityQueues;

		private int _awaiterCount = 0;

		#region Construction

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
		{
			if ( priorityLevels < 0 || priorityLevels > AsyncCollection<T>.TakeFromAnyMaxCollections )
			{
				throw new ArgumentOutOfRangeException(
					"priorityLevels",
					priorityLevels,
					String.Format( "Amount of priority levels can't be less than 0 or bigger than {0}", AsyncCollection<T>.TakeFromAnyMaxCollections ) );
			}

			_priorityResolver = priorityResolver;

			_priorityQueues = new AsyncQueue<T>[ priorityLevels ];
			for ( int i = 0; i < priorityLevels; i++ )
				_priorityQueues[ i ] = new AsyncQueue<T>();
		}

		#endregion

		#region Priority-specific

		/// <summary>
		/// Gets the amount of priority levels the collection supports.
		/// </summary>
		public int PriorityLevels
		{
			get { return _priorityQueues.Length; }
		}

		/// <summary>
		/// Adds an item to the collection at the highest priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void AddTopPriority( T item )
		{
			Add( item, 0 );
		}

		/// <summary>
		/// Adds an item to the collection at the lowest priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void AddLowPriority( T item )
		{
			Add( item, _priorityQueues.Length - 1 );
		}

		/// <summary>
		/// Adds an item to the collection at a specified priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		/// <param name="priority">The priority of the item, with 0 being the top priority.</param>
		public void Add( T item, int priority )
		{
			if ( priority < 0 || priority > _priorityQueues.Length )
			{
				throw new ArgumentOutOfRangeException(
					"priority",
					priority,
					String.Format( "Priority can't be less than 0 or bigger than {0}.", _priorityQueues.Length - 1 ) );
			}

			_priorityQueues[ priority ].Add( item );
		}

		#endregion

		#region IAsyncCollection<T> Members

		/// <summary>
		/// Gets an amount of pending item requests.
		/// </summary>
		public int AwaiterCount
		{
			get { return Volatile.Read( ref _awaiterCount ); }
		}

		/// <summary>
		/// Adds an item to the collection at default priority.
		/// </summary>
		/// <param name="item">The item to add to the collection.</param>
		public void Add( T item )
		{
			Add( item, _priorityResolver( item ) );
		}

		/// <summary>
		/// Removes and returns an item with the highest priority from the collection in an asynchronous manner.
		/// </summary>
		public async Task<T> TakeAsync( System.Threading.CancellationToken cancellationToken )
		{
			Interlocked.Increment( ref _awaiterCount );

			try
			{
				var result = await AsyncCollection<T>.TakeFromAnyAsync( _priorityQueues, cancellationToken ).ConfigureAwait( false );
				return result.Value;
			}
			finally
			{
				Interlocked.Decrement( ref _awaiterCount );
			}
		}

		#endregion

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			return _priorityQueues.SelectMany( q => q ).GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return this.GetEnumerator();
		}

		#endregion

		#region ICollection Members

		void System.Collections.ICollection.CopyTo( Array array, int index )
		{
			throw new NotSupportedException();
		}

		public int Count
		{
			get { return _priorityQueues.Sum( q => q.Count ); }
		}

		bool System.Collections.ICollection.IsSynchronized
		{
			get { return false; }
		}

		object System.Collections.ICollection.SyncRoot
		{
			get { throw new NotSupportedException(); }
		}

		#endregion
	}
}
