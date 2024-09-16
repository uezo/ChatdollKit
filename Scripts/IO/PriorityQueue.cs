using System.Collections.Generic;
using System.Linq;

namespace ChatdollKit.IO
{
    public class PriorityQueue<T>
    {
        private SortedList<int, Queue<T>> priorityQueues;

        public PriorityQueue()
        {
            priorityQueues = new SortedList<int, Queue<T>>();
        }

        public void Enqueue(T item, int priority)
        {
            if (!priorityQueues.ContainsKey(priority))
            {
                priorityQueues[priority] = new Queue<T>();
            }
            priorityQueues[priority].Enqueue(item);
        }

        public T Dequeue()
        {
            if (priorityQueues.Count == 0)
            {
                throw new System.InvalidOperationException("Queue is empty.");
            }

            var highestPriority = priorityQueues.Keys.Min();
            var queue = priorityQueues[highestPriority];
            var item = queue.Dequeue();

            if (queue.Count == 0)
            {
                priorityQueues.Remove(highestPriority);
            }

            return item;
        }

        public bool IsEmpty()
        {
            return !priorityQueues.Any();
        }

        public List<T> PeekAll()
        {
            var items = new List<T>();
            foreach (var queue in priorityQueues.Values)
            {
                items.AddRange(queue);
            }
            return items;
        }

        public bool Remove(T item)
        {
            foreach (var priority in priorityQueues.Keys.ToList())
            {
                var queue = priorityQueues[priority];

                var tempQueue = new Queue<T>();
                var found = false;

                while (queue.Count > 0)
                {
                    var currentItem = queue.Dequeue();
                    if (!found && currentItem.Equals(item))
                    {
                        found = true;
                        continue;
                    }
                    tempQueue.Enqueue(currentItem);
                }

                if (tempQueue.Count > 0)
                {
                    priorityQueues[priority] = tempQueue;
                }
                else
                {
                    priorityQueues.Remove(priority);
                }

                if (found)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear(int priority = 0)
        {
            if (priority == 0)
            {
                priorityQueues.Clear();
            }
            else
            {
                if (priorityQueues.ContainsKey(priority))
                {
                    priorityQueues.Remove(priority);
                }
            }
        }
    }
}
