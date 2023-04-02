using System.Collections.Generic;
using System.IO;
using MEC;
using SCPSLAudioApi.AudioCore;
using VoiceChat;

namespace SCPSLAudioApi.Api
{
    public class AudioPlayerBase
    {
        public static Dictionary<ReferenceHub, AudioPlayerBase> AudioPlayers;

        private readonly AudioPlayerComponent _component;

        /// <summary>
        ///     If URLs are allowed to be played
        /// </summary>
        public bool AllowUrl;

        /// <summary>
        ///     List of Paths/Urls that the player will play from (Urls only work if <see cref="AllowUrl" /> is true)
        /// </summary>
        public List<string> AudioToPlay = new List<string>();

        /// <summary>
        ///     If not empty, the audio will only be sent to players with the PlayerIds in this list
        /// </summary>
        public List<int> BroadcastTo = new List<int>();

        /// <summary>
        ///     Whether the Player should continue playing by itself after the current Track ends.
        /// </summary>
        public bool Continue = true;

        /// <summary>
        ///     Path/Url of the currently playing audio file.
        /// </summary>
        public string CurrentPlay;

        /// <summary>
        ///     Stream containing the Audio data
        /// </summary>
        public MemoryStream CurrentPlayStream;

        /// <summary>
        ///     Boolean indicating whether or not the Queue will loop (Audio will be added to the end of the queue after it gets
        ///     removed on play)
        /// </summary>
        public bool Loop;

        /// <summary>
        ///     Whether the Player should be sending audio to the broadcaster.
        /// </summary>
        public bool ShouldPlay = true;

        /// <summary>
        ///     If the playlist should be shuffled when an audio track start.
        /// </summary>
        public bool Shuffle;

        /// <summary>
        ///     If Vebose logs shouldbe shown (Note: can be very spammy)
        /// </summary>
        public bool VerboseLogs;

        static AudioPlayerBase()
        {
            AudioPlayers = new Dictionary<ReferenceHub, AudioPlayerBase>();
        }

        public AudioPlayerBase(ReferenceHub referenceHub, AudioPlayerComponent audioPlayerComponent)
        {
            ReferenceHub = referenceHub;
            _component = audioPlayerComponent;
        }

        /// <summary>
        ///     The ReferenceHub instance that this player sends as.
        /// </summary>
        public ReferenceHub ReferenceHub { get; }

        /// <summary>
        ///     Volume that the player will play at.
        /// </summary>
        public float Volume { get; set; } = 100f;

        /// <summary>
        ///     Gets or Sets the Channel where audio will be played in
        /// </summary>
        public VoiceChatChannel BroadcastChannel { get; set; } = VoiceChatChannel.Proximity;

        /// <summary>
        ///     Add or retrieve the AudioPlayerBase instance based on a ReferenceHub instance.
        /// </summary>
        /// <param name="hub">The ReferenceHub instance that this AudioPlayer belongs to</param>
        /// <returns>
        ///     <see cref="AudioPlayerBase" />
        /// </returns>
        public static AudioPlayerBase Get(ReferenceHub hub)
        {
            if (AudioPlayers.TryGetValue(hub, out var player)) return player;

            var audioPlayerComponent = hub.gameObject.AddComponent<AudioPlayerComponent>();
            player = new AudioPlayerBase(hub, audioPlayerComponent);

            AudioPlayers.Add(hub, player);
            return player;
        }

        /// <summary>
        ///     Start playing audio, if called while audio is already playing the player will skip to the next file.
        /// </summary>
        /// <param name="queuePos">The position in the queue of the audio that should be played.</param>
        public virtual void Play(int queuePos)
        {
            if (_component.PlaybackCoroutine.IsRunning)
                Timing.KillCoroutines(_component.PlaybackCoroutine);

            _component.PlaybackCoroutine = Timing.RunCoroutine(_component.Playback(queuePos), Segment.FixedUpdate);
        }

        /// <summary>
        ///     Stops playing the current Track, or stops the player entirely if Clear is true.
        /// </summary>
        /// <param name="clear">If true the player will stop and the queue will be cleared.</param>
        public virtual void Stoptrack(bool clear)
        {
            if (clear) AudioToPlay.Clear();

            _component.stopTrack = true;
        }

        /// <summary>
        ///     Add an audio file to the queue
        /// </summary>
        /// <param name="audio">Path/Url to an audio file (Url only works if <see cref="AllowUrl" /> is true)</param>
        /// <param name="pos">Position that the audio file should be inserted at, use -1 to insert at the end of the queue.</param>
        public virtual void Enqueue(string audio, int pos)
        {
            if (pos == -1)
                AudioToPlay.Add(audio);
            else
                AudioToPlay.Insert(pos, audio);
        }
    }
}