namespace ExoProxy.Core;

// The three mixing buses. Effects = overlapping one-shots; Music and Ambient each
// hold a single looping track at a time. All sit under one master volume.
public enum AudioChannel { Effects, Music, Ambient }
