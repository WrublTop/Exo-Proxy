using ExoProxy.Core;
using System.Threading.Channels;

namespace ExoProxy.Engine;

public sealed class GameLoop
{
    private readonly ScreenManager _screenManager;
    private readonly IRenderBuffer _buffer;
    private readonly ChannelReader<InputEvent> _input;
    private readonly int _targetFps;

    public GameLoop(ScreenManager screenManager, IRenderBuffer buffer, ChannelReader<InputEvent> input, int targetFps = 30)
    {
        _screenManager = screenManager;
        _buffer         = buffer;
        _input          = input;
        _targetFps      = targetFps;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var frameTime = TimeSpan.FromSeconds(1.0 / _targetFps);
        var totalTime = TimeSpan.Zero;
        var previous = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var delta = now - previous;
            previous  = now;
            totalTime += delta;
            var gt = new GameTime(totalTime, delta);

            InputEvent? inputEvent = null;
            while (_input.TryRead(out var ie))
                inputEvent = ie;

            _screenManager.Update(gt, inputEvent);

            _buffer.Clear();
            _screenManager.Render(_buffer);
            Console.Write(_buffer.Flush());
            var elapsed = DateTimeOffset.UtcNow - now;
            if (elapsed < frameTime)
                await Task.Delay(frameTime - elapsed, ct);
        }

    }
}