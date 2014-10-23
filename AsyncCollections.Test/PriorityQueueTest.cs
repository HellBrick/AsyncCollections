using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HellBrick.Collections.Test
{
    [TestClass]
    public class PriorityQueueTest
    {
        [TestMethod]
        public void RandomTest()
        {
            var r = new Random();
            var list = Enumerable.Range(0, 10000).Select(_ => r.Next()).ToList();

            var queue = new PriorityQueue<int>();
            foreach (var item in list)
                queue.Add(item);

            for (var i = 0; i < 100; i++)
                queue.Remove(list[i]);
            list.RemoveRange(0, 100);

            var sorted = new List<int>();
            {
                int item;
                while (queue.TryTake(out item))
                    sorted.Add(item);
            }

            list.Sort();
            Assert.IsTrue(list.SequenceEqual(sorted), "different sequences");
        }
    }
}
