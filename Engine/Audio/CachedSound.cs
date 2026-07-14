using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ExoProxy.Engine.Audio;

// A sound file decoded fully into memory once, resampled to the mixer's format so it
// can be replayed instantly with no disk/decoder latency. Used for short SFX and for
// short ambient loops. (Long music tracks are STREAMED instead — see MusicPlayer.)
public sealed class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; }

    public CachedSound(string filePath, int targetSampleRate, int targetChannels)
    {
        using var reader = new AudioFileReader(filePath);

        // Match the mixer: resample, then fold mono up to stereo if needed.
        ISampleProvider provider = reader;
        if (provider.WaveFormat.SampleRate != targetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, targetSampleRate);
        if (provider.WaveFormat.Channels == 1 && targetChannels == 2)
            provider = new MonoToStereoSampleProvider(provider);

        WaveFormat = provider.WaveFormat;

        var samples = new List<float>();
        var buffer = new float[targetSampleRate * Math.Max(1, provider.WaveFormat.Channels)];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());

        AudioData = samples.ToArray();
    }
}

// Plays a CachedSound for a controlled length: a plain one-shot, a fixed number of
// whole loops, or an exact sample-count (for "play for N seconds"). Loops the source
// to fill when asked, applies an optional fade in at the start and fade out at the end,
// and ends (Read returns 0 → mixer auto-removes it) once it has emitted its target.
public sealed class EffectSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;
    private readonly bool _loopFill;
    private readonly long _total;     // total samples to emit
    private readonly long _fadeIn;    // samples
    private readonly long _fadeOut;   // samples
    private long _emitted;
    private long _srcPos;

    public EffectSampleProvider(CachedSound sound, long totalSamples, bool loopFill,
                                long fadeInSamples, long fadeOutSamples)
    {
        _sound    = sound;
        _loopFill = loopFill;
        _total    = Math.Max(0, totalSamples);
        _fadeIn   = Math.Clamp(fadeInSamples, 0, _total);
        _fadeOut  = Math.Clamp(fadeOutSamples, 0, _total);
    }

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var data = _sound.AudioData;
        long len = data.Length;
        if (len == 0) return 0;

        int produced = 0;
        while (produced < count && _emitted < _total)
        {
            if (_srcPos >= len)
            {
                if (_loopFill) _srcPos = 0;
                else break;
            }

            long chunk = Math.Min(Math.Min(len - _srcPos, _total - _emitted), count - produced);
            for (long i = 0; i < chunk; i++)
            {
                long p = _emitted + i;
                float g = 1f;
                if (_fadeIn  > 0 && p < _fadeIn)            g *= (float)((double)p / _fadeIn);
                if (_fadeOut > 0 && p >= _total - _fadeOut) g *= (float)((double)(_total - 1 - p) / _fadeOut);
                buffer[offset + produced + i] = data[_srcPos + i] * g;
            }

            _srcPos  += chunk;
            _emitted += chunk;
            produced += (int)chunk;
        }

        return produced;
    }
}

// Plays a CachedSound forever (always returns a full buffer, so the mixer never drops
// it). Stop it by removing it from the mixer. Used for ambient loops (wrapped in a fade
// provider for smooth starts/stops and crossfades).
public sealed class LoopingSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;
    private long _position;

    public LoopingSampleProvider(CachedSound sound) => _sound = sound;

    public WaveFormat WaveFormat => _sound.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sound.AudioData.Length == 0) { Array.Clear(buffer, offset, count); return count; }

        int filled = 0;
        while (filled < count)
        {
            if (_position >= _sound.AudioData.Length) _position = 0;
            long available = _sound.AudioData.Length - _position;
            int n = (int)Math.Min(available, count - filled);
            Array.Copy(_sound.AudioData, _position, buffer, offset + filled, n);
            _position += n;
            filled += n;
        }
        return filled;
    }
}
