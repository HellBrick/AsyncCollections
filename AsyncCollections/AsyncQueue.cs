using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HellBrick.Collections.Internal;

namespace HellBrick.Collections
{
	/// <summary>
	/// Represents a thread-safe queue that allows asynchronous consuming.
	/// </summary>
	/// <typeparam name="T">The type of the items contained in the queue.</typeparam>
	public class AsyncQueue<T> : IAsyncCollection<T>
	{
		internal const int SegmentSize = 32;

		private Segment _itemTail;
		private Segment _awaiterTail;

		/// <summary>
		/// This actually points to either <see cref="_itemTail"/> or <see cref="_awaiterTail"/>, depending on which one lags behind.
		/// The only reason this field exists is to simplify enumerating segments for things like <see cref="Count"/>, <see cref="AwaiterCount"/> or <see cref="GetEnumerator"/>.
		/// </summary>
		private Segment _head;

		/// <summary>
		/// The value is positive if there are any active enumerators and negative if any segments are being transferred to the pool.
		/// </summary>
		private int _enumerationPoolingBalance = 0;

		private Segment _segmentPoolHead = null;

		/// <summary>
		/// Initializes a new empty instance of <see cref="AsyncQueue{T}"/>.
		/// </summary>
		public AsyncQueue()
		{
			Segment firstSegment = new Segment( this ) { SegmentID = 0 };
			_itemTail = firstSegment;
			_awaiterTail = firstSegment;
			_head = firstSegment;
		}

		/// <summary>
		/// Initializes a new instance of <see cref="AsyncQueue{T}"/> that contains elements copied from a specified collection.
		/// </summary>
		/// <param name="collection">The collection whose elements are copied to the new queue.</param>
		public AsyncQueue( IEnumerable<T> collection )
			: this()
		{
			foreach ( T item in collection )
				Add( item );
		}

		public int AwaiterCount => ComputeCount( Volatile.Read( ref _awaiterTail ), Volatile.Read( ref _itemTail ), s => s.AwaiterCount );
		public int Count => ComputeCount( Volatile.Read( ref _itemTail ), Volatile.Read( ref _awaiterTail ), s => s.ItemCount );

		private int ComputeCount( Segment myTail, Segment otherTail, Func<Segment, int> countExtractor )
		{
			if ( myTail.SegmentID < otherTail.SegmentID )
				return 0;

			if ( myTail.SegmentID == otherTail.SegmentID )
				return countExtractor( myTail );

			int count = countExtractor( myTail ) + countExtractor( otherTail );
			long fullMiddleSegmentCount = myTail.SegmentID - otherTail.SegmentID - 1;
			if ( fullMiddleSegmentCount > 0 )
				count += SegmentSize * (int) fullMiddleSegmentCount;

			return count;
		}

		public void Add( T item )
		{
			SpinWait spin = new SpinWait();
			while ( !Volatile.Read( ref _itemTail ).TryAdd( item ) )
				spin.SpinOnce();
		}

		public ValueTask<T> TakeAsync( CancellationToken cancellationToken )
			=> cancellationToken.IsCancellationRequested
			? CanceledValueTask<T>.Value
			: TakeWithoutValidationAsync( cancellationToken );

		private ValueTask<T> TakeWithoutValidationAsync( CancellationToken cancellationToken )
		{
			SpinWait spin = new SpinWait();

			while ( true )
			{
				ValueTask<T>? result = Volatile.Read( ref _awaiterTail ).TryTakeAsync( cancellationToken );
				if ( result != null )
					return result.Value;

				spin.SpinOnce();
			}
		}

