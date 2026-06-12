namespace ExoProxy.Core;

public interface IBaseSection
{
    string SectionId { get; }
    void Update(GameTime time, InputEvent? input);
    void Render(IRenderBuffer buffer);
    BaseSectionResponse Response { get; }
}
