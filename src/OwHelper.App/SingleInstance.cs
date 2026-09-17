using System;
using System.Threading;

namespace OwHelper;

public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;

    public SingleInstance(string name)
    {
        mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        Acquired = createdNew;
    }

    public bool Acquired { get; }

    public void Dispose()
    {
        if (Acquired)
        {
            try { mutex.ReleaseMutex(); } catch { }
        }
        mutex.Dispose();
    }
}

