using TATAPP.Core.Workflow;

namespace TATAPP.Core.Caching;

/// <summary>
/// Keeps an acquired cache value alive even if its key is invalidated or evicted.
/// </summary>
public sealed class CacheLease<TValue> : IDisposable
{
    private readonly Action release;
    private int disposed;

    internal CacheLease(TValue value, Action release)
    {
        Value = value;
        this.release = release;
    }

    public TValue Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return field;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) release();
    }
}

/// <summary>
/// A thread-safe least-recently-used cache constrained by owned bytes rather than item
/// count. Callers borrow values through leases, so eviction never disposes a value while
/// it is in use. The cache takes ownership of every value passed to <see cref="Set"/>.
/// </summary>
public sealed class ByteBudgetLruCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private sealed class Entry(TKey key, TValue value, long sizeBytes)
    {
        public TKey Key { get; } = key;
        public TValue Value { get; } = value;
        public long SizeBytes { get; set; } = sizeBytes;
        public int LeaseCount { get; set; }
        public bool Removed { get; set; }
        public bool Released { get; set; }
    }

    private readonly object synchronization = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recency = [];
    private readonly Func<TValue, long> sizeOf;
    private readonly Action<TValue> release;
    private long currentBytes;
    private bool disposed;

    public ByteBudgetLruCache(long maximumBytes, Func<TValue, long> sizeOf,
        Action<TValue>? release = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentNullException.ThrowIfNull(sizeOf);
        MaximumBytes = maximumBytes;
        this.sizeOf = sizeOf;
        this.release = release ?? (value =>
        {
            if (value is IDisposable disposable) disposable.Dispose();
        });
    }

    public long MaximumBytes { get; }

    public long CurrentBytes
    {
        get { lock (synchronization) return currentBytes; }
    }

    public int Count
    {
        get { lock (synchronization) return entries.Count; }
    }

    public bool TryAcquire(TKey key, out CacheLease<TValue>? lease)
    {
        lock (synchronization)
        {
            ThrowIfDisposed();
            if (!entries.TryGetValue(key, out var node))
            {
                lease = null;
                return false;
            }

            recency.Remove(node);
            recency.AddFirst(node);
            node.Value.LeaseCount = checked(node.Value.LeaseCount + 1);
            var entry = node.Value;
            lease = new(entry.Value, () => ReleaseLease(entry));
            return true;
        }
    }

    /// <summary>
    /// Transfers ownership of <paramref name="value"/> to the cache. An oversized value
    /// is released immediately unless it is the same instance already owned under this key;
    /// that entry is first removed and is released only after outstanding leases end.
    /// </summary>
    public bool Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sizeBytes = sizeOf(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        var removed = new List<TValue>();
        var accepted = true;

        lock (synchronization)
        {
            ThrowIfDisposed();
            entries.TryGetValue(key, out var existingNode);
            var isSameInstance = existingNode is not null &&
                                 ReferenceEquals(existingNode.Value.Value, value);

            if (sizeBytes > MaximumBytes)
            {
                if (isSameInstance)
                    RemoveNode(existingNode!, removed);
                else
                    removed.Add(value);
                accepted = false;
            }
            else
            {
                if (existingNode is not null)
                {
                    if (isSameInstance)
                    {
                        currentBytes -= existingNode.Value.SizeBytes;
                        existingNode.Value.SizeBytes = sizeBytes;
                        recency.Remove(existingNode);
                        recency.AddFirst(existingNode);
                    }
                    else
                    {
                        RemoveNode(existingNode, removed);
                        var replacement = new Entry(key, value, sizeBytes);
                        entries.Add(key, recency.AddFirst(replacement));
                    }
                }
                else
                {
                    var added = new Entry(key, value, sizeBytes);
                    entries.Add(key, recency.AddFirst(added));
                }

                EvictUntilWithinBudget(removed);
                // Addition uses subtraction-safe eviction checks; reaching this assignment
                // cannot overflow because both values are within MaximumBytes.
                currentBytes += sizeBytes;
            }
        }

        ReleaseAll(removed);
        return accepted;
    }

    public int Invalidate(Func<TKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var removed = new List<TValue>();
        int count;
        lock (synchronization)
        {
            ThrowIfDisposed();
            // Evaluate first. If caller policy throws, the cache remains untouched and
            // therefore cannot leak values already detached from its bookkeeping.
            var selected = recency.Where(entry => predicate(entry.Key)).ToArray();
            count = selected.Length;
            foreach (var entry in selected)
                if (entries.TryGetValue(entry.Key, out var node) && ReferenceEquals(node.Value, entry))
                    RemoveNode(node, removed);
        }
        ReleaseAll(removed);
        return count;
    }

    public void Clear() => Invalidate(_ => true);

    public void Dispose()
    {
        var removed = new List<TValue>();
        lock (synchronization)
        {
            if (disposed) return;
            disposed = true;
            while (recency.Last is { } node) RemoveNode(node, removed);
        }
        ReleaseAll(removed);
    }

    private void EvictUntilWithinBudget(List<TValue> removed)
    {
        while (currentBytes > MaximumBytes - recency.First!.Value.SizeBytes &&
               recency.Last is { } oldest && !ReferenceEquals(oldest, recency.First))
            RemoveNode(oldest, removed);

        // If a same-instance replacement grew, it may itself be the least-recently-used
        // entry and the loop above deliberately preserves it. Its size already passed the
        // individual MaximumBytes gate, so it is safe after older entries are removed.
    }

    private void RemoveNode(LinkedListNode<Entry> node, List<TValue> readyToRelease)
    {
        recency.Remove(node);
        entries.Remove(node.Value.Key);
        currentBytes -= node.Value.SizeBytes;
        node.Value.Removed = true;
        QueueReleaseIfUnleased(node.Value, readyToRelease);
    }

    private static void QueueReleaseIfUnleased(Entry entry, List<TValue> readyToRelease)
    {
        if (entry.LeaseCount != 0 || entry.Released) return;
        entry.Released = true;
        readyToRelease.Add(entry.Value);
    }

    private void ReleaseLease(Entry entry)
    {
        TValue? readyToRelease = default;
        var shouldRelease = false;
        lock (synchronization)
        {
            if (entry.LeaseCount <= 0)
                throw new InvalidOperationException("A cache lease was released more than once.");
            entry.LeaseCount--;
            if (entry.Removed && entry.LeaseCount == 0 && !entry.Released)
            {
                entry.Released = true;
                readyToRelease = entry.Value;
                shouldRelease = true;
            }
        }
        if (shouldRelease) ReleaseAll([readyToRelease!]);
    }

    private void ReleaseAll(IEnumerable<TValue> values)
    {
        Exception? firstFailure = null;
        foreach (var value in values)
        {
            try { release(value); }
            catch (Exception exception) { firstFailure ??= exception; }
        }
        if (firstFailure is not null)
            throw new InvalidOperationException("A cached value could not be released.", firstFailure);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

public readonly record struct StageRenderCacheKey(
    string SourceFingerprint,
    TattooStageKind Stage,
    string? AnatomicalStateKey)
{
    public static StageRenderCacheKey Create(string sourceFingerprint, TattooStage stage,
        AnatomicalWorkflowState anatomy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(anatomy);
        anatomy.Validate();
        var canonicalStage = TattooStageNavigator.Get(stage.Kind);
        return new(sourceFingerprint.Trim(), canonicalStage.Kind,
            canonicalStage.IsAnatomicalPlacement ? anatomy.DescriptionCacheKey : null);
    }
}
