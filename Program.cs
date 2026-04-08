using ExoProxy.Core;
using ExoProxy.Engine;
using ExoProxy.Presentation.Screens;
using System.Threading.Channels;

Console.OutputEncoding = System.Text.Encoding.UTF8;

int width = Console.WindowWidth;
int height = Console.WindowHeight;

var channel = Channel.CreateUnbounded<InputEvent>();
using var cts = new CancellationTokenSource();

var renderBuffer = new RenderBuffer(width, height);
var screenManager = new ScreenManager();
var gameLoop = new GameLoop(screenManager, renderBuffer, channel.Reader);
var inputPoller = new InputPoller(channel.Writer);

Console.CursorVisible = false;
Console.Clear();

var bootScreen = new BootScreen();
screenManager.AddScreen(bootScreen);
screenManager.SetActive(bootScreen);

var inputTask = Task.Run(() => inputPoller.PollAsync(cts.Token));
var gameTask = Task.Run(() => gameLoop.RunAsync(cts.Token));

await Task.WhenAll(inputTask, gameTask);
