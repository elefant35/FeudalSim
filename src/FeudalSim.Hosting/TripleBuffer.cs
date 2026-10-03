namespace FeudalSim.Hosting;

/// <summary>
/// Lock-free single-writer/single-reader triple buffer (20 §2.4): the writer fills the back buffer and
/// publishes it; the reader always gets the latest complete snapshot and never sees a torn one.
/// </summary>
public sealed class TripleBuffer<T> where T : class
{
    private readonly T[] _buffers;
    private int _back = 0;      // writer-owned
    private int _front = 2;     // reader-owned
    private int _ready = 1;     // shared; bit 0x4 = "fresh data"

    public TripleBuffer(Func<T> factory) => _buffers = [factory(), factory(), factory()];

    /// <summary>The buffer the writer may fill now.</summary>
    public T Back => _buffers[_back];

    /// <summary>Publishes the back buffer and takes the stale one in exchange.</summary>
    public void Publish()
    {
        var old = Interlocked.Exchange(ref _ready, _back | 0x4);
        _back = old & 0x3;
    }

    /// <summary>Returns the latest published snapshot (or the previous one if nothing new arrived).</summary>
    public T ReadLatest()
    {
        if ((Volatile.Read(ref _ready) & 0x4) != 0)
        {
            var old = Interlocked.Exchange(ref _ready, _front);
            _front = old & 0x3;
        }

        return _buffers[_front];
    }
}
