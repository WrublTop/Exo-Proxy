namespace ExoProxy.Core;

public interface IScreen
{
    string ScreenId { get; }
    Task OnEnterAsync(CancellationToken ct);
    Task OnExitAsync(CancellationToken ct);
    void Update(GameTime time, InputEvent? input);
    void Render(IRenderBuffer buffer);
}