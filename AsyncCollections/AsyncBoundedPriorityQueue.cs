using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections
{
	public class AsyncBoundedPriorityQueue<T>: IAsyncCollection<T>
	{
		private readonly Func<T, int> _priorityExtractor;
		private readonly AsyncQueue<T>[] _priorityQueues;

		#region Construction

		public AsyncBoundedPriorityQueue( int priorityLevels )
			: this( priorityLevels, _ => priorityLevels - 1 )
		{
		}

		public AsyncBoundedPriorityQueue( int priorityLevels, Func<T, int> priorityExtractor )
		{
			if ( priorityLevels < 0 || priorityLevels > 32 )
				throw new ArgumentOutOfRangeException( "priorityLevels", priorityLevels, "Amount of priority levels can't be less than 0 or bigger than 32" );

			_priorityExtractor = priorityExtractor;

			_priorityQueues = new AsyncQueue<T>[ priorityLevels ];
			for ( int i = 0; i < priorityLevels; i++ )
				_priorityQueues[ i ] = new AsyncQueue<T>();
		}

		#endregion

		#region IAsyncCollection<T> Members

		public int AwaiterCount
		{
			get { return _priorityQueues.Sum( q => q.AwaiterCount ); }
		}

		public void Add( T item )
		{
			Add( item, _priorityExtractor( item ) );
		}

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

		public async Task<T> TakeAsync( System.Threading.CancellationToken cancellationToken )
		{
			var result = await AsyncCollection<T>.TakeFromAnyAsync( _priorityQueues, cancellationToken ).ConfigureAwait( false );
			return result.Value;
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
