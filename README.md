# SCPSLAudioAPi

This is an API Library that does not depend on any Plugin frameworks for its function.
This API provides a implementation of an Audio player in a SCP:SL Server with options to extend

## Installation
Download the latest release from the [Releases](https://github.com/CedModV2/SCPSLAudioApi/releases/) page and place the SCPSLAudioAPi.dll file in your plugin dependencies folder

## Usage
Note: This is **NOT** an audioplayer plugin, this is a library to make creating a plugin that can play audio easier. To be able to play audio you will need to find a plugin that does that using this api, or a custom solution.

## Developer Usage
The Library mainly consists of the AudioPlayerBase class, which can be inherited in combination with overriding Methods to create custom logic while still having the heavy lifting done by the Api.

Note: In order for the Library to function properly, you **must** call `Startup::SetupDependencies` in a function that runs before any interaction with the library, Eg: OnLoad or OnRegistered of your plugin.

### Without a custom AudioPlayer class
Simply Create a Dummyplayer (Not included in the library) and Call AudioPlayerBase.Get() on the ReferenceHub of this DummyPlayer.

To play Audio, you must first, insert a Path to the audio file into the Queue, This can be done by directly modifying AudioToPlay, but it is recommend to use the Enqueue Method. \
When audio is in the queue, call the Play method and after the file has been loaded, it should start playing your audio.

### With a Custom AudioPlayer class
Simply Create a Dummyplayer (Not included in the library) and Call AudioPlayerBase.Get() on the ReferenceHub of this DummyPlayer.

(You will need to create a custom NetworkConnection for a fake player)

```csharp
public class FakeConnection : NetworkConnectionToClient
{
    public FakeConnection(int connectionId) : base(connectionId, false, 0f)
    {
            
    }

    public override string address
    {
        get
        {
            return "localhost";
        }
    }

    public override void Send(ArraySegment<byte> segment, int channelId = 0)
    {
    }
    public override void Disconnect()
    {
    }
}
```

then, you can spawn a fake player like this

```csharp
var newPlayer = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab);
var fakeConnection = new FakeConnection(id);
var hubPlayer = newPlayer.GetComponent<ReferenceHub>();
NetworkServer.AddPlayerForConnection(fakeConnection, newPlayer);
```
Then you can use the player like you would with any, eg spawn it, teleport it

Custom AudioPlayer classes work the same way as a normal AudioPlayerBase, the developer can change whatever they feel like in the methods for playing and broadcasting.
Refer to the source of AudioPlayerBase to see what each method does.

You can get your AudioPlayer class in multiple ways
You can try to find it in AudioPlayerBase.AudioPlayers, but it is recommended to create a helper Method.

```csharp
public static CustomAudioPlayer Get(ReferenceHub hub)
{
    if (AudioPlayers.TryGetValue(hub, out AudioPlayerBase player))
    {
        if (player is CustomAudioPlayer cplayer1)
            return cplayer1;
    }

    var cplayer = hub.gameObject.AddComponent<CustomAudioPlayer>();
    cplayer.Owner = hub;

    AudioPlayers.Add(hub, cplayer);
    return cplayer;
}
```

To start playing audio there are 2 options you can use, using the Queue system, or directly playing.
to directly play audio, set CurrentPlay to the path or url that you want to play, and call Play(-1).
Additionally if you want to loop the audio, set Loop to true.

If you wish to play using the Queue system.
Call Enqueue with the file or url you wish to play.
then call Play(0); to start playing the first file or url in queue.
Additionally, setting loop to true will cause the AudioPlayer to add audio to the end of the queue when it starts to play it.

**Note:** For Urls to work you must set AllowUrl to true on the AudioPlayerBase instance.

### Features:
Volume control - `AudioPlayerBase::Volume` \
Queue Looping - `AudioPlayerBase::Loop` \
Queue Shuffle - `AudioPlayerBase::Shuffle` \
Autoplay - `AudioPlayerBase::Continue` \
Pausing - `AudioPlayerBase::ShouldPlay` \
Play From URL - `AudioPlayerBase::AllowUrl` \
Play to specific players only (Will play to everyone if the list is Empty) - `AudioPlayerBase::BroadcastTo` \
Debug logs (can cause spam) - `AudioPlayerBase::LogDebug` \
AudioChannel - `AudioPlayerBase::BroadcastChannel`
