using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MEC;
using NVorbis;
using PluginAPI.Core;
using UnityEngine;
using UnityEngine.Networking;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Networking;
using Random = UnityEngine.Random;

namespace SCPSLAudioApi.AudioCore
{
    public class AudioPlayerBase : MonoBehaviour
    {
        public static Dictionary<ReferenceHub, AudioPlayerBase> AudioPlayers = new Dictionary<ReferenceHub, AudioPlayerBase>();

        #region Internal
        
        public const int HeadSamples = 1920;
        public OpusEncoder Encoder { get; } = new OpusEncoder(VoiceChat.Codec.Enums.OpusApplicationType.Voip);
        public PlaybackBuffer PlaybackBuffer { get; } = new PlaybackBuffer();
        public byte[] EncodedBuffer { get; } = new byte[512];
        public bool stopTrack = false;
        public bool ready = false;
        public CoroutineHandle PlaybackCoroutine;

        public float allowedSamples;
        public int samplesPerSecond;
        public Queue<float> StreamBuffer { get; } = new Queue<float>();
        public VorbisReader VorbisReader { get; set; }
        public float[] SendBuffer { get; set; }
        public float[] ReadBuffer { get; set; }
        
        #endregion
        
        #region AudioPlayer Settings
        
        /// <summary>
        /// The ReferenceHub instance that this player sends as.
        /// </summary>
        public ReferenceHub Owner { get; set; }
        
        /// <summary>
        /// Volume that the player will play at.
        /// </summary>
        public float Volume { get; set; } = 100f;
        
        /// <summary>
        /// List of Paths/Urls that the player will play from (Urls only work if <see cref="AllowUrl"/> is true)
        /// </summary>
        public List<string> AudioToPlay = new List<string>();
        
        /// <summary>
        /// Path/Url of the currently playing audio file.
        /// </summary>
        public string CurrentPlay;
        
        /// <summary>
        /// Stream containing the Audio data
        /// </summary>
        public MemoryStream CurrentPlayStream;
        
        /// <summary>
        /// Boolean indicating whether or not the Queue will loop (Audio will be added to the end of the queue after it gets removed on play)
        /// </summary>
        public bool Loop = false;
        
        /// <summary>
        /// If the playlist should be shuffled when an audio track start.
        /// </summary>
        public bool Shuffle = false;
        
        /// <summary>
        /// Whether the Player should continue playing by itself after the current Track ends.
        /// </summary>
        public bool Continue = true;
        
        /// <summary>
        /// Whether the Player should be sending audio to the broadcaster.
        /// </summary>
        public bool ShouldPlay = true;
        
        /// <summary>
        /// If URLs are allowed to be played
        /// </summary>
        public bool AllowUrl = false;
        
        /// <summary>
        /// If Debug logs shouldbe shown (Note: can be very spammy)
        /// </summary>
        public bool LogDebug = false;

        /// <summary>
        /// If not empty, the audio will only be sent to players with the PlayerIds in this list
        /// </summary>
        public List<int> BroadcastTo = new List<int>();

        /// <summary>
        /// Gets or Sets the Channel where audio will be played in
        /// </summary>
        public VoiceChatChannel BroadcastChannel { get; set; } = VoiceChatChannel.Proximity;

        #endregion

        #region Events
        
        /// <summary>
        /// Fired when a track is getting selected.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that is going to be selected</param>
        public delegate void TrackSelecting(AudioPlayerBase playerBase, bool directPlay, ref int queuePos);
        public static event TrackSelecting OnTrackSelecting;
        
        /// <summary>
        /// Fired when a track has been selected
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that will start</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackSelected(AudioPlayerBase playerBase, bool directPlay, int queuePos, ref string track);
        public static event TrackSelected OnTrackSelected;
        
        
        /// <summary>
        /// Fired when a track is loaded and will begin playing.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue that will play</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackLoaded(AudioPlayerBase playerBase, bool directPlay, int queuePos, string track);
        public static event TrackLoaded OnTrackLoaded;
        
        /// <summary>
        /// Fired when a track finishes.
        /// </summary>
        /// <param name="playerBase">The AudioPlayer instance that this event fired for</param>
        /// <param name="track">The track the AudioPlayer was playing</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="nextQueuePos">Position in the Queue that will play next, can be set to a different value</param>
        public delegate void TrackFinished(AudioPlayerBase playerBase, string track, bool directPlay, ref int nextQueuePos);
        public static event TrackFinished OnFinishedTrack;

