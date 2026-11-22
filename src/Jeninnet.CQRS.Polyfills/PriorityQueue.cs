//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if NETSTANDARD2_0 || NET462
namespace System.Collections.Generic;

/// <summary>netstandard2.0 priority queue used by the deferred pump.</summary>
/// <typeparam name="TElement">The queued element type.</typeparam>
/// <typeparam name="TPriority">The priority type.</typeparam>
internal sealed class PriorityQueue<TElement, TPriority> {
    private readonly SortedDictionary<TPriority, Queue<TElement>> _queues = new(Comparer<TPriority>.Default);
    private int _count;

    /// <summary>Gets the number of queued elements.</summary>
    public int Count => _count;

    /// <summary>Removes and returns the element with the lowest priority.</summary>
    /// <returns>The element.</returns>
    public TElement Dequeue() {
        if(!TryDequeue(out var element, out _)) {
            throw new InvalidOperationException("The priority queue is empty.");
        }

        return element;
    }

    /// <summary>Adds an element with a priority.</summary>
    /// <param name="element">The element.</param>
    /// <param name="priority">The priority.</param>
    public void Enqueue(TElement element, TPriority priority) {
        if(!_queues.TryGetValue(priority, out var queue)) {
            queue = new Queue<TElement>();
            _queues.Add(priority, queue);
        }

        queue.Enqueue(element);
        _count++;
    }

    /// <summary>Removes and returns the element with the lowest priority.</summary>
    /// <param name="element">The removed element.</param>
    /// <param name="priority">The removed element's priority.</param>
    /// <returns><see langword="true"/> when an element was removed.</returns>
    public bool TryDequeue(out TElement element, out TPriority priority) {
        if(_queues.Count is 0) {
            element = default!;
            priority = default!;

            return false;
        }

        var first = _queues.First();
        element = first.Value.Dequeue();
        priority = first.Key;
        _count--;

        if(first.Value.Count is 0) {
            _queues.Remove(first.Key);
        }

        return true;
    }

    /// <summary>Returns the element with the lowest priority without removing it.</summary>
    /// <param name="element">The element.</param>
    /// <param name="priority">The element's priority.</param>
    /// <returns><see langword="true"/> when an element exists.</returns>
    public bool TryPeek(out TElement element, out TPriority priority) {
        if(_queues.Count is 0) {
            element = default!;
            priority = default!;

            return false;
        }

        var first = _queues.First();
        element = first.Value.Peek();
        priority = first.Key;

        return true;
    }
}
#endif
