using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ExoProxy.Engine.Audio;

// The music "jukebox". Plays tracks one at a time on the music channel — streamed from
// disk, not cached to RAM (tracks are long). Between tracks it waits a gap of silence,
// then picks the next (shuffled, avoiding an immediate repeat). Each track fades in at
// the start and out at the end. Driven by Update(dt) from the game loop.
public sealed class MusicPlayer : IDisposable
{
    private enum State { Idle, Playing, Gap, Stopping }

    private readonly MixingSampleProvider _mixer;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly IReadOnlyList<string> _tracks;
    private readonly bool _shuffle;
    private readonly double _gap;
    private readonly double _fadeIn;
    private readonly double _fadeOut;
    private readonly float _gain;
    private readonly Random _rng = new();

    private State _state = State.Idle;
    private double _timer;
    private int _lastIndex = -1;
    private bool _fadeOutStarted;

    private AudioFileReader? _reader;
    private ISampleProvider? _input;   // what's currently in the mixer
    private FadeInOutSampleProvider? _fade;

    public MusicPlayer(MixingSampleProvider mixer, int sampleRate, int channels,
                       IReadOnlyList<string> tracks, bool shuffle,
                       double gap, double fadeIn, double fadeOut, float gain)
    {
        _mixer      = mixer;
        _sampleRate = sampleRate;
        _channels   = channels;
        _tracks     = tracks;
        _shuffle    = shuffle;
        _gap        = Math.Max(0, gap);
        _fadeIn     = Math.Max(0, fadeIn);
        _fadeOut    = Math.Max(0, fadeOut);
        _gain       = gain;
    }

    public void Start()
    {
        if (_state == State.Idle) StartNext();
    }

    public void Update(double dt)
    {
        switch (_state)
        {
            case State.Playing:
                if (_reader is null) { _state = State.Idle; return; }

                double remaining = SecondsRemaining(_reader);
                if (!_fadeOutStarted && _fadeOut > 0 && remaining <= _fadeOut)
                {
                    _fade?.BeginFadeOut(_fadeOut * 1000.0);
                    _fadeOutStarted = true;
                }

                if (_reader.Position >= _reader.Length)
                {
                    Cleanup();
                    _state = State.Gap;
                    _timer = _gap;
                }
                break;

            case State.Gap:
                _timer -= dt;
                if (_timer <= 0) StartNext();
                break;

            case State.Stopping:
                _timer -= dt;
                if (_timer <= 0) { Cleanup(); _state = State.Idle; }
                break;
        }
    }

    // Fade the current track out and go quiet (used when leaving music behind).
    public void Stop()
    {
        if (_state is State.Playing && _fade is not null)
        {
            _fade.BeginFadeOut(_fadeOut * 1000.0);
            _state = State.Stopping;
            _timer = _fadeOut;
        }
        else
        {
            Cleanup();
            _state = State.Idle;
        }
    }

    private void StartNext()
    {
        int index = PickIndex();
        try
        {
            var reader = new AudioFileReader(_tracks[index]);

            ISampleProvider provider = reader;
            if (provider.WaveFormat.SampleRate != _sampleRate)
                provider = new WdlResamplingSampleProvider(provider, _sampleRate);
            if (provider.WaveFormat.Channels == 1 && _channels == 2)
                provider = new MonoToStereoSampleProvider(provider);
            if (provider.WaveFormat.Channels != _channels)
            {
                reader.Dispose();
                _state = State.Gap;   // skip an odd-format track, try again after the gap
                _timer = _gap;
                return;
            }

            var trimmed = new VolumeSampleProvider(provider) { Volume = _gain };
            var fade = new FadeInOutSampleProvider(trimmed, _fadeIn > 0);
            if (_fadeIn > 0) fade.BeginFadeIn(_fadeIn * 1000.0);

            _reader = reader;
            _fade   = fade;
            _input  = fade;
            _fadeOutStarted = false;
            _lastIndex = index;

            _mixer.AddMixerInput(fade);
            _state = State.Playing;
        }
        catch
        {
            _state = State.Gap;       // unreadable track → wait, then try the next
            _timer = _gap;
        }
    }

    private int PickIndex()
    {
        if (_tracks.Count == 1) return 0;
        if (!_shuffle) return (_lastIndex + 1) % _tracks.Count;

        int index;
        do { index = _rng.Next(_tracks.Count); } while (index == _lastIndex);
        return index;
    }

    private double SecondsRemaining(AudioFileReader reader)
    {
        long remainingBytes = reader.Length - reader.Position;
        int bytesPerSec = reader.WaveFormat.AverageBytesPerSecond;
        return bytesPerSec > 0 ? (double)remainingBytes / bytesPerSec : 0;
    }

    private void Cleanup()
    {
        if (_input is not null) { try { _mixer.RemoveMixerInput(_input); } catch { } }
        _reader?.Dispose();
        _reader = null;
        _input  = null;
        _fade   = null;
    }

    public void Dispose() => Cleanup();
}