        #endregion

        /// <summary>
        /// Add or retrieve the AudioPlayerBase instance based on a ReferenceHub instance.
        /// </summary>
        /// <param name="hub">The ReferenceHub instance that this AudioPlayer belongs to</param>
        /// <returns><see cref="AudioPlayerBase"/></returns>
        public static AudioPlayerBase Get(ReferenceHub hub)
        {
            if (AudioPlayers.TryGetValue(hub, out AudioPlayerBase player))
            {
                return player;
            }

            player = hub.gameObject.AddComponent<AudioPlayerBase>();
            player.Owner = hub;

            AudioPlayers.Add(hub, player);
            return player;
        }

        /// <summary>
        /// Start playing audio, if called while audio is already playing the player will skip to the next file.
        /// </summary>
        /// <param name="queuePos">The position in the queue of the audio that should be played.</param>
        public virtual void Play(int queuePos)
        {
            if (PlaybackCoroutine.IsRunning)
                Timing.KillCoroutines(PlaybackCoroutine);
            PlaybackCoroutine = Timing.RunCoroutine(Playback(queuePos), Segment.FixedUpdate);
        }
        
        /// <summary>
        /// Stops playing the current Track, or stops the player entirely if Clear is true.
        /// </summary>
        /// <param name="Clear">If true the player will stop and the queue will be cleared.</param>
        public virtual void Stoptrack(bool Clear)
        {
            if (Clear)
                AudioToPlay.Clear();
            stopTrack = true;
        }
        
        /// <summary>
        /// Add an audio file to the queue
        /// </summary>
        /// <param name="audio">Path/Url to an audio file (Url only works if <see cref="AllowUrl"/> is true)</param>
        /// <param name="pos">Position that the audio file should be inserted at, use -1 to insert at the end of the queue.</param>
        public virtual void Enqueue(string audio, int pos)
        {
            if (pos == -1)
                AudioToPlay.Add(audio);
            else
                AudioToPlay.Insert(pos, audio);
        }

        public virtual void OnDestroy()
        {
            if (PlaybackCoroutine.IsValid)
                Timing.KillCoroutines(PlaybackCoroutine);
            AudioPlayers.Remove(Owner);
        }

        public virtual IEnumerator<float> Playback(int position)
        {
            stopTrack = false;
            int index = position;
            OnTrackSelecting?.Invoke(this, index == -1, ref index);
            if (index != -1)
            {
                if (Shuffle)
                    AudioToPlay = AudioToPlay.OrderBy(i => Random.value).ToList();
                CurrentPlay = AudioToPlay[index];
                AudioToPlay.RemoveAt(index);
                if (Loop)
                {
                    AudioToPlay.Add(CurrentPlay);
                }
            }
            OnTrackSelected?.Invoke(this, index == -1, index, ref CurrentPlay);
            Log.Info($"Loading Audio");
            if (AllowUrl && Uri.TryCreate(CurrentPlay, UriKind.Absolute, out Uri result))
            {
                UnityWebRequest www = new UnityWebRequest(CurrentPlay, "GET");
                DownloadHandlerBuffer dH = new DownloadHandlerBuffer();
                www.downloadHandler = dH;
            
                yield return Timing.WaitUntilDone(www.SendWebRequest());

                if (www.responseCode != 200)
                {
                    Log.Error($"Failed to retrieve audio {www.responseCode} {www.downloadHandler.text}");
                    if (Continue && AudioToPlay.Count >= 1)
                    {
                        yield return Timing.WaitForSeconds(1);
                        if (AudioToPlay.Count >= 1)
                            Timing.RunCoroutine(Playback(0));
                    }
                    yield break;
                }

                CurrentPlayStream = new MemoryStream(www.downloadHandler.data);
            }
            else
            {
                if (File.Exists(CurrentPlay))
                {
                    if (!CurrentPlay.EndsWith(".ogg"))
                    {
                        Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must be ogg files");
                        yield return Timing.WaitForSeconds(1);
                        if (AudioToPlay.Count >= 1)
                            Timing.RunCoroutine(Playback(0));
                        yield break;
                    }
                    CurrentPlayStream = new MemoryStream(File.ReadAllBytes(CurrentPlay));
                }
                else
                {
                    Log.Error($"Audio file {CurrentPlay} does not exist. skipping.");
                    yield return Timing.WaitForSeconds(1);
                    if (AudioToPlay.Count >= 1)
                        Timing.RunCoroutine(Playback(0));
                    yield break;
                }
            }
            
            CurrentPlayStream.Seek(0, SeekOrigin.Begin);
            
            VorbisReader = new NVorbis.VorbisReader(CurrentPlayStream);

            if (VorbisReader.Channels >= 2)
            {
                Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must be mono.");
                yield return Timing.WaitForSeconds(1);
                if (AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));
                VorbisReader.Dispose();
                CurrentPlayStream.Dispose();
                yield break;
            }
            
