using ExoProxy.Core;
using System.Threading.Channels;

namespace ExoProxy.Engine;

public sealed class GameLoop
{
    private readonly ScreenManager _screenManager;
    private readonly IRenderBuffer _buffer;
    private readonly ChannelReader<InputEvent> _input;
    private readonly IAudioService _audio;
    private readonly int _targetFps;

    private int  _lastW       = -1;
    private int  _lastH       = -1;
    private bool _wasTooSmall = false;
    private int  _lastTermW   = -1;
    private int  _lastTermH   = -1;

    public GameLoop(ScreenManager screenManager, IRenderBuffer buffer, ChannelReader<InputEvent> input,
                    IAudioService audio, int targetFps = 30)
    {
        _screenManager = screenManager;
        _buffer        = buffer;
        _input         = input;
        _audio         = audio;
        _targetFps     = targetFps;
    }

    public async Task RunUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var frameTime = TimeSpan.FromSeconds(1.0 / _targetFps);
        var totalTime = TimeSpan.Zero;
        var previous  = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested && !condition())
        {
            var now   = DateTimeOffset.UtcNow;
            var delta = now - previous;
            previous  = now;

            // Terminal too small → freeze the game: no update, no time advance,
            // input discarded. Resumes exactly where it was once the window fits.
            if (Console.WindowWidth < _buffer.Width || Console.WindowHeight < _buffer.Height)
            {
                while (_input.TryRead(out _)) { }
                RenderFrame();
                var idle = DateTimeOffset.UtcNow - now;
                if (idle < frameTime) await Task.Delay(frameTime - idle, ct);
                continue;
            }

            totalTime += delta;
            var gt = new GameTime(totalTime, delta);

            var events = new List<InputEvent>();
            while (_input.TryRead(out var ie))
                events.Add(ie);

            if (events.Count == 0)
                _screenManager.Update(gt, null);
            else
                foreach (var ev in events)
                {
                    // Every physical keypress clicks — anywhere in the game, boot included.
                    // (Throttled by ui.click's min_interval so a held key can't machine-gun.)
                    _audio.Play("key.press");
                    _screenManager.Update(gt, ev);
                }

            RenderFrame();

            var elapsed = DateTimeOffset.UtcNow - now;
            if (elapsed < frameTime)
                await Task.Delay(frameTime - elapsed, ct);
        }
    }

    private void RenderFrame()
    {
        int cw = Console.WindowWidth;
        int ch = Console.WindowHeight;

        if (cw < _buffer.Width || ch < _buffer.Height)
        {
            if (cw != _lastW || ch != _lastH)
            {
                RenderWrongSize(cw, ch, _buffer.Width, _buffer.Height);
                _lastW = cw;
                _lastH = ch;
            }
            _wasTooSmall = true;
            return;
        }

        bool needsClear = _wasTooSmall || cw != _lastTermW || ch != _lastTermH;
        _wasTooSmall = false;
        _lastTermW   = cw;
        _lastTermH   = ch;
        _lastW       = -1;
        _lastH       = -1;

        _buffer.Clear();
        _screenManager.Render(_buffer);
        string frame = _buffer.Flush();
        Console.Write(needsClear ? "\x1b[2J" + frame : frame);
    }

    private static void RenderWrongSize(int w, int h, int reqW, int reqH)
    {
        const string Reset  = "\x1b[0m";
        const int    BoxW   = 44;
        const int    Inner  = BoxW - 2;   // 42
        const int    BoxH   = 10;

        string Col(string ansi, string text) => ansi + text + Reset;
        string At(int x, int y) => $"\x1b[{y + 1};{x + 1}H";

        string Centered(string s)
        {
            int pad = (Inner - s.Length) / 2;
            return s.PadLeft(pad + s.Length).PadRight(Inner);
        }

        string SizeRow(string label, int dw, int dh, bool wOk, bool hOk)
        {
            // layout: 2 indent + label(10) + w(4) + "  ×  "(5) + h(4) + trailing(17) = 42
            string lbl = ("  " + label).PadRight(12);
            return
                Col(ExoColors.ProksBorder, "│") +
                Col(ExoColors.ProksPale,   lbl) +
                Col(wOk ? ExoColors.PhosphorText : ExoColors.FaultText, dw.ToString().PadLeft(4)) +
                Col(ExoColors.ProksPale,   "  ×  ") +
                Col(hOk ? ExoColors.PhosphorText : ExoColors.FaultText, dh.ToString().PadLeft(4)) +
                Col(ExoColors.ProksPale,   new string(' ', Inner - 25)) +
                Col(ExoColors.ProksBorder, "│");
        }

        int bx = Math.Max(0, (w - BoxW) / 2);
        int by = Math.Max(0, (h - BoxH) / 2);

        var sb = new System.Text.StringBuilder();
        sb.Append("\x1b[2J\x1b[H");

        sb.Append(At(bx, by));     sb.Append(Col(ExoColors.ProksBorder, "┌" + new string('─', Inner) + "┐"));
        sb.Append(At(bx, by + 1)); sb.Append(Col(ExoColors.ProksBorder, "│") + Col(ExoColors.FaultText,  Centered("TERMINAL SIZE MISMATCH")) + Col(ExoColors.ProksBorder, "│"));
        sb.Append(At(bx, by + 2)); sb.Append(Col(ExoColors.ProksBorder, "├" + new string('─', Inner) + "┤"));
        sb.Append(At(bx, by + 3)); sb.Append(Col(ExoColors.ProksBorder, "│" + new string(' ', Inner) + "│"));
        sb.Append(At(bx, by + 4)); sb.Append(SizeRow("Required", reqW, reqH, true,       true));
        sb.Append(At(bx, by + 5)); sb.Append(SizeRow("Current",  w,    h,    w == reqW,  h == reqH));
        sb.Append(At(bx, by + 6)); sb.Append(Col(ExoColors.ProksBorder, "│" + new string(' ', Inner) + "│"));
        sb.Append(At(bx, by + 7)); sb.Append(Col(ExoColors.ProksBorder, "├" + new string('─', Inner) + "┤"));
        sb.Append(At(bx, by + 8)); sb.Append(Col(ExoColors.ProksBorder, "│") + Col(ExoColors.ProksText,  ("  Adjust terminal size to continue.").PadRight(Inner)) + Col(ExoColors.ProksBorder, "│"));
        sb.Append(At(bx, by + 9)); sb.Append(Col(ExoColors.ProksBorder, "└" + new string('─', Inner) + "┘"));

        Console.Write(sb);
    }
}
