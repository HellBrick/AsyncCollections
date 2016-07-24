# What is it and why should I care?

`AsyncCollections` is a library that contains a bunch of fast lock-free thread-safe producer-consumer collections tailored for asynchronous usage. Think `System.Collections.Concurrent` but fully `async`-ready.

# Nuget package

[https://www.nuget.org/packages/AsyncCollections/](https://www.nuget.org/packages/AsyncCollections/)

# API

## IAsyncCollection

Most of collections here implement this interface (I'd say it pretty much describes what this library is all about):

```C#
public interface IAsyncCollection<T> : IReadOnlyCollection<T>
{
	void Add( T item );
	Task<T> TakeAsync( CancellationToken cancellationToken );

	int AwaiterCount { get; } // An amount of pending item requests
}
```

## AsyncQueue and AsyncStack

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

## AsyncCollection

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

## AsyncBoundedPriorityQueue

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

## AsyncBatchQueue

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

There's a bunch of scenarios when you might want to make items available for consumption even if the specified batch size is not reached yet. First of all, you can do it manually by calling `Flush`:

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( 9999 );
queue.Add( 42 );
queue.Flush();

// This will return a batch of 1 item.
IReadOnlyList<int> batch = await queue.TakeAsync();
```

Second, there's a typical case to flush pending items when a certain amount of time has passed. You can use a `WithFlushEvery` extension method to achieve this:

```C#
AsyncBatchQueue<int> queue = new AsyncBatchQueue<int>( 10 );

using ( TimerAsyncBatchQueue<int> timerQueue = queue.WithFlushEvery( TimeSpan.FromSeconds( 5 ) ) )
{
	timerQueue.Add( 42 );

	// This will asynchronously return a batch of 1 item in 5 seconds.
	IReadOnlyList<int> batch = await timerQueue.TakeAsync();
}

// Disposing the TimerAsyncBatchQueue simply disposes the inner timer.
// You can continue using the original queue though.
```

If the queue has no pending items, `Flush` won't do anything, so you don't have to worry about creating a lot of empty batches when doing manual/timer flushing.

# Benchmarks

The idea behind all the benchmarks is to measure how long it takes to add a fixed amount of items to the collection and them to take them back, using different amount of producer and consumer tasks. The results below were measured by using this configuration:

```ini
BenchmarkDotNet=v0.9.6.0
OS=Microsoft Windows NT 6.2.9200.0
Processor=Intel(R) Core(TM) i5-2500 CPU @ 3.30GHz, ProcessorCount=4
Frequency=3239558 ticks, Resolution=308.6841 ns, Timer=TSC
HostCLR=MS.NET 4.0.30319.42000, Arch=64-bit RELEASE [RyuJIT]
JitModules=clrjit-v4.6.1080.0

Type=AsyncQueueBenchmark  Mode=Throughput  Platform=X64  
Jit=RyuJit  LaunchCount=1  
```

## AsyncQueue benchmark

There are multiple ways to achieve the functionality of `AsyncQueue`:

* `AsyncQueue` itself
* `AsyncCollection` that wraps `ConcurrentQueue`
* `Nito.AsyncEx.AsyncCollection` (https://github.com/StephenCleary/AsyncEx) that wraps `ConcurrentQueue`
* `BlockingCollection` + `Task.FromResult` (this isn't really asynchronous and will starve the thread pool on large consumer count, but still is an interesting thing to compare against)
* `System.Threading.Tasks.Dataflow.BufferBlock`

```
                                      Method | ConsumerTasks | ProducerTasks |          Median |        StdDev |    Gen 0 | Gen 1 | Gen 2 | Bytes Allocated/Op |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             1 |             1 |     667.1741 us |     8.1260 us |    32,18 |     - |     - |         489 714,62 |
          AsyncCollection( ConcurrentQueue ) |             1 |             1 |   1,003.4241 us |     4.8941 us |    23,91 |  0,20 |     - |         337 561,10 |
                Nito.AsyncEx.AsyncCollection |             1 |             1 |  63,391.2878 us |   140.8527 us |   572,53 |  0,97 |     - |       7 461 726,56 |
        System.Concurrent.BlockingCollection |             1 |             1 |   2,167.5751 us |     5.4851 us |    19,07 |     - |     - |         250 087,18 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             1 |   1,550.5462 us |     3.5019 us |    19,48 |  0,09 |     - |         263 424,62 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             1 |             3 |   2,028.5381 us |    11.8484 us |    52,13 |  0,53 |     - |         715 022,97 |
          AsyncCollection( ConcurrentQueue ) |             1 |             3 |   3,187.5168 us |    36.9967 us |    82,03 |  2,80 |     - |       1 205 906,60 |
                Nito.AsyncEx.AsyncCollection |             1 |             3 |  84,958.9944 us | 3,457.0669 us |   542,00 | 29,00 |     - |       8 058 962,10 |
        System.Concurrent.BlockingCollection |             1 |             3 |   6,481.9880 us |     8.3425 us |    67,57 | 14,53 |     - |       1 245 211,75 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             3 |   3,319.2262 us |    27.6294 us |    77,70 |     - |     - |       1 036 270,57 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             1 |   1,326.0813 us |    23.8702 us |    26,95 |  1,92 |  0,09 |         514 707,98 |
          AsyncCollection( ConcurrentQueue ) |             3 |             1 |   4,991.7558 us |   454.3432 us |    93,60 |  3,75 |  0,67 |       1 591 833,55 |
                Nito.AsyncEx.AsyncCollection |             3 |             1 |  67,086.5496 us |   207.4014 us |   585,24 |  0,94 |     - |       7 631 964,76 |
        System.Concurrent.BlockingCollection |             3 |             1 |   2,516.6972 us |    19.7043 us |    21,62 |  0,03 |     - |         325 639,16 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             1 |   1,555.7353 us |     6.4511 us |    25,97 |  0,12 |     - |         375 659,42 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             3 |   2,218.7625 us |    48.0594 us |    51,16 |  2,18 |     - |         795 974,79 |
          AsyncCollection( ConcurrentQueue ) |             3 |             3 |   3,627.3078 us |    61.3965 us |    77,70 |  2,71 |     - |       1 304 593,66 |
                Nito.AsyncEx.AsyncCollection |             3 |             3 | 261,696.1702 us | 2,249.4580 us | 1 324,63 |  0,98 |     - |      17 586 437,62 |
        System.Concurrent.BlockingCollection |             3 |             3 |   7,049.6346 us |   158.8700 us |    53,18 |  5,65 |     - |         948 178,60 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             3 |   3,437.2353 us |    99.1422 us |    84,20 |     - |     - |       1 164 906,10 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
```

## AsyncBatchQueue benchmark

There are less alternatives to `AsyncBatchQueue` exist in the wild that I know of. As a matter of fact, the only thing I can come up with is `System.Threading.Tasks.Dataflow.BatchBlock`, so here it is:

```
                                     Method | ConsumerTasks | ProducerTasks |        Median |     StdDev |  Gen 0 | Gen 1 | Gen 2 | Bytes Allocated/Op |
------------------------------------------- |-------------- |-------------- |-------------- |----------- |------- |------ |------ |------------------- |
                            AsyncBatchQueue |             1 |             1 |   596.4135 us |  2.5802 us | 168,31 |  9,45 |     - |          74 545,44 |
 System.Threading.Tasks.Dataflow.BatchBlock |             1 |             1 |   898.6758 us |  2.0544 us | 204,51 |     - |     - |          86 408,56 |
------------------------------------------- |-------------- |-------------- |-------------- |----------- |------- |------ |------ |------------------- |
                            AsyncBatchQueue |             1 |             3 | 1,917.7758 us | 18.4080 us | 239,00 | 64,00 |  2,00 |         158 864,18 |
 System.Threading.Tasks.Dataflow.BatchBlock |             1 |             3 | 2,455.5698 us | 85.2168 us | 519,75 |     - |     - |         215 718,12 |
------------------------------------------- |-------------- |-------------- |-------------- |----------- |------- |------ |------ |------------------- |
                            AsyncBatchQueue |             3 |             1 |   891.2680 us |  8.9186 us | 219,30 | 12,22 |     - |         108 489,50 |
 System.Threading.Tasks.Dataflow.BatchBlock |             3 |             1 |   906.1295 us |  3.7556 us | 267,00 |     - |     - |         120 142,84 |
------------------------------------------- |-------------- |-------------- |-------------- |----------- |------- |------ |------ |------------------- |
                            AsyncBatchQueue |             3 |             3 | 2,170.9173 us | 16.3702 us | 373,78 | 93,68 | 13,91 |         266 014,53 |
 System.Threading.Tasks.Dataflow.BatchBlock |             3 |             3 | 2,455.8641 us | 17.9906 us | 525,57 |     - |     - |         223 575,37 |
------------------------------------------- |-------------- |-------------- |-------------- |----------- |------- |------ |------ |------------------- |
```
