namespace ExoProxy.Core;

public interface IBaseSection
{
    string SectionId { get; }
    void Update(DateTimeOffset now, InputEvent? input);
    void Render(IRenderBuffer buffer);
    BaseSectionResponse Response { get; }
}