            if (VorbisReader.SampleRate != 48000)
            {
                Log.Error($"Audio file {CurrentPlay} is not valid. Audio files must have a SamepleRate of 48000");
                yield return Timing.WaitForSeconds(1);
                if (AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));
                VorbisReader.Dispose();
                CurrentPlayStream.Dispose();
                yield break;
            }
            OnTrackLoaded?.Invoke(this, index == -1, index, CurrentPlay);
            Log.Info($"Playing {CurrentPlay} with samplerate of {VorbisReader.SampleRate}");
            samplesPerSecond = VoiceChatSettings.SampleRate * VoiceChatSettings.Channels;
            //_samplesPerSecond = VorbisReader.Channels * VorbisReader.SampleRate / 5;
            SendBuffer = new float[samplesPerSecond / 5 + HeadSamples];
            ReadBuffer = new float[samplesPerSecond / 5 + HeadSamples];
            int cnt;
            while ((cnt = VorbisReader.ReadSamples(ReadBuffer, 0, ReadBuffer.Length)) > 0)
            {
                if (stopTrack)
                {
                    VorbisReader.SeekTo(VorbisReader.TotalSamples - 1);
                    stopTrack = false;
                }
                while (!ShouldPlay)
                {
                    yield return Timing.WaitForOneFrame;
                }
                while (StreamBuffer.Count >= ReadBuffer.Length)
                {
                    ready = true;
                    yield return Timing.WaitForOneFrame;
                }
                for (int i = 0; i < ReadBuffer.Length; i++)
                {
                    StreamBuffer.Enqueue(ReadBuffer[i]);
                }
            }
            Log.Info($"Track Complete.");

            int nextQueuepos = 0;
            if (Continue && Loop && index == -1)
            {
                nextQueuepos = -1;
                Timing.RunCoroutine(Playback(nextQueuepos));
                OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);
                yield break;
            }

            if (Continue && AudioToPlay.Count >= 1)
            {
                Timing.RunCoroutine(Playback(nextQueuepos));
                OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);
                yield break;
            }
            
            OnFinishedTrack?.Invoke(this, CurrentPlay, index == -1, ref nextQueuepos);
        }

        public virtual void Update()
        {
            if (Owner == null || !ready || StreamBuffer.Count == 0 || !ShouldPlay) return;

            allowedSamples += Time.deltaTime * samplesPerSecond;
            int toCopy = Mathf.Min(Mathf.FloorToInt(allowedSamples), StreamBuffer.Count);
            if (LogDebug)
                Log.Debug($"1 {toCopy} {allowedSamples} {samplesPerSecond} {StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");
            if (toCopy > 0)
            {
                for (int i = 0; i < toCopy; i++)
                {
                    PlaybackBuffer.Write(StreamBuffer.Dequeue() * (Volume / 100f));
                }
            }

            if (LogDebug)
                Log.Debug($"2 {toCopy} {allowedSamples} {samplesPerSecond} {StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");
            
            allowedSamples -= toCopy;

            while (PlaybackBuffer.Length >= 480)
            {
                PlaybackBuffer.ReadTo(SendBuffer, (long)480, 0L);
                int dataLen = Encoder.Encode(SendBuffer, EncodedBuffer, 480);
                
                foreach (var plr in ReferenceHub.AllHubs)
                {
                    if (plr.connectionToClient == null || !PlayerIsConnected(plr) || (BroadcastTo.Count >= 1 && !BroadcastTo.Contains(plr.PlayerId))) continue;
                    
                    plr.connectionToClient.Send(new VoiceMessage(Owner, BroadcastChannel, EncodedBuffer, dataLen, false));
                }
            }
        }

        private bool PlayerIsConnected(ReferenceHub hub)
        {
            return hub.characterClassManager.InstanceMode == ClientInstanceMode.ReadyClient && hub.nicknameSync.NickSet;
        }
    }
}
