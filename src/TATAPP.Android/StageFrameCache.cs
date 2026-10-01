using TATAPP.Core;

namespace TATAPP.AndroidApp;

internal sealed class StageFrameCache
{
    private readonly long maximumBytes;
    private readonly Dictionary<(string DocumentId, TattooStageKind Stage), CacheEntry> entries = [];
    private readonly LinkedList<(string DocumentId, TattooStageKind Stage)> lru = [];
    private long usedBytes;

    public StageFrameCache(long maximumBytes) =>
        this.maximumBytes = Math.Max(8L * 1024 * 1024, maximumBytes);

    public bool TryGet(string documentId, TattooStageKind stage, out ImageFrame frame)
    {
        lock (entries)
        {
            var key = (documentId, stage);
            if (!entries.TryGetValue(key, out var entry))
            {
                frame = null!;
                return false;
            }
            lru.Remove(entry.Node);
            lru.AddFirst(entry.Node);
            frame = entry.Frame;
            return true;
        }
    }

    public void Put(string documentId, TattooStageKind stage, ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (entries)
        {
            var key = (documentId, stage);
            if (entries.Remove(key, out var old))
            {
                lru.Remove(old.Node);
                usedBytes -= old.Bytes;
            }
            var bytes = frame.Pixels.LongLength;
            if (bytes > maximumBytes) return;
            var node = lru.AddFirst(key);
            entries.Add(key, new CacheEntry(frame, bytes, node));
            usedBytes += bytes;
            while (usedBytes > maximumBytes && lru.Last is { } last)
            {
                var victim = last.Value;
                lru.RemoveLast();
                if (!entries.Remove(victim, out var removed)) continue;
                usedBytes -= removed.Bytes;
            }
        }
    }

    public void Clear()
    {
        lock (entries)
        {
            entries.Clear();
            lru.Clear();
            usedBytes = 0;
        }
    }

    private sealed record CacheEntry(ImageFrame Frame, long Bytes,
        LinkedListNode<(string DocumentId, TattooStageKind Stage)> Node);
}
