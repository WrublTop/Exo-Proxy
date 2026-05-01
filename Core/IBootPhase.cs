namespace ExoProxy.Core;

public interface IBootPhase
{
    void Update(DateTimeOffset now, InputEvent? input);
    void Render(IRenderBuffer buffer);
    bool IsDone { get; }
}
