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
                                  AsyncQueue |             1 |             1 |     620.9217 us |     6.3803 us |    31,44 |  0,46 |     - |         484 926,83 |
          AsyncCollection( ConcurrentQueue ) |             1 |             1 |   1,034.5017 us |     9.7968 us |    25,94 |  0,19 |     - |         366 770,81 |
                Nito.AsyncEx.AsyncCollection |             1 |             1 |  65,425.7464 us |   240.3173 us |   560,86 |  0,95 |     - |       7 311 567,94 |
        System.Concurrent.BlockingCollection |             1 |             1 |   2,200.9260 us |    12.7156 us |    19,38 |     - |     - |         254 866,99 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             1 |   1,569.6152 us |     8.0605 us |    23,09 |  0,11 |     - |         312 697,76 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             1 |             3 |   1,996.4951 us |    50.6842 us |    43,95 | 13,65 |     - |       1 106 731,83 |
          AsyncCollection( ConcurrentQueue ) |             1 |             3 |   3,199.7083 us |    25.0346 us |    84,84 |  2,73 |     - |       1 247 439,14 |
                Nito.AsyncEx.AsyncCollection |             1 |             3 |  81,244.5324 us | 4,193.0515 us |   578,03 | 34,34 |     - |       8 608 820,98 |
        System.Concurrent.BlockingCollection |             1 |             3 |   6,511.0252 us |    19.2980 us |    43,36 |  9,20 |     - |         798 844,08 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             3 |   3,299.0853 us |    38.3530 us |    89,42 |     - |     - |       1 195 081,17 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             1 |   1,390.6797 us |   124.3341 us |    33,24 |  2,11 |  0,06 |         687 242,12 |
          AsyncCollection( ConcurrentQueue ) |             3 |             1 |   3,912.6480 us |   656.9430 us |    85,25 |  4,26 |  0,37 |       1 440 170,71 |
                Nito.AsyncEx.AsyncCollection |             3 |             1 |  66,032.3878 us |   112.9322 us |   541,53 |  0,97 |     - |       7 062 824,16 |
        System.Concurrent.BlockingCollection |             3 |             1 |   2,597.5235 us |    29.7243 us |    18,90 |     - |     - |         287 767,12 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             1 |   1,583.0477 us |    31.8197 us |    31,06 |  0,04 |     - |         451 753,48 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             3 |   2,194.5087 us |    67.6814 us |    43,95 | 15,75 |     - |       1 216 582,05 |
          AsyncCollection( ConcurrentQueue ) |             3 |             3 |   3,688.7965 us |    67.8031 us |    76,35 |  3,34 |     - |       1 308 573,43 |
                Nito.AsyncEx.AsyncCollection |             3 |             3 | 237,520.5198 us | 1,436.4324 us | 1 269,00 |  1,00 |     - |      17 009 222,65 |
        System.Concurrent.BlockingCollection |             3 |             3 |   7,205.1737 us |   116.0288 us |    68,54 |  9,72 |     - |       1 282 549,78 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             3 |   3,543.9996 us |    43.0873 us |    79,51 |     - |     - |       1 100 384,23 |
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
