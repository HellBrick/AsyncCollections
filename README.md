AsyncCollections
================

Have you ever dreamed about an asynchronous version of BlockingCollection<T> that returns thread to the thread pool instead of blocking? If the answer is 'yes', you'll definitely find this library interesting.

Nuget package
-------------

[https://www.nuget.org/packages/AsyncCollections/](https://www.nuget.org/packages/AsyncCollections/)

``AsyncQueue<T>`` and ``AsyncStack<T>``
-------------------------------

These classes provide simple asynchronous implementations of queue and stack respectively.

```C#
AsyncQueue<int> queue = new AsyncQueue<int>();
queue.Add( 42 );

CancellationTokenSource cancelSource = new CancellationTokenSource();
int item = await queue.TakeAsync( cancelSource.Token );
```

``AsyncCollection<T>``
-------------------------------

``AsyncCollection<T>`` is the base class for ``AsyncQueue<T>`` and ``AsyncStack<T>`` that also provides functions similar to ones of the ``BlockingCollection<T>``. For instance:

```C#
AsyncQueue<int> queue1 = new AsyncQueue<int>();
AsyncQueue<int> queue2 = new AsyncQueue<int>();
AsyncQueue<int>[] _collections = new [] { queue1, queue2 };

//	will return asynchronously when one of the queues gets an item
AnyResult<int> result = await AsyncCollection<int>.TakeFromAnyAsync( _collections );

//  index of the collection that returned the item
int index = result.CollectionIndex;

//  the actual item that has been returned
int value = result.Value;
```

``AsyncBoundedPriorityQueue<T>``
-------------------------------

This class represents a priority queue with a limited number of priority levels.

```C#
var queue = new AsyncBoundedPriorityQueue<int>( priorityLevels: 3 );

queue.Add( 1000 ); //  item is added at the lowest priority by default
queue.AddTopPriority( 42 );
queue.AddLowPriority( 999 );
queue.Add( 16, priority: 1 );

//  42 16 1000 999
while ( true )
{
  Debug.Write( await queue.TakeAsync() );
  Debug.Write( " " );
}
```

``AsyncBatchQueue<T>``
------------------

This class is a bit more complex. Just like AsyncQueue<T>, it allows you to add items synchronously and retreive them asynchronously, but the difference is you consume them in batches of the specified size.

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( batchSize: 3 );
queue.Add( 42 );
queue.Add( 64 );
queue.Add( 128 );

CancellationTokenSource cancelSource = new CancellationTokenSource();
//	this will asynchronously return a batch of 3 items
IReadOnlyList<int> batch = await queue.TakeAsync( cancelSource.Token );
```

There's a constructor overload that allows you to specify a time period to wait before the pending items are flushed and a batch is made available for consuming, even if the batch size is not reached yet.

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( 9999, TimeSpan.FromSeconds( 5 ) );
queue.Add( 42 );

//	this will asynchronously return a batch of 1 item in 5 seconds
IReadOnlyList<int> batch = await queue.TakeAsync();
```
