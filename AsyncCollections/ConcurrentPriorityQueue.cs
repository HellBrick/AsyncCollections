using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HellBrick.Collections
{
    public class ConcurrentPriorityQueue<T> : IProducerConsumerCollection<T>
    {
        private readonly PriorityQueue<T> impl;
        private readonly ConcurrentQueue<T> newItems;
        private volatile int count;
        private int adding;

        public ConcurrentPriorityQueue(IEnumerable<T> items = null, IComparer<T> comparer = null)
        {
            impl = new PriorityQueue<T>(items, comparer);
            newItems = new ConcurrentQueue<T>();
            count = impl.Count;
        }

        public bool TryAdd(T item)
        {
            newItems.Enqueue(item);

            while (newItems.Count > 0 && Interlocked.Exchange(ref adding, 1) == 0)
                lock (impl)
                {
                    while (newItems.TryDequeue(out item))
                        impl.Add(item);
                    count = impl.Count;
                    adding = 0;
                };
            return true;
        }

        public bool TryTake(out T item)
        {
            lock (impl)
            {
                var result = impl.TryTake(out item);
                count = impl.Count;
                return result;
            }
        }

        public bool TryPeek(out T item)
        {
            lock (impl) return impl.TryPeek(out item);
        }

        public void CopyTo(T[] array, int index)
        {
            lock (impl) impl.CopyTo(array, index);
        }

        public T[] ToArray()
        {
            lock (impl)
            {
                var result = new T[impl.Count];
                impl.CopyTo(result, 0);
                return result;
            }
        }

        #region Mics
        public IEnumerator<T> GetEnumerator()
        {
            lock (impl) return impl.ToList().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (impl) return impl.ToList().GetEnumerator();
        }

        public void CopyTo(Array array, int index)
        {
            var temp = array as T[];
            if (temp != null)
                CopyTo(temp, index);
            else
            {
                temp = ToArray();
                Array.Copy(temp, 0, array, index, temp.Length);
            }
        }

        public int Count
        {
            get { return count; }
        }

        public bool IsSynchronized
        {
            get { return true; }
        }

        public object SyncRoot
        {
            get { return impl; }
        }
        #endregion
    }
}
