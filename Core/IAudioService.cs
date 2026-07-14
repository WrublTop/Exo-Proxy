namespace ExoProxy.Core;

// The whole game talks to audio through this one façade. Screens depend on the
// abstraction, never on NAudio directly — so a machine with no sound device can
// fall back to NullAudioService and the game keeps running, just in silence.
//
// One verb makes sound: Play(name). The soundbank entry behind that name decides
// everything — bleep, sample, ambient loop, or music playlist. Callers never deal
// with channels or file formats; they just name what happened.
public interface IAudioService : IDisposable
{
    // Play the sound mapped to this name in soundbank.yaml. An unknown or blank entry
    // is SILENT, never an error — which is what lets us sprinkle named hooks everywhere
    // and let the sound designer light up only the ones they want.
    void Play(string name);

    // Stop a named looping sound (one started from a loop:true entry), fading it out.
    // Safe to call when it isn't playing. Used to switch context beds and to end a
    // transient loop such as the "computer is busy" hum.
    void StopLoop(string name);

    // Advance time-based audio: music playlist progression and fade-out cleanup.
    // Call once per frame from the game loop with the frame delta in seconds.
    void Update(double deltaSeconds);

    // Re-read soundbank.yaml from disk and drop the sample cache, so freshly assigned
    // sounds take effect without restarting (used by the DEV RELOAD command).
    void Reload();

    // Push the 0–10 volume sliders into the live mix. Master scales the other three.
    void ApplyVolumes(int master, int music, int effects, int ambient);
}
