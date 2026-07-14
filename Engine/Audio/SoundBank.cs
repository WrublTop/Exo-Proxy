using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Engine.Audio;

// One PC-speaker tone: a frequency for a duration. freq 0 = a silent rest.
public sealed class BeepTone
{
    public int Freq { get; set; }
    public int Ms { get; set; }
}

// One entry in soundbank.yaml. An entry is exactly ONE kind of sound, decided by which
// field it carries: tones (beep) / file or variations (sample) / playlist (music) /
// use (alias to another entry). Radek edits these.
public sealed class SoundEntry
{
    // Point at another entry and behave exactly like it — the shared "action". Lets many
    // events use one sound (every UI click → use: ui.click). Change it in one place.
    public string? Use { get; set; }

    // PC-speaker bleep: a sequence of tones. Works even with no sound device, and ignores
    // the volume sliders. This is how boot bleeps and retro UI feedback are defined.
    public List<BeepTone>? Tones { get; set; }

    public string? File { get; set; }              // path relative to Content/Audio/
    public string Channel { get; set; } = "effects"; // effects | music | ambient
    public int Volume { get; set; } = 10;          // per-sound trim, 0–10
    public bool Loop { get; set; }                 // single seamless loop (ambient bed)
    public List<string>? Variations { get; set; }  // if set, a random one is picked each play

    // One-shot length control (effects). Set ONE of these:
    public double? Duration { get; set; }          // play for exactly N seconds (loops the
                                                   //   clip to fill, cuts sample-accurate)
    public int? Loops { get; set; }                // play the clip exactly N whole times

    // Optional fades (seconds). fade_out also smooths a duration cut so it doesn't click.
    public double? FadeIn { get; set; }
    public double? FadeOut { get; set; }

    // Don't retrigger this event more often than every N seconds (anti-spam, e.g. keys).
    public double? MinInterval { get; set; }

    // Music playlist (channel: music). When set, the engine shuffles/sequences these
    // tracks, streaming them from disk, with a gap of silence between them.
    public List<string>? Playlist { get; set; }
    public bool Shuffle { get; set; }
    public double? Gap { get; set; }               // seconds of silence between tracks
}

// The event → sound map, loaded from soundbank.yaml. A missing file or malformed
// bank degrades to silence (empty map) — it never throws.
public sealed class SoundBank
{
    private Dictionary<string, SoundEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static SoundBank Load(string path)
    {
        var bank = new SoundBank();
        try
        {
            if (File.Exists(path))
            {
                var yaml = File.ReadAllText(path);
                var parsed = _deserializer.Deserialize<Dictionary<string, SoundEntry>>(yaml);
                if (parsed is not null)
                    bank._entries = new Dictionary<string, SoundEntry>(parsed, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Malformed bank → stay empty (silent) rather than crash the game.
        }
        return bank;
    }

    // Null = no such event, or the event is intentionally left blank → caller plays silence.
    public SoundEntry? Get(string key) =>
        _entries.TryGetValue(key, out var entry) ? entry : null;
}
