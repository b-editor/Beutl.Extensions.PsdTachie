using Beutl.Media;

namespace Beutl.Extensions.PsdTachie.Graphics;

/// <summary>
/// Keeps the composited frames of recently used layer states. Mouth and eye shapes cycle through a handful of
/// states, so after the first pass playback only switches between cached bitmaps.
/// </summary>
internal sealed class PsdFrameCache : IDisposable
{
    private const int Capacity = 24;

    // An evicted frame may still be referenced by a render node recorded for an earlier frame, so it is
    // disposed only after this many further updates.
    private const int RetireAfterUpdates = 8;

    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();
    private readonly Queue<(Bitmap Bitmap, long DisposeAt)> _retired = new();
    private long _updates;

    public Bitmap GetOrCreate(string key, Func<Bitmap> factory)
    {
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bitmap;
        }

        Bitmap bitmap = factory();
        _entries[key] = _lru.AddFirst(new Entry(key, bitmap));
        while (_lru.Count > Capacity)
        {
            LinkedListNode<Entry> last = _lru.Last!;
            _lru.RemoveLast();
            _entries.Remove(last.Value.Key);
            _retired.Enqueue((last.Value.Bitmap, _updates + RetireAfterUpdates));
        }

        return bitmap;
    }

    public void Tick()
    {
        _updates++;
        while (_retired.Count > 0 && _retired.Peek().DisposeAt <= _updates)
        {
            _retired.Dequeue().Bitmap.Dispose();
        }
    }

    public void Clear()
    {
        foreach (Entry entry in _lru)
            _retired.Enqueue((entry.Bitmap, _updates + RetireAfterUpdates));
        _lru.Clear();
        _entries.Clear();
    }

    public void Dispose()
    {
        foreach (Entry entry in _lru)
            entry.Bitmap.Dispose();
        _lru.Clear();
        _entries.Clear();
        while (_retired.Count > 0)
            _retired.Dequeue().Bitmap.Dispose();
    }

    private sealed record Entry(string Key, Bitmap Bitmap);
}
