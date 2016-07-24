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
		/// Initializes a new empty instance of <see cref="AsyncQueue{T}"/>.
		/// </summary>
		public AsyncQueue()
		{
			Segment firstSegment = new Segment( this, 0 );
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

		public Task<T> TakeAsync( CancellationToken cancellationToken )
			=> cancellationToken.IsCancellationRequested
			? CanceledTask<T>.Value
			: TakeWithoutValidationAsync( cancellationToken );

		private Task<T> TakeWithoutValidationAsync( CancellationToken cancellationToken )
		{
			SpinWait spin = new SpinWait();

			while ( true )
			{
				Task<T> result = Volatile.Read( ref _awaiterTail ).TryTakeAsync( cancellationToken );
				if ( result != null )
					return result;

				spin.SpinOnce();
			}
		}

		public Enumerator GetEnumerator() => new Enumerator( this );

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => new BoxedEnumerator<T, Enumerator>( GetEnumerator() );
		IEnumerator IEnumerable.GetEnumerator() => ( this as IEnumerable<T> ).GetEnumerator();

		public struct Enumerator : IEnumerator<T>
		{
			private SelectManyStructEnumererator<Segment, SegmentEnumerator, T, Segment.Enumerator> _innerEnumerator;

			public Enumerator( AsyncQueue<T> queue )
			{
				_innerEnumerator = new SelectManyStructEnumererator<Segment, SegmentEnumerator, T, Segment.Enumerator>( new SegmentEnumerator( queue ), segment => segment.GetEnumerator() );
			}

			public T Current => _innerEnumerator.Current;
			object IEnumerator.Current => Current;

			public bool MoveNext() => _innerEnumerator.MoveNext();
			public void Dispose() => _innerEnumerator.Dispose();
			public void Reset() => _innerEnumerator.Reset();
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
			private readonly long _segmentID;

			private int _awaiterIndex = -1;
			private int _itemIndex = -1;
			private Segment _next = null;

			public Segment( AsyncQueue<T> queue, long segmentID )
			{
				_queue = queue;
				_segmentID = segmentID;
			}

			public Segment VolatileNext => Volatile.Read( ref _next );

			public long SegmentID => _segmentID;
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
				HandleLastSlotCapture( slot, wonSlot, ref _queue._itemTail );

				/// 1. If we have won the slot, the item is considered successfully added.
				/// 2. Otherwise, it's up to the result of <see cref="IAwaiter{T}.TrySetResult(T)"/>.
				///    Awaiter could have been canceled by now, and if it has, we should return false to insert item again into another slot.
				///    We also can't blindly read awaiter from the slot, because <see cref="TryTakeAsync(CancellationToken)"/> captures slot *before* filling in the awaiter.
				///    So we have to spin until it is available.
				///    And regardless of the awaiter state, we mark the slot as finished because both item and awaiter have visited it.
				return wonSlot || TrySetAwaiterResultAndMarkSlotAsFinished( item, slot );
			}

			private bool TrySetAwaiterResultAndMarkSlotAsFinished( T item, int slot )
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

			public Task<T> TryTakeAsync( CancellationToken cancellationToken )
				=> TryTakeAsync( cancellationToken, Interlocked.Increment( ref _awaiterIndex ) );

			private Task<T> TryTakeAsync( CancellationToken cancellationToken, int slot )
				=> slot < SegmentSize
				? TryTakeWithoutValidationAsync( cancellationToken, slot )
				: null;

			private Task<T> TryTakeWithoutValidationAsync( CancellationToken cancellationToken, int slot )
			{
				Task<T> result;

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
					result = Task.FromResult( _items[ slot ] );
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
			/// </remarks>
			/// <param name="tailReference">Either <see cref="AsyncQueue{T}._itemTail"/> or <see cref="AsyncQueue{T}._awaiterTail"/>, whichever we're working on right now.</param>
			private void HandleLastSlotCapture( int slot, bool wonSlot, ref Segment tailReference )
			{
				if ( IsLastSlot( slot ) )
				{
					Segment nextSegment = wonSlot ? GrowSegment() : SpinUntilNextSegmentIsReady();
					Volatile.Write( ref tailReference, nextSegment );

					if ( !wonSlot )
						Volatile.Write( ref _queue._head, nextSegment );
				}
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
				Segment newTail = new Segment( _queue, _segmentID + 1 );
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
