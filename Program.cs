using ExoProxy.Core;
using ExoProxy.Engine;
using System.Threading.Channels;

var channel = Channel.CreateUnbounded<InputEvent>();
using var cts = new CancellationTokenSource();

var renderBuffer = new RenderBuffer(220, 50);
var screenManager = new ScreenManager();
var gameLoop = new GameLoop(screenManager, renderBuffer, channel.Reader);
var inputPoller = new InputPoller(channel.Writer);

Console.CursorVisible = false;
Console.Clear();

var inputTask = Task.Run(() => inputPoller.PollAsync(cts.Token));
var gameTask = Task.Run(() => gameLoop.RunAsync(cts.Token));

await Task.WhenAll(inputTask, gameTask);