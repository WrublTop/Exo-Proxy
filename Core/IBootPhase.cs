namespace ExoProxy.Core;

public interface IBootPhase
{
    void Update(GameTime time, InputEvent? input);
    void Render(IRenderBuffer buffer);
    bool IsDone { get; }
}
