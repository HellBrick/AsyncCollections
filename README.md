What is it and why should I care?
=================================

`AsyncCollections` is a library that contains a bunch of fast lock-free thread-safe producer-consumer collections tailored for asynchronous usage. Think `System.Collections.Concurrent` but fully `async`-ready.

Nuget package
=============

[https://www.nuget.org/packages/AsyncCollections/](https://www.nuget.org/packages/AsyncCollections/)

API
===

IAsyncCollection
-------------------

Most of collections here implement this interface (I'd say it pretty much describes what this library is all about):

```C#
public interface IAsyncCollection<T> : IReadOnlyCollection<T>
{
	void Add( T item );
	Task<T> TakeAsync( CancellationToken cancellationToken );

	int AwaiterCount { get; } // An amount of pending item requests
}
```

AsyncQueue and AsyncStack
-------------------------

These classes provide queue- and stack-based implementations of `IAsyncCollection<T>`.

```C#
AsyncQueue<int> queue = new AsyncQueue<int>();
Task<int> itemTask = queue.TakeAsync();
queue.Add( 42 ); // at this point itemTask completes with Result = 42

AsyncStack<int> stack = new AsyncStack<int>();
stack.Add( 64 );
stack.Add( 128 );
int first = await stack.TakeAsync(); // 128
int second = await stack.TaskAsync(); // 64
```

AsyncCollection
---------------

Think `System.Concurrent.BlockingCollection` for `async` usage. `AsyncCollection<T>` wraps anything that implements `IProducerConcumerCollection<T>` and turns it into an `IAsyncCollection<T>`:

```C#
var asyncBag = new AsyncCollection<int>( new ConcurrentBag<int>() );
asyncBag.Add( 984 );
int item = await asyncBag.TakeAsync();
```

Another trait it shares with `BlockingCollection` is a static `TakeFromAny` method (which is actually called `TakeFromAnyAsync` here):

```C#
var bag1 = new AsyncCollection<int>( new ConcurrentBag<int>() );
var bag2 = new AsyncCollection<int>( new ConcurrentBag<int>() );
AsyncCollection<int>[] collections = new [] { bag1, bag2 };

// Will return asynchronously when one of the queues gets an item.
AnyResult<int> result = await AsyncCollection<int>.TakeFromAnyAsync( collections );

// Index of the collection that returned the item.
int index = result.CollectionIndex;

// The actual item that has been returned.
int value = result.Value;
```

AsyncBoundedPriorityQueue
-------------------------

This is a priority queue with a limited number of priority levels:

```C#
var queue = new AsyncBoundedPriorityQueue<int>( priorityLevels: 3 );

queue.Add( 1000 ); // item is added at the lowest priority by default
queue.AddTopPriority( 42 );
queue.AddLowPriority( 999 );
queue.Add( 16, priority: 1 );

// 42 16 1000 999 
while ( true )
{
  Debug.Write( await queue.TakeAsync() );
  Debug.Write( " " );
}
```

AsyncBatchQueue
---------------

This one doesn't implement `IAsyncCollection<T>`, because it provides a slightly different experience. Just like `AsyncQueue<T>`, `AsyncBatchQueue<T>` allows you to add items synchronously and retreive them asynchronously, but the difference is you consume them in batches of the specified size:

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( batchSize: 2 );
queue.Add( 42 );
queue.Add( 64 );
queue.Add( 128 );

// Immediately returns a batch of items 42 and 64.
IReadOnlyList<int> batch1 = await queue.TakeAsync();

// Asynchronously returns when the next batch is full
// (128 and whatever the next item will be)
IReadOnlyList<int> batch2 = await queue.TakeAsync();
```

There's a constructor overload that allows you to specify a time period to wait before the pending items are flushed and a batch is made available for consuming, even if the batch size is not reached yet:

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( 9999, TimeSpan.FromSeconds( 5 ) );
queue.Add( 42 );

// This will asynchronously return a batch of 1 item in 5 seconds
IReadOnlyList<int> batch = await queue.TakeAsync();
```
