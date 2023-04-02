using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exiled.API.Features;
using MEC;
using NVorbis;
using SCPSLAudioApi.Api;
using SCPSLAudioApi.Events.Handlers;
using UnityEngine;
using UnityEngine.Networking;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;
using Random = UnityEngine.Random;

namespace SCPSLAudioApi.AudioCore
{
    public class AudioPlayerComponent : MonoBehaviour
    {
        public AudioPlayerBase AudioPlayerBase { get; private set; }

        public void Start()
        {
            AudioPlayerBase = AudioPlayerBase.Get(ReferenceHub.GetHub(this));
        }

        public virtual void Update()
        {
            if (!ready)
                return;
            
            if (AudioPlayerBase.ReferenceHub == null)
                return;

            if (StreamBuffer.Count == 0 || !AudioPlayerBase.ShouldPlay)
                return;

            allowedSamples += Time.deltaTime * samplesPerSecond;
            var toCopy = Mathf.Min(Mathf.FloorToInt(allowedSamples), StreamBuffer.Count);

            if (AudioPlayerBase.VerboseLogs)
                Log.Info($"1 {toCopy} {allowedSamples} {samplesPerSecond} " +
                         $"{StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");

            if (toCopy > 0)
                for (var i = 0; i < toCopy; i++)
                    PlaybackBuffer.Write(StreamBuffer.Dequeue() * (AudioPlayerBase.Volume / 100f));

            if (AudioPlayerBase.VerboseLogs)
                Log.Info($"2 {toCopy} {allowedSamples} {samplesPerSecond} " +
                         $"{StreamBuffer.Count} {PlaybackBuffer.Length} {PlaybackBuffer.WriteHead}");

            allowedSamples -= toCopy;

            while (PlaybackBuffer.Length >= 480)
            {
                PlaybackBuffer.ReadTo(SendBuffer, 480);
                var dataLen = Encoder.Encode(SendBuffer, EncodedBuffer);

                foreach (var plr in ReferenceHub.AllHubs)
                {
                    if (plr.connectionToClient == null ||
                        (AudioPlayerBase.BroadcastTo.Count > 0 && !AudioPlayerBase.BroadcastTo.Contains(plr.PlayerId)))
                        continue;

                    plr.connectionToClient.Send(new VoiceMessage(AudioPlayerBase.ReferenceHub,
                        AudioPlayerBase.BroadcastChannel,
                        EncodedBuffer, dataLen, false));
                }
            }
        }

        public virtual void OnDestroy()
        {
            if (PlaybackCoroutine.IsValid)
                Timing.KillCoroutines(PlaybackCoroutine);

            AudioPlayerBase.AudioPlayers.Remove(AudioPlayerBase.ReferenceHub);
        }

        internal virtual IEnumerator<float> Playback(int position)
        {
            stopTrack = false;
            var index = position;

            Track.InvokeTrackSelectingEvent(AudioPlayerBase, index == -1, ref index);

            if (index != -1)
            {
                if (AudioPlayerBase.Shuffle)
                    AudioPlayerBase.AudioToPlay = AudioPlayerBase.AudioToPlay.OrderBy(_ => Random.value).ToList();

                AudioPlayerBase.CurrentPlay = AudioPlayerBase.AudioToPlay[index];
                AudioPlayerBase.AudioToPlay.RemoveAt(index);

                if (AudioPlayerBase.Loop)
                    AudioPlayerBase.AudioToPlay.Add(AudioPlayerBase.CurrentPlay);
            }

            Track.InvokeTrackSelectedEvent(AudioPlayerBase, index == -1, index, ref AudioPlayerBase.CurrentPlay);
            Log.Debug("Loading Audio");

            if (AudioPlayerBase.AllowUrl &&
                Uri.TryCreate(AudioPlayerBase.CurrentPlay, UriKind.Absolute, out var result))
            {
                var webRequest = new UnityWebRequest(AudioPlayerBase.CurrentPlay, "GET");
                var downloadHandler = new DownloadHandlerBuffer();
                webRequest.downloadHandler = downloadHandler;

                yield return Timing.WaitUntilDone(webRequest.SendWebRequest());

                if (webRequest.responseCode != 200)
                {
                    Log.Error($"Failed to retrieve audio {webRequest.responseCode} {webRequest.downloadHandler.text}");

                    if (!AudioPlayerBase.Continue || AudioPlayerBase.AudioToPlay.Count < 1) yield break;

                    yield return Timing.WaitForSeconds(1);

                    if (AudioPlayerBase.AudioToPlay.Count >= 1)
                        Timing.RunCoroutine(Playback(0));

                    yield break;
                }

                AudioPlayerBase.CurrentPlayStream = new MemoryStream(webRequest.downloadHandler.data);
            }
            else
            {
                if (File.Exists(AudioPlayerBase.CurrentPlay))
                {
                    if (!AudioPlayerBase.CurrentPlay.EndsWith(".ogg"))
                    {
                        Log.Error(
                            $"Audio file {AudioPlayerBase.CurrentPlay} is not valid. Audio files must be ogg files");
                        yield return Timing.WaitForSeconds(1);

                        if (AudioPlayerBase.AudioToPlay.Count >= 1)
                            Timing.RunCoroutine(Playback(0));

                        yield break;
                    }

                    AudioPlayerBase.CurrentPlayStream =
                        new MemoryStream(File.ReadAllBytes(AudioPlayerBase.CurrentPlay));
                }
                else
                {
                    Log.Error($"Audio file {AudioPlayerBase.CurrentPlay} does not exist. skipping.");
                    yield return Timing.WaitForSeconds(1);

                    if (AudioPlayerBase.AudioToPlay.Count >= 1)
                        Timing.RunCoroutine(Playback(0));

                    yield break;
                }
            }

            AudioPlayerBase.CurrentPlayStream.Seek(0, SeekOrigin.Begin);
            VorbisReader = new VorbisReader(AudioPlayerBase.CurrentPlayStream);

            if (VorbisReader.Channels >= 2)
            {
                Log.Error($"Audio file {AudioPlayerBase.CurrentPlay} is not valid. Audio files must be mono.");
                yield return Timing.WaitForSeconds(1);

                if (AudioPlayerBase.AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));

                VorbisReader.Dispose();
                AudioPlayerBase.CurrentPlayStream.Dispose();

                yield break;
            }

