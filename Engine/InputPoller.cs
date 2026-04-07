using ExoProxy.Core;
using System.Threading.Channels;

namespace ExoProxy.Engine;

public sealed class InputPoller
{
    private readonly ChannelWriter<InputEvent> _inputWriter;

    public InputPoller(ChannelWriter<InputEvent> inputWriter)
    {
        _inputWriter = inputWriter;
    }

    public async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(true);
                var inputEvent = new InputEvent(keyInfo, DateTimeOffset.UtcNow);
                _inputWriter.TryWrite(inputEvent);
            }
            else
            {
                await Task.Delay(10, ct);
            }
        }
    }
}