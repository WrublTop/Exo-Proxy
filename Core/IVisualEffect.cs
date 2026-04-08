namespace ExoProxy.Core;
public interface IVisualEffect
{
    void Update(GameTime gt);
    bool IsDone { get; }
}