		public Enumerator GetEnumerator()
		{
			SpinWait spin = new SpinWait();

			while ( true )
			{
				int oldBalance = Volatile.Read( ref _enumerationPoolingBalance );
				if ( oldBalance >= 0 && Interlocked.CompareExchange( ref _enumerationPoolingBalance, oldBalance + 1, oldBalance ) == oldBalance )
					break;

				spin.SpinOnce();
			}

			return new Enumerator( this );
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => new BoxedEnumerator<T, Enumerator>( GetEnumerator() );
		IEnumerator IEnumerable.GetEnumerator() => ( this as IEnumerable<T> ).GetEnumerator();

		public struct Enumerator : IEnumerator<T>
		{
			private SelectManyStructEnumererator<Segment, SegmentEnumerator, T, Segment.Enumerator> _innerEnumerator;
			private readonly AsyncQueue<T> _queue;

			public Enumerator( AsyncQueue<T> queue )
			{
				_queue = queue;
				_innerEnumerator = new SelectManyStructEnumererator<Segment, SegmentEnumerator, T, Segment.Enumerator>( new SegmentEnumerator( queue ), segment => segment.GetEnumerator() );
			}

			public T Current => _innerEnumerator.Current;
			object IEnumerator.Current => Current;

			public bool MoveNext() => _innerEnumerator.MoveNext();
			public void Reset() => _innerEnumerator.Reset();

			public void Dispose()
			{
				_innerEnumerator.Dispose();
				Interlocked.Decrement( ref _queue._enumerationPoolingBalance );
			}
		}

		private struct SegmentEnumerator : IEnumerator<Segment>
		{
			private readonly AsyncQueue<T> _queue;
			private bool _readFirstSegment;

			public SegmentEnumerator( AsyncQueue<T> queue )
			{
				_queue = queue;
				Current = default( Segment );
				_readFirstSegment = false;
			}

			public Segment Current { get; private set; }
			object IEnumerator.Current => Current;

			public bool MoveNext()
			{
				if ( !_readFirstSegment )
				{
					Current = Volatile.Read( ref _queue._head );
					_readFirstSegment = true;
					return true;
				}

				Current = Current.VolatileNext;
				return Current != null;
			}

			public void Dispose()
			{
			}

			public void Reset()
			{
			}
		}

		private class Segment : IEnumerable<T>
		{
			private readonly T[] _items = new T[ SegmentSize ];
			private readonly IAwaiter<T>[] _awaiters = new IAwaiter<T>[ SegmentSize ];
			private readonly int[] _slotStates = new int[ SegmentSize ];

			private readonly AsyncQueue<T> _queue;
			private long _segmentID;

			private int _awaiterIndex = -1;
			private int _itemIndex = -1;
			private Segment _next = null;

			private Segment _nextPooledSegment = null;

			public Segment( AsyncQueue<T> queue )
			{
				_queue = queue;
			}

			public Segment VolatileNext => Volatile.Read( ref _next );

			public long SegmentID
			{
				get { return Volatile.Read( ref _segmentID ); }
				set { Volatile.Write( ref _segmentID, value ); }
			}

			public int ItemCount => Math.Max( 0, ItemAwaiterBalance );
			public int AwaiterCount => Math.Max( 0, -ItemAwaiterBalance );

			private int ItemAwaiterBalance => SlotReferenceToCount( ref _itemIndex ) - SlotReferenceToCount( ref _awaiterIndex );
			private int SlotReferenceToCount( ref int slotReference ) => Math.Min( SegmentSize, Volatile.Read( ref slotReference ) + 1 );

			public bool TryAdd( T item )
				=> TryAdd( item, Interlocked.Increment( ref _itemIndex ) );

			private bool TryAdd( T item, int slot )
				=> slot < SegmentSize
				&& TryAddWithoutValidation( item, slot );

			private bool TryAddWithoutValidation( T item, int slot )
			{
				_items[ slot ] = item;
				bool wonSlot = Interlocked.CompareExchange( ref _slotStates[ slot ], SlotState.HasItem, SlotState.None ) == SlotState.None;

				/// 1. If we have won the slot, the item is considered successfully added.
				/// 2. Otherwise, it's up to the result of <see cref="IAwaiter{T}.TrySetResult(T)"/>.
				///    Awaiter could have been canceled by now, and if it has, we should return false to insert item again into another slot.
				///    We also can't blindly read awaiter from the slot, because <see cref="TryTakeAsync(CancellationToken)"/> captures slot *before* filling in the awaiter.
				///    So we have to spin until it is available.
				///    And regardless of the awaiter state, we mark the slot as finished because both item and awaiter have visited it.
				bool success = wonSlot || TrySetAwaiterResultAndClearSlot( item, slot );

				HandleLastSlotCapture( slot, wonSlot, ref _queue._itemTail );
				return success;
			}

			private bool TrySetAwaiterResultAndClearSlot( T item, int slot )
			{
				bool success = SpinUntilAwaiterIsReady( slot ).TrySetResult( item );
				ClearSlot( slot );
				return success;
			}

			private IAwaiter<T> SpinUntilAwaiterIsReady( int slot )
			{
				SpinWait spin = new SpinWait();
				while ( true )
				{
					IAwaiter<T> awaiter = Volatile.Read( ref _awaiters[ slot ] );
					if ( awaiter != null )
						return awaiter;

					spin.SpinOnce();
				}
			}

			public ValueTask<T>? TryTakeAsync( CancellationToken cancellationToken )
				=> TryTakeAsync( cancellationToken, Interlocked.Increment( ref _awaiterIndex ) );

			private ValueTask<T>? TryTakeAsync( CancellationToken cancellationToken, int slot )
				=> slot < SegmentSize
				? TryTakeWithoutValidationAsync( cancellationToken, slot )
				: (ValueTask<T>?) null;

			private ValueTask<T> TryTakeWithoutValidationAsync( CancellationToken cancellationToken, int slot )
			{
				ValueTask<T> result;

				/// The order here differs from what <see cref="TryAdd(T)"/> does: we capture the slot *before* inserting an awaiter.
				/// We do it to avoid allocating an awaiter / registering the cancellation that we're not gonna need in case we lose.
				/// This means <see cref="TryAdd(T)"/> can see the default awaiter value, but it is easily solved by spinning until the awaiter is assigned.
				bool wonSlot = Interlocked.CompareExchange( ref _slotStates[ slot ], SlotState.HasAwaiter, SlotState.None ) == SlotState.None;
				if ( wonSlot )
				{
					IAwaiter<T> awaiter = new CompletionSourceAwaiterFactory<T>( cancellationToken ).CreateAwaiter();
					Volatile.Write( ref _awaiters[ slot ], awaiter );
					result = awaiter.Task;
				}
				else
				{
					result = new ValueTask<T>( _items[ slot ] );
					ClearSlot( slot );
				}

				HandleLastSlotCapture( slot, wonSlot, ref _queue._awaiterTail );
				return result;
			}

			private void ClearSlot( int slot )
			{
				Volatile.Write( ref _slotStates[ slot ], SlotState.Cleared );
				Volatile.Write( ref _awaiters[ slot ], null );
				_items[ slot ] = default( T );
			}

			/// <remarks>
			/// Here comes the tricky part.
			/// 0. We only care if we've captured exactly the last slot, so only one thread performs the segment maintenance.
			/// 1. Whether the slot is lost or won, the next time the same kind of item is inserted, there's no point in looking for a slot at the current segment.
			///    So we have to advance <see cref="AsyncQueue{T}._itemTail"/> or <see cref="AsyncQueue{T}._awaiterTail"/>, depending on the kind of item we're working on right now.
			/// 2. The last slot is captured twice: by an item and by an awaiter. We obviously should to grow a segment only once, so only the winner does it.
			/// 3. If we've lost the last slot, it's still possible the next segment is not grown yet, so we have to spin.
			/// 4. If we've lost the last slot, it means we're done with the current segment: all items and all awaiters have annihilated each other.
			///    <see cref="Count"/> and <see cref="AwaiterCount"/> are 0 now, so the segment can't contribute to <see cref="AsyncQueue{T}.Count"/> or <see cref="AsyncQueue{T}.Count"/>.
			///    So we lose the reference to it by advancing <see cref="AsyncQueue{T}._head"/>.
			/// 5. If we've lost the last slot, we pool it to be reused later.
			/// </remarks>
			/// <param name="tailReference">Either <see cref="AsyncQueue{T}._itemTail"/> or <see cref="AsyncQueue{T}._awaiterTail"/>, whichever we're working on right now.</param>
			private void HandleLastSlotCapture( int slot, bool wonSlot, ref Segment tailReference )
			{
				if ( !IsLastSlot( slot ) )
					return;

				Segment nextSegment = wonSlot ? GrowSegment() : SpinUntilNextSegmentIsReady();
				Volatile.Write( ref tailReference, nextSegment );

				if ( wonSlot )
					return;

				Volatile.Write( ref _queue._head, nextSegment );
				TryPoolSegment();
			}

			private void TryPoolSegment()
			{
				if ( !TryDecreaseBalance() )
					return;

				/// We reset <see cref="_next"/> so it could be GC-ed if it doesn't make it to the pool.
				/// It's safe to do so because:
				/// 1. <see cref="TryDecreaseBalance"/> guarantees that the whole queue is not being enumerated right now.
				/// 2. By this time <see cref="_head"/> is already rewritten so future enumerators can't possibly reference the current segment.
				/// The rest of the clean-up is *NOT* safe to do here, see <see cref="ResetAfterTakingFromPool"/> for details.
				Volatile.Write( ref _next, null );
				PushToPool();
				Interlocked.Increment( ref _queue._enumerationPoolingBalance );
			}

			private bool TryDecreaseBalance()
			{
				SpinWait spin = new SpinWait();
				while ( true )
				{
					int enumeratorPoolBalance = Volatile.Read( ref _queue._enumerationPoolingBalance );

					// If the balance is positive, we have some active enumerators and it's dangerous to pool the segment right now.
					// We can't spin until the balance is restored either, because we have no guarantee that enumerators will be disposed soon (or will be disposed at all).
					// So we have no choice but to give up on pooling the segment.
					if ( enumeratorPoolBalance > 0 )
						return false;

					if ( Interlocked.CompareExchange( ref _queue._enumerationPoolingBalance, enumeratorPoolBalance - 1, enumeratorPoolBalance ) == enumeratorPoolBalance )
						return true;

					spin.SpinOnce();
				}
			}

			private void PushToPool()
			{
				SpinWait spin = new SpinWait();
				while ( true )
				{
					Segment oldHead = Volatile.Read( ref _queue._segmentPoolHead );
					Volatile.Write( ref _nextPooledSegment, oldHead );

					if ( Interlocked.CompareExchange( ref _queue._segmentPoolHead, this, oldHead ) == oldHead )
						break;

					spin.SpinOnce();
				}
			}

			private Segment TryPopSegmentFromPool()
			{
				SpinWait spin = new SpinWait();
				while ( true )
				{
					Segment oldHead = Volatile.Read( ref _queue._segmentPoolHead );
					if ( oldHead == null )
						return null;

					if ( Interlocked.CompareExchange( ref _queue._segmentPoolHead, oldHead._nextPooledSegment, oldHead ) == oldHead )
					{
						Volatile.Write( ref oldHead._nextPooledSegment, null );
						return oldHead;
					}

					spin.SpinOnce();
				}
			}

			/// <remarks>
			/// It's possible for the appenders to read the tail reference before it's updated and to try appending to the segment while it's being pooled.
			/// There's no cheap way to prevent it, so we have to be prepared that an append can succeed as fast as we reset <see cref="_itemIndex"/> or <see cref="_awaiterIndex"/>.
			/// This means this method must *NOT* be called on putting the segment into the pool, because it could lead to items/awaiters being stored somewhere in the pool.
			/// Since there's no guarantee that the segment will ever be reused, this effectively means losing data (or, in the best-case scenario, completely screwing up the item order).
			/// We prevent this disaster by calling this method on taking segment from the pool, instead of putting it there.
			/// This way even if such append happens, we're about the reattach the segment to the queue and the data won't be lost.
			/// </remarks>
			private Segment ResetAfterTakingFromPool()
			{
				/// We must reset <see cref="_slotStates"/> before <see cref="_awaiterIndex"/> and <see cref="_itemIndex"/>.
				/// Otherwise appenders could successfully increment a pointer and mess with the slots before they are ready to be messed with.
				for ( int i = 0; i < SegmentSize; i++ )
				{
					/// We can't simply overwrite the state, because it's possible that the slot loser has not finished <see cref="ClearSlot(int)"/> yet.
					SpinWait spin = new SpinWait();
					while ( Interlocked.CompareExchange( ref _slotStates[ i ], SlotState.None, SlotState.Cleared ) != SlotState.Cleared )
						spin.SpinOnce();
				}

				Volatile.Write( ref _awaiterIndex, -1 );
				Volatile.Write( ref _itemIndex, -1 );

				return this;
			}

			private static bool IsLastSlot( int slot ) => slot == SegmentSize - 1;

			private Segment SpinUntilNextSegmentIsReady()
			{
				SpinWait spin = new SpinWait();
				while ( VolatileNext == null )
					spin.SpinOnce();

				return VolatileNext;
			}

			private Segment GrowSegment()
			{
				Segment newTail = TryPopSegmentFromPool()?.ResetAfterTakingFromPool() ?? new Segment( _queue );
				newTail.SegmentID = _segmentID + 1;
				Volatile.Write( ref _next, newTail );
				return newTail;
			}

			private int SpinUntilStateIsResolvedAndReturnState( int slot )
			{
				SpinWait spin = new SpinWait();
				int slotState;
				while ( true )
				{
					slotState = Volatile.Read( ref _slotStates[ slot ] );
					if ( slotState != SlotState.None )
						break;

					spin.SpinOnce();
				}

				return slotState;
			}

			public Enumerator GetEnumerator() => new Enumerator( this );
			IEnumerator<T> IEnumerable<T>.GetEnumerator() => new BoxedEnumerator<T, Enumerator>( GetEnumerator() );
			IEnumerator IEnumerable.GetEnumerator() => ( this as IEnumerable<T> ).GetEnumerator();

			private static class SlotState
			{
				public const int None = 0;
				public const int HasItem = 1;
				public const int HasAwaiter = 2;
				public const int Cleared = 3;
			}

			public struct Enumerator : IEnumerator<T>
			{
				private readonly Segment _segment;
				private int _currentSlot;
				private int _effectiveLength;

				public Enumerator( Segment segment )
				{
					_segment = segment;
					_currentSlot = Int32.MinValue;
					_effectiveLength = Int32.MinValue;
					Current = default( T );
				}

				public T Current { get; private set; }
				object IEnumerator.Current => Current;

				public bool MoveNext()
				{
					if ( _currentSlot == Int32.MinValue )
					{
						/// Items in slots 0 .. <see cref="_awaiterIndex"/> are taken by awaiters, so they are no longer considered stored in the queue.
						_currentSlot = _segment.SlotReferenceToCount( ref _segment._awaiterIndex );

						/// <see cref="_itemIndex"/> is the last slot an item actually exists at at the moment, so we shouldn't enumerate through the default values that are stored further.
						_effectiveLength = _segment.SlotReferenceToCount( ref _segment._itemIndex );
					}

					while ( _currentSlot < _effectiveLength )
					{
						int slotState = _segment.SpinUntilStateIsResolvedAndReturnState( _currentSlot );
						Current = _segment._items[ _currentSlot ];
						_currentSlot++;

						if ( slotState == SlotState.HasItem )
							return true;
					}

					return false;
				}

				public void Dispose()
				{
				}

				public void Reset()
				{
				}
			}
		}
	}
}
