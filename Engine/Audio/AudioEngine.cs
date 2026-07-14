using ExoProxy.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ExoProxy.Engine.Audio;

// Real audio backend. One verb does everything: Play(name) looks the name up in the
// sound bank and does whatever that entry says — bleep, sample, ambient loop, or music
// playlist. Three channel mixers (Music / Ambient / Effects) sit under a master volume:
//
//     music   ─→ musicVolume   ┐
//     ambient ─→ ambientVolume ─┼─→ masterMixer ─→ masterVolume ─→ output device
//     effects ─→ effectsVolume ┘
//
// NAudio init is wrapped in try/catch: with no output device the engine degrades to
// silence for samples — but tone bleeps still play (they're the PC speaker).
public sealed class AudioEngine : IAudioService
{
    private const int SampleRate = 44100;
    private const int Channels   = 2;

    private static readonly string _contentAudioDir =
        Path.Combine(AppContext.BaseDirectory, "Content", "Audio");
    private static readonly string _soundBankPath = Path.Combine(_contentAudioDir, "soundbank.yaml");

    private readonly PcSpeaker _speaker = new();
    private readonly Random _rng = new();
    private readonly object _mixerLock = new();

    private readonly Dictionary<string, CachedSound?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);

    private SoundBank _bank;

    private IWavePlayer? _output;
    private MixingSampleProvider? _musicMixer;
    private MixingSampleProvider? _ambientMixer;
    private MixingSampleProvider? _effectsMixer;
    private VolumeSampleProvider? _musicVolume;
    private VolumeSampleProvider? _ambientVolume;
    private VolumeSampleProvider? _effectsVolume;
    private VolumeSampleProvider? _masterVolume;

    // A faded loop currently playing, keyed by its event NAME so several can run at once
    // (that's how the two ambient beds layer). Channel is kept so a stop knows which
    // mixer to pull it from.
    private sealed class FadedLoop
    {
        public required FadeInOutSampleProvider Fade;
        public required AudioChannel Channel;
        public required double FadeOutSec;
    }
    private readonly Dictionary<string, FadedLoop> _loops = new(StringComparer.OrdinalIgnoreCase);

    private MusicPlayer? _musicPlayer;

    private readonly List<(MixingSampleProvider Mixer, ISampleProvider Input, double RemoveAt)> _pendingRemovals = new();
    private double _clock;

    private bool _ready;

    public AudioEngine(int master, int music, int effects, int ambient)
    {
        _bank = SoundBank.Load(_soundBankPath);

        try
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

            // ReadFully keeps each mixer emitting silence when empty, so the output
            // device never starves and is always ready for the next sound.
            _musicMixer   = new MixingSampleProvider(format) { ReadFully = true };
            _ambientMixer = new MixingSampleProvider(format) { ReadFully = true };
            _effectsMixer = new MixingSampleProvider(format) { ReadFully = true };

            _musicVolume   = new VolumeSampleProvider(_musicMixer);
            _ambientVolume = new VolumeSampleProvider(_ambientMixer);
            _effectsVolume = new VolumeSampleProvider(_effectsMixer);

            var masterMixer = new MixingSampleProvider(format) { ReadFully = true };
            masterMixer.AddMixerInput(_musicVolume);
            masterMixer.AddMixerInput(_ambientVolume);
            masterMixer.AddMixerInput(_effectsVolume);
            _masterVolume = new VolumeSampleProvider(masterMixer);

            _output = new WaveOutEvent();
            _output.Init(_masterVolume);
            _output.Play();

            ApplyVolumes(master, music, effects, ambient);
            _ready = true;
        }
        catch
        {
            _ready = false;   // no device — tone bleeps still work, samples stay silent
        }
    }

    // The one entry point. Look the name up, follow any use:-alias, and do what the
    // resolved entry says. An unknown/blank entry is silent — never an error.
    public void Play(string name)
    {
        var entry = ResolveEntry(name, 0);
        if (entry is null) return;

        // Anti-spam: ignore retriggers that come too fast (e.g. a held key). Keyed by the
        // event name, but the interval comes from the resolved sound.
        if (entry.MinInterval is double mi && mi > 0)
        {
            if (_lastPlayed.TryGetValue(name, out double last) && _clock - last < mi) return;
            _lastPlayed[name] = _clock;
        }

        // A tone bleep is the PC speaker — it plays even with no audio output device.
        if (entry.Tones is { Count: > 0 } tones)
        {
            foreach (var t in tones) _speaker.Beep(t.Freq, t.Ms);
            return;
        }

        if (!_ready) return;

        float gain = ToGain(entry.Volume);

        if (entry.Playlist is { Count: > 0 })
        {
            StartPlaylist(entry, gain);
            return;
        }

        var sound = Resolve(PickFile(entry));
        if (sound is null) return;                       // no file yet, or unreadable = silence
        if (sound.WaveFormat.Channels != Channels ||
            sound.WaveFormat.SampleRate != SampleRate) return;

        var channel = ParseChannel(entry.Channel);
        if (entry.Loop && channel != AudioChannel.Effects)
        {
            StartFadedLoop(name, channel, sound, gain, entry.FadeIn ?? 1.0, entry.FadeOut ?? 1.0);
            return;
        }

        StartEffect(channel, sound, gain, entry);
    }

    public void Update(double deltaSeconds)
    {
        if (!_ready) return;
        _clock += deltaSeconds;

        lock (_mixerLock)
        {
            for (int i = _pendingRemovals.Count - 1; i >= 0; i--)
            {
                if (_clock < _pendingRemovals[i].RemoveAt) continue;
                try { _pendingRemovals[i].Mixer.RemoveMixerInput(_pendingRemovals[i].Input); } catch { }
                _pendingRemovals.RemoveAt(i);
            }

            _musicPlayer?.Update(deltaSeconds);
        }
    }

    public void Reload()
    {
        _bank = SoundBank.Load(_soundBankPath);
        lock (_mixerLock) _cache.Clear();
    }

    public void ApplyVolumes(int master, int music, int effects, int ambient)
    {
        if (_masterVolume is null) return;
        _masterVolume.Volume   = ToGain(master);
        _musicVolume!.Volume   = ToGain(music);
        _ambientVolume!.Volume = ToGain(ambient);
        _effectsVolume!.Volume = ToGain(effects);
    }

    public void Dispose()
    {
        try { _musicPlayer?.Dispose(); } catch { }
        try { _output?.Stop(); _output?.Dispose(); } catch { }
        _speaker.Dispose();
    }

    // ── resolution ─────────────────────────────────────────────────────────────

    private SoundEntry? ResolveEntry(string key, int depth)
    {
        if (depth > 8) return null;                      // guard against use:-cycles
        var entry = _bank.Get(key);
        if (entry is null) return null;
        if (!string.IsNullOrWhiteSpace(entry.Use)) return ResolveEntry(entry.Use!, depth + 1);
        return entry;
    }

    // ── playback builders ──────────────────────────────────────────────────────

    private void StartEffect(AudioChannel channel, CachedSound sound, float gain, SoundEntry entry)
    {
        long len = sound.AudioData.Length;
        long total;
        bool loopFill;

        if (entry.Duration is double d && d > 0)
        {
            total = (long)Math.Round(d * SampleRate * Channels);
            loopFill = true;                  // loop the clip to fill the requested time
        }
        else if (entry.Loops is int n && n > 0)
        {
            total = len * n;
            loopFill = true;
        }
        else
        {
            total = len;                      // plain one-shot
            loopFill = false;
        }

        var effect = new EffectSampleProvider(sound, total, loopFill,
                                              SecondsToSamples(entry.FadeIn),
                                              SecondsToSamples(entry.FadeOut));
        var voiced = new VolumeSampleProvider(effect) { Volume = gain };
        lock (_mixerLock) MixerFor(channel).AddMixerInput(voiced);
    }

    // Start a named loop. Idempotent: if that name is already playing it's left running,
    // so re-asserting an always-on bed never restarts it. Distinct names layer freely —
    // that's how the two ambient beds play at the same time.
    private void StartFadedLoop(string name, AudioChannel channel, CachedSound sound, float gain,
                                double fadeIn, double fadeOut)
    {
        lock (_mixerLock)
        {
            if (_loops.ContainsKey(name)) return;

            var voiced = new VolumeSampleProvider(new LoopingSampleProvider(sound)) { Volume = gain };
            var fade   = new FadeInOutSampleProvider(voiced, fadeIn > 0);
            if (fadeIn > 0) fade.BeginFadeIn(fadeIn * 1000.0);

            MixerFor(channel).AddMixerInput(fade);
            _loops[name] = new FadedLoop { Fade = fade, Channel = channel, FadeOutSec = fadeOut };
        }
    }

    private void StartPlaylist(SoundEntry entry, float gain)
    {
        var paths = entry.Playlist!
            .Select(p => Path.Combine(_contentAudioDir, p))
            .Where(File.Exists)
            .ToList();
        if (paths.Count == 0) return;

        lock (_mixerLock)
        {
            _musicPlayer?.Dispose();
            _musicPlayer = new MusicPlayer(_musicMixer!, SampleRate, Channels, paths,
                                           entry.Shuffle, entry.Gap ?? 3.0,
                                           entry.FadeIn ?? 1.5, entry.FadeOut ?? 1.5, gain);
            _musicPlayer.Start();
        }
    }

    // Fade a named loop out and remove it once the fade finishes. Safe to call for a name
    // that isn't playing (no-op). Used to switch context beds and to end transient loops.
    public void StopLoop(string name)
    {
        if (!_ready) return;
        lock (_mixerLock)
        {
            if (!_loops.TryGetValue(name, out var loop)) return;
            loop.Fade.BeginFadeOut(loop.FadeOutSec * 1000.0);
            // Small margin so a frame-timing wobble can't yank the loop before its fade
            // tail finishes (a removed-late loop is just harmless silence).
            _pendingRemovals.Add((MixerFor(loop.Channel), loop.Fade, _clock + loop.FadeOutSec + 0.1));
            _loops.Remove(name);
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private CachedSound? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;

        string full = Path.Combine(_contentAudioDir, relative);
        if (_cache.TryGetValue(full, out var cached)) return cached;

        CachedSound? sound = null;
        try { if (File.Exists(full)) sound = new CachedSound(full, SampleRate, Channels); }
        catch { sound = null; }
        _cache[full] = sound;             // cache misses too, so we don't retry every time
        return sound;
    }

    private string? PickFile(SoundEntry entry)
    {
        if (entry.Variations is { Count: > 0 } v) return v[_rng.Next(v.Count)];
        return entry.File;
    }

    private MixingSampleProvider MixerFor(AudioChannel channel) => channel switch
    {
        AudioChannel.Music   => _musicMixer!,
        AudioChannel.Ambient => _ambientMixer!,
        _                    => _effectsMixer!,
    };

    private static AudioChannel ParseChannel(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "music"   => AudioChannel.Music,
        "ambient" => AudioChannel.Ambient,
        _         => AudioChannel.Effects,
    };

    private static long SecondsToSamples(double? seconds) =>
        seconds is double s && s > 0 ? (long)Math.Round(s * SampleRate * Channels) : 0;

    // 0–10 → LINEAR gain (10 = unity / full level). This factor is applied three times
    // over — per-sound volume × channel slider × master — so a square (perceptual) curve
    // here compounded into roughly a 6th-power law: everything sat far too quiet with no
    // headroom near the top. Linear keeps the levels and the knobs usable.
    private static float ToGain(int level) => Math.Clamp(level, 0, 10) / 10f;
}
