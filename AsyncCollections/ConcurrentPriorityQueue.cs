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
	public class ConcurrentPriorityQueue<TKey, TValue> : IProducerConsumerCollection<KeyValuePair<TKey, TValue>>
	{
		[ThreadStatic]
		private static Random _random;
		private static int _insertionThreadRandomSeed = 0;
		private const double _maxRandomValue = Int32.MaxValue * 1.0;

		private readonly int _maxLevels;
		private readonly double _skipChance;

		private readonly Node _head;
		private readonly Node _tail;
		private readonly IComparer<TKey> _comparer;

		public ConcurrentPriorityQueue( int maxSkipListLevels, double skipChance )
			: this( maxSkipListLevels, skipChance, Comparer<TKey>.Default )
		{
		}

		public ConcurrentPriorityQueue( int maxSkipListLevels, double skipChance, IComparer<TKey> comparer )
		{
			_maxLevels = maxSkipListLevels;
			_skipChance = skipChance;
			_comparer = comparer;

			_head = new Node( default( TKey ), default( TValue ), this, 0 );
			_tail = new Node( default( TKey ), default( TValue ), this, 0 );
			for ( int level = 0; level < maxSkipListLevels; level++ )
				_head.NextNodes[ level ] = _tail;
		}

		public int Count { get; }

		public bool TryAdd( KeyValuePair<TKey, TValue> item )
		{
			int levelsToUse = GetRandomNumberOfLevels();
			int levelsToSkip = _maxLevels - levelsToUse;
			Node newNode = new Node( item.Key, item.Value, this, levelsToUse );
			Node previousNode = _head;

			for ( int level = 0; level < _maxLevels; level++ )
			{
				SpinWait spin = new SpinWait();
				while ( true )
				{
					NodePair insertionPlace = TryFindInsertionPlace( item.Key, previousNode, level );
					previousNode = insertionPlace.Left;
					Node nextNode = insertionPlace.Right;

					if ( level < levelsToSkip )
						break;

					Volatile.Write( ref newNode.NextNodes[ level ], nextNode );
					if ( Interlocked.CompareExchange( ref previousNode.NextNodes[ level ], newNode, nextNode ) == nextNode )
						break;

					spin.SpinOnce();
				}
			}

			return true;
		}

		private int GetRandomNumberOfLevels()
		{
			_random = _random ?? new Random( Interlocked.Increment( ref _insertionThreadRandomSeed ) );
			int randomRoll = _random.Next();
			int rolledLevelsToUse = 1 + (int) Math.Log( _maxRandomValue / randomRoll, _skipChance );
			return Math.Min( rolledLevelsToUse, _maxLevels );
		}

		private NodePair TryFindInsertionPlace( TKey key, Node previousNode, int level )
		{
			while ( true )
			{
				Node nextNode = SpinUntilNextNodeIsLinked( previousNode, level );
				if ( nextNode == _tail || _comparer.Compare( key, nextNode.Key ) < 0 )
					return new NodePair( previousNode, nextNode );

				previousNode = nextNode;
			}
		}

		private static Node SpinUntilNextNodeIsLinked( Node previousNode, int level )
		{
			SpinWait spin = new SpinWait();
			while ( true )
			{
				Node nextNode = Volatile.Read( ref previousNode.NextNodes[ level ] );
				if ( nextNode != null )
					return nextNode;
				else
					spin.SpinOnce();
			}
		}

		public bool TryTake( out KeyValuePair<TKey, TValue> item )
		{
			SpinWait spin = new SpinWait();
			while ( true )
			{
				Node topNode = Volatile.Read( ref _head.NextNodes[ _maxLevels - 1 ] );
				if ( topNode == _tail )
				{
					item = default( KeyValuePair<TKey, TValue> );
					return false;
				}

				if ( topNode.TryMarkAsRemoved() )
				{
					/// Don't care about the <see cref="Interlocked.CompareExchange"/> results:
					/// if we lose the race, it means the insertion thread have set up the proper link at this level and there's nothing left for us to do.
					for ( int level = _maxLevels - topNode.LevelsUsed; level < _maxLevels; level++ )
						Interlocked.CompareExchange( ref _head.NextNodes[ level ], Volatile.Read( ref topNode.NextNodes[ level ] ), topNode );

					item = new KeyValuePair<TKey, TValue>( topNode.Key, topNode.Value );
					return true;
				}
				else
					spin.SpinOnce();
			}
		}

		bool ICollection.IsSynchronized => false;

		object ICollection.SyncRoot
		{
			get { throw new NotSupportedException(); }
		}

		void ICollection.CopyTo( Array array, int index )
		{
			throw new NotImplementedException();
		}

		void IProducerConsumerCollection<KeyValuePair<TKey, TValue>>.CopyTo( KeyValuePair<TKey, TValue>[] array, int index )
		{
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		KeyValuePair<TKey, TValue>[] IProducerConsumerCollection<KeyValuePair<TKey, TValue>>.ToArray()
		{
			throw new NotImplementedException();
		}

		private class Node
		{
			private readonly ConcurrentPriorityQueue<TKey, TValue> _queue;
			private int _isRemoved = RemovedMarker.NotRemoved;

			public Node( TKey key, TValue value, ConcurrentPriorityQueue<TKey, TValue> queue, int levelsUsed )
			{
				_queue = queue;
				NextNodes = new Node[ queue._maxLevels ];
				Key = key;
				Value = value;
				LevelsUsed = levelsUsed;
			}

			public Node[] NextNodes { get; }
			public TKey Key { get; }
			public TValue Value { get; }
			public int LevelsUsed { get; }

			public bool IsRemoved => Volatile.Read( ref _isRemoved ) == RemovedMarker.IsBeingRemoved;

			public bool TryMarkAsRemoved() => Interlocked.CompareExchange( ref _isRemoved, RemovedMarker.IsBeingRemoved, RemovedMarker.NotRemoved ) == RemovedMarker.NotRemoved;

			public override string ToString()
				=> this == _queue._head ? "Head"
				: this == _queue._tail ? "Tail"
				: $"{Key} : {Value}";
		}

		private static class RemovedMarker
		{
			public const int NotRemoved = 0;
			public const int IsBeingRemoved = 1;
		}

		private struct NodePair : IEquatable<NodePair>
		{
			public NodePair( Node left, Node right )
			{
				Left = left;
				Right = right;
			}

			public Node Left { get; }
			public Node Right { get; }

			public override string ToString() => $"[{Left}] > [{Right}]";

			public override int GetHashCode()
			{
				unchecked
				{
					const int prime = -1521134295;
					int hash = 12345701;
					hash = hash * prime + EqualityComparer<Node>.Default.GetHashCode( Left );
					hash = hash * prime + EqualityComparer<Node>.Default.GetHashCode( Right );
					return hash;
				}
			}

			public bool Equals( NodePair other ) => EqualityComparer<Node>.Default.Equals( Left, other.Left ) && EqualityComparer<Node>.Default.Equals( Right, other.Right );
			public override bool Equals( object obj ) => obj is NodePair && Equals( (NodePair) obj );

			public static bool operator ==( NodePair x, NodePair y ) => x.Equals( y );
			public static bool operator !=( NodePair x, NodePair y ) => !x.Equals( y );
		}
	}
}
