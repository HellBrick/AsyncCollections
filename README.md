AsyncCollections
================

Description
-----------

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

//	this will asynchronously return a batch of 1 item after the specified flushPeriod has passed
IReadOnlyList<int> batch = await queue.TakeAsync();
```
