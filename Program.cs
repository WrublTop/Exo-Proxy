using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Engine;
using ExoProxy.Presentation.Screens.Base;
using ExoProxy.Presentation.Screens.Boot;
using System.Threading.Channels;

// The game is designed for a fixed 120×30 canvas — fits 1080p fullscreen with
// a comfortable font size. The buffer is created at this size regardless of
// the launch window — GameLoop's mismatch screen enforces it.
const int TerminalWidth = 120;
const int TerminalHeight = 30;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.CursorVisible = false;

// Settings load before anything renders so the saved theme/brightness apply
// to the boot sequence as well.
var settings = GameSettings.Load();
ExoColors.Apply(settings.Theme, settings.Brightness);

var channel = Channel.CreateUnbounded<InputEvent>();
using var cts = new CancellationTokenSource();
var renderBuffer = new RenderBuffer(TerminalWidth, TerminalHeight);
var screenManager = new ScreenManager();
var gameLoop = new GameLoop(screenManager, renderBuffer, channel.Reader);
var inputPoller = new InputPoller(channel.Writer);

Console.Clear();

Exception? fatal = null;

try
{
    var inputTask = Task.Run(() => inputPoller.PollAsync(cts.Token));

    var gameTask = Task.Run(async () =>
    {
        try
        {
            // Session loop: boot → base → (LOGOUT → boot again | EXIT → shutdown).
            // The boot sequence doubles as the main menu, so logout simply
            // starts a fresh boot screen.
            while (true)
            {
                var bootScreen = new BootScreen();
                await screenManager.SetActiveAsync(bootScreen, cts.Token);
                await gameLoop.RunUntilAsync(() => bootScreen.IsBooted, cts.Token);

                var progress = OperatorProgress.Load(bootScreen.LoggedInAccount!.Login);
                var baseScreen = new BaseScreen(bootScreen.LoggedInAccount!, settings, progress,
                                                bootScreen.Registry, bootScreen.IsDevLogin);
                await screenManager.SetActiveAsync(baseScreen, cts.Token);
                await gameLoop.RunUntilAsync(
                    () => baseScreen.LogoutRequested || baseScreen.ExitRequested, cts.Token);

                if (baseScreen.ExitRequested) break;
            }
        }
        finally
        {
            // Always stop the input poller — otherwise Task.WhenAll below
            // would wait forever after a fault or a clean exit.
            cts.Cancel();
        }
    });

    await Task.WhenAll(inputTask, gameTask);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    fatal = ex;
}
finally
{
    // Leave the user's terminal usable no matter how we exit.
    Console.Write(ExoCodes.ShowCursor + ExoCodes.Reset);
    Console.CursorVisible = true;
    Console.Clear();
}

if (fatal is not null)
{
    string logPath = CrashLog.Write(fatal);
    Console.WriteLine($"Fatal error: {fatal.Message}");
    Console.WriteLine($"Details logged to: {logPath}");
}
else
{
    Console.WriteLine("SUIRDC TERMINAL — SESSION CLOSED.");
}