            if (VorbisReader.SampleRate != 48000)
            {
                Log.Error(
                    $"Audio file {AudioPlayerBase.CurrentPlay} is not valid. Audio files must have a SamepleRate of 48000");
                yield return Timing.WaitForSeconds(1);

                if (AudioPlayerBase.AudioToPlay.Count >= 1)
                    Timing.RunCoroutine(Playback(0));

                VorbisReader.Dispose();
                AudioPlayerBase.CurrentPlayStream.Dispose();

                yield break;
            }

            Track.InvokeTrackLoadedEvent(AudioPlayerBase, index == -1, index, AudioPlayerBase.CurrentPlay);
            Log.Debug($"Playing {AudioPlayerBase.CurrentPlay} with samplerate of {VorbisReader.SampleRate}");

            samplesPerSecond = VoiceChatSettings.SampleRate * VoiceChatSettings.Channels;
            //_samplesPerSecond = VorbisReader.Channels * VorbisReader.SampleRate / 5;
            SendBuffer = new float[samplesPerSecond / 5 + HeadSamples];
            ReadBuffer = new float[samplesPerSecond / 5 + HeadSamples];

            while (VorbisReader.ReadSamples(ReadBuffer, 0, ReadBuffer.Length) > 0)
            {
                if (stopTrack)
                {
                    VorbisReader.SeekTo(VorbisReader.TotalSamples - 1);
                    stopTrack = false;
                }

                while (!AudioPlayerBase.ShouldPlay) yield return Timing.WaitForOneFrame;

                while (StreamBuffer.Count >= ReadBuffer.Length)
                {
                    ready = true;
                    yield return Timing.WaitForOneFrame;
                }

                for (var i = 0; i < ReadBuffer.Length; i++) StreamBuffer.Enqueue(ReadBuffer[i]);
            }

            Log.Debug("Track Complete.");

            var nextQueuePos = 0;

            switch (AudioPlayerBase.Continue)
            {
                case true when AudioPlayerBase.Loop && index == -1:
                    nextQueuePos = -1;

                    Timing.RunCoroutine(Playback(nextQueuePos));
                    Track.InvokeFinishedTrackEvent(AudioPlayerBase, AudioPlayerBase.CurrentPlay, false,
                        ref nextQueuePos);

                    yield break;
                case true when AudioPlayerBase.AudioToPlay.Count >= 1:
                    Timing.RunCoroutine(Playback(nextQueuePos));
                    Track.InvokeFinishedTrackEvent(AudioPlayerBase, AudioPlayerBase.CurrentPlay, index == -1,
                        ref nextQueuePos);

                    yield break;
                default:
                    Track.InvokeFinishedTrackEvent(AudioPlayerBase, AudioPlayerBase.CurrentPlay, index == -1,
                        ref nextQueuePos);
                    break;
            }
        }

        #region Internal

        public const int HeadSamples = 1920;

        public bool stopTrack;

        public bool ready;

        public CoroutineHandle PlaybackCoroutine;

        public int samplesPerSecond;

        public Queue<float> StreamBuffer { get; } = new Queue<float>();

        public VorbisReader VorbisReader { get; set; }

        public float[] SendBuffer { get; set; }

        public float[] ReadBuffer { get; set; }

        public OpusEncoder Encoder { get; } = new OpusEncoder(OpusApplicationType.Voip);

        public PlaybackBuffer PlaybackBuffer { get; } = new PlaybackBuffer();

        public byte[] EncodedBuffer { get; } = new byte[512];

        public float allowedSamples;

        #endregion
    }
}