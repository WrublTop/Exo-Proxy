using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Engine;
using ExoProxy.Presentation.Screens.Base;
using ExoProxy.Presentation.Screens.Boot;
using System.Threading.Channels;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.CursorVisible = false;

int width = Console.WindowWidth;
int height = Console.WindowHeight;

var channel = Channel.CreateUnbounded<InputEvent>();
using var cts = new CancellationTokenSource();
var renderBuffer = new RenderBuffer(width, height);
var screenManager = new ScreenManager();
var gameLoop = new GameLoop(screenManager, renderBuffer, channel.Reader);
var inputPoller = new InputPoller(channel.Writer);

Console.Clear();

var bootScreen = new BootScreen();
await screenManager.SetActiveAsync(bootScreen, cts.Token);

var inputTask = Task.Run(() => inputPoller.PollAsync(cts.Token));
var gameTask = Task.Run(async () =>
{
    await gameLoop.RunUntilAsync(() => bootScreen.IsBooted, cts.Token);
    var settings = GameSettings.Load();
    var baseScreen = new BaseScreen(bootScreen.LoggedInAccount!, settings, bootScreen.Registry);
    await screenManager.SetActiveAsync(baseScreen, cts.Token);
    await gameLoop.RunAsync(cts.Token);
});

try
{
    await Task.WhenAll(inputTask, gameTask);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    Console.Clear();
    Console.WriteLine($"Fatal error: {ex.Message}");
}
