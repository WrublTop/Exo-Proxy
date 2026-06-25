using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Engine;
using ExoProxy.Presentation.Screens.Base;
using ExoProxy.Presentation.Screens.Boot;
using System.Threading.Channels;

// Fixed canvas, centred in the terminal. Bump these to fill more of a 1080p
// fullscreen — less black border, a bigger map slice. Layouts are relative, so
// they adapt. GameLoop shows a friendly "resize" screen when the window is
// smaller than this, so a too-big value never garbles — it just asks for a
// larger terminal (or a smaller font). Tune freely.
const int TerminalWidth = 180;
const int TerminalHeight = 40;

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

                var account  = bootScreen.LoggedInAccount!;
                var progress = OperatorProgress.Load(account.Login);
                var baseScreen = new BaseScreen(account, settings, progress,
                                                bootScreen.Registry, bootScreen.IsDevLogin);
                await screenManager.SetActiveAsync(baseScreen, cts.Token);
                await gameLoop.RunUntilAsync(
                    () => baseScreen.LogoutRequested || baseScreen.ExitRequested
                          || baseScreen.PerishRequested, cts.Token);

                if (baseScreen.PerishRequested)
                {
                    // Permadeath. The run's saves are wiped either way. A real
                    // operator is also marked terminated so the login can never be
                    // re-entered; DEV isn't in the registry, so its wipe is just a
                    // reset and it can log straight back in fresh.
                    OperatorSaves.Wipe(account.Login);
                    var registered = bootScreen.Registry.Accounts
                        .FirstOrDefault(a => a.Login == account.Login);
                    if (registered is not null)
                    {
                        registered.Status = OperatorStatus.Terminated;
                        bootScreen.Registry.Save();
                    }
                }

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
