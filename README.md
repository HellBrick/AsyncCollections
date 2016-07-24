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
	ValueTask<T> TakeAsync( CancellationToken cancellationToken );

	int AwaiterCount { get; } // An amount of pending item requests
}
```

A copy of corefx `ValueTask<T>` implementation is used at the moment, it's going to be replaced by the original one when 2.0 is released.

## AsyncQueue and AsyncStack

These classes provide queue- and stack-based implementations of `IAsyncCollection<T>`.

```C#
AsyncQueue<int> queue = new AsyncQueue<int>();
ValueTask<int> itemTask = queue.TakeAsync();
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
* `BlockingCollection` (this isn't really asynchronous and will starve the thread pool on large consumer count, but still is an interesting thing to compare against)
* `System.Threading.Tasks.Dataflow.BufferBlock`

```
                                      Method | ConsumerTasks | ProducerTasks |          Median |        StdDev |    Gen 0 | Gen 1 | Gen 2 | Bytes Allocated/Op |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             1 |             1 |     914.6110 us |     4.6121 us |     3,73 |  0,39 |     - |          57 889,00 |
          AsyncCollection( ConcurrentQueue ) |             1 |             1 |     969.8161 us |     4.2769 us |     2,37 |     - |     - |          31 965,97 |
                Nito.AsyncEx.AsyncCollection |             1 |             1 |  67,751.7260 us |   104.9323 us |   580,78 |  0,98 |     - |       7 446 934,72 |
        System.Concurrent.BlockingCollection |             1 |             1 |   2,108.3728 us |    17.0404 us |     2,25 |     - |     - |          35 318,88 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             1 |   1,673.1337 us |     7.6434 us |    19,88 |     - |     - |         263 489,31 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             1 |             3 |   2,527.1599 us |     8.7526 us |     3,16 |  1,13 |     - |          71 401,25 |
          AsyncCollection( ConcurrentQueue ) |             1 |             3 |   2,904.8245 us |    14.8086 us |     5,02 |  0,94 |     - |          91 164,24 |
                Nito.AsyncEx.AsyncCollection |             1 |             3 |  82,357.5946 us | 2,624.8432 us |   704,33 | 36,10 |     - |      10 253 908,07 |
        System.Concurrent.BlockingCollection |             1 |             3 |   6,337.2439 us |    64.5049 us |     4,56 |     - |     - |          78 099,77 |
 System.Threading.Tasks.Dataflow.BufferBlock |             1 |             3 |   3,738.7718 us |    48.2398 us |    90,86 |     - |     - |       1 192 165,45 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             1 |   1,393.2974 us |    89.1313 us |     5,84 |  0,90 |     - |         100 325,39 |
          AsyncCollection( ConcurrentQueue ) |             3 |             1 |   5,931.3962 us |   655.4277 us |    98,69 |  6,88 |  1,18 |       1 662 971,75 |
                Nito.AsyncEx.AsyncCollection |             3 |             1 |  66,368.2408 us |    83.2923 us |   559,00 |  1,00 |     - |       7 175 669,84 |
        System.Concurrent.BlockingCollection |             3 |             1 |   2,506.8905 us |    15.4637 us |     2,09 |     - |     - |          29 234,91 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             1 |   1,680.4107 us |    13.3545 us |    19,07 |     - |     - |         273 553,48 |
-------------------------------------------- |-------------- |-------------- |---------------- |-------------- |--------- |------ |------ |------------------- |
                                  AsyncQueue |             3 |             3 |   2,673.4342 us |    18.6929 us |     4,28 |  1,17 |     - |          89 555,79 |
          AsyncCollection( ConcurrentQueue ) |             3 |             3 |   3,304.1851 us |    40.6244 us |     6,69 |  0,37 |     - |         113 443,76 |
                Nito.AsyncEx.AsyncCollection |             3 |             3 | 249,474.1560 us | 1,340.5204 us | 1 275,00 |  1,00 |     - |      16 685 767,14 |
        System.Concurrent.BlockingCollection |             3 |             3 |   6,826.4026 us |    99.6510 us |     4,37 |     - |     - |          80 584,81 |
 System.Threading.Tasks.Dataflow.BufferBlock |             3 |             3 |   3,901.0241 us |    55.7299 us |    90,86 |     - |     - |       1 243 432,93 |
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
