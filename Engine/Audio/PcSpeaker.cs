using System.Collections.Concurrent;

namespace ExoProxy.Engine.Audio;

// Retro beeps via Console.Beep. Console.Beep BLOCKS the calling thread for the
// full duration and is monophonic, so we run it on one dedicated background thread
// fed by a queue. The game loop never blocks; the tones simply play back-to-back in
// order — which is exactly the modem-warble we want during the boot handshake.
public sealed class PcSpeaker : IDisposable
{
    private readonly BlockingCollection<(int Freq, int Ms)> _queue = new();
    private readonly Thread _thread;

    public PcSpeaker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "PcSpeaker" };
        _thread.Start();
    }

    public void Beep(int frequencyHz, int durationMs)
    {
        // freq <= 0 is a silent rest (lets a sequence have gaps); otherwise clamp to
        // Console.Beep's valid 37..32767 Hz so a stray value can't throw.
        int freq = frequencyHz <= 0 ? 0 : Math.Clamp(frequencyHz, 37, 32767);
        int ms   = Math.Max(1, durationMs);
        try { _queue.TryAdd((freq, ms)); }
        catch (InvalidOperationException) { /* queue already closed on shutdown */ }
    }

    private void Run()
    {
        foreach (var (freq, ms) in _queue.GetConsumingEnumerable())
        {
            if (freq <= 0) { Thread.Sleep(ms); continue; }   // a rest between tones
            // Console.Beep is Windows-only; elsewhere we simply stay silent.
            if (!OperatingSystem.IsWindows()) continue;
            try { Console.Beep(freq, ms); }
            catch { }
        }
    }

    public void Dispose()
    {
        // Stop accepting tones and let the worker drain. We don't join the thread,
        // so a long tone in flight can't delay program shutdown.
        _queue.CompleteAdding();
    }
}
