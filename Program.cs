using ExoProxy.Core;
using ExoProxy.Engine;
using ExoProxy.Presentation.Screens;
using System.Threading.Channels;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var channel = Channel.CreateUnbounded<InputEvent>();
using var cts = new CancellationTokenSource();

var renderBuffer = new RenderBuffer(120, 30);
var screenManager = new ScreenManager();
var gameLoop = new GameLoop(screenManager, renderBuffer, channel.Reader);
var inputPoller = new InputPoller(channel.Writer);

Console.SetBufferSize(120, 30);
Console.SetWindowSize(120, 30);

Console.CursorVisible = false;
Console.Clear();

var bootScreen = new BootScreen();
screenManager.AddScreen(bootScreen);
screenManager.SetActive(bootScreen);

var inputTask = Task.Run(() => inputPoller.PollAsync(cts.Token));
var gameTask = Task.Run(() => gameLoop.RunAsync(cts.Token));

await Task.WhenAll(inputTask, gameTask);


