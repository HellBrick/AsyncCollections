using System.Collections.Generic;
using System.Linq;

namespace HellBrick.Collections
{
    public class PriorityQueue<T> : ICollection<T>
    {
        private const int K = 4;
        private readonly List<T> items;
        private readonly IComparer<T> comparer;

        public PriorityQueue(IEnumerable<T> items = null, IComparer<T> comparer = null)
        {
            this.comparer = comparer ?? Comparer<T>.Default;
            if (items == null)
                this.items = new List<T>();
            else
            {
                this.items = new List<T>(items);
                this.items.Sort(comparer);
            }
        }

        private void Swap(int i, int j)
        {
            T temp = items[i];
            items[i] = items[j];
            items[j] = temp;
        }

        private void FixUp(int i)
        {
            for (int j; i > 0; Swap(i, j), i = j)
            {
                j = (i - 1) / K;
                if (comparer.Compare(items[i], items[j]) >= 0)
                    return;
            }
        }

        private void FixDown(int i)
        {
            for (int j; i < items.Count; Swap(i, j), i = j)
            {
                j = i;
                for (int k = (i * K + 1); k <= (i * K + K) && k < items.Count; k++)
                {
                    if (comparer.Compare(items[j], items[k]) > 0)
                        j = k;
                }
                if (i == j)
                    return;
            }
        }

        public T Peek()
        {
            return items[0];
        }

        public bool TryPeek(out T value)
        {
            if (items.Count > 0)
            {
                value = items[0];
                return true;
            }
            else
            {
                value = default(T);
                return false;
            }
        }

        public T Take()
        {
            var value = items[0];
            Swap(0, items.Count - 1);
            items.RemoveAt(items.Count - 1);
            if (items.Count > 0)
                FixDown(0);
            return value;
        }

        public bool TryTake(out T value)
        {
            if (items.Count > 0)
            {
                value = Take();
                return true;
            }
            else
            {
                value = default(T);
                return false;
            }
        }

        public void Add(T item)
        {
            items.Add(item);
            FixUp(items.Count - 1);
        }

        public bool Remove(T item)
        {
            var i = items.IndexOf(item);
            if (i == -1) return false;

            Swap(i, items.Count - 1);
            items.RemoveAt(items.Count - 1);
            if (i == items.Count) return true;

            var cmp = comparer.Compare(item, items[i]);
            if (cmp < 0)
                FixDown(i);
            else if (cmp > 0)
                FixUp(i);

            return true;
        }

        #region Mics

        public void Clear()
        {
            items.Clear();
        }

        public bool Contains(T item)
        {
            return items.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            items.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return items.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return items.GetEnumerator();
        }
        #endregion
    }
}
