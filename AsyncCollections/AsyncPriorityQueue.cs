using System.Collections.Generic;

namespace HellBrick.Collections
{
    public class AsyncPriorityQueue<T> : AsyncCollection<T>
    {
        public AsyncPriorityQueue(IEnumerable<T> items = null, IComparer<T> comparer = null)
            : base(new ConcurrentPriorityQueue<T>(items, comparer)) { }
    }
}
