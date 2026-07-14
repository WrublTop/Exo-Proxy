namespace ExoProxy.Core;

// No-op audio. Installed when the real engine can't open an output device
// (remote desktop, headless CI, no sound card). Keeps the game fully playable
// in silence instead of crashing on startup.
public sealed class NullAudioService : IAudioService
{
    public void Play(string name) { }
    public void StopLoop(string name) { }
    public void Update(double deltaSeconds) { }
    public void Reload() { }
    public void ApplyVolumes(int master, int music, int effects, int ambient) { }
    public void Dispose() { }
}
