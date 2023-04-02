using SCPSLAudioApi.Api;

namespace SCPSLAudioApi.Events.Handlers
{
    public static class Track
    {
        /// <summary>
        ///     Fired when a track finishes.
        /// </summary>
        /// <param name="audioPlayer">The AudioPlayer instance that this event fired for</param>
        /// <param name="track">The track the AudioPlayer was playing</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="nextQueuePos">Position in the Queue that will play next, can be set to a different value</param>
        public delegate void TrackFinished(AudioPlayerBase audioPlayer, string track, bool directPlay,
            ref int nextQueuePos);

        /// <summary>
        ///     Fired when a track is loaded and will begin playing.
        /// </summary>
        /// <param name="audioPlayer">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue that will play</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackLoaded(AudioPlayerBase audioPlayer, bool directPlay, int queuePos, string track);

        /// <summary>
        ///     Fired when a track has been selected
        /// </summary>
        /// <param name="audioPlayer">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that will start</param>
        /// <param name="track">The track the AudioPlayer will play</param>
        public delegate void TrackSelected(AudioPlayerBase audioPlayer, bool directPlay, int queuePos,
            ref string track);

        /// <summary>
        ///     Fired when a track is getting selected.
        /// </summary>
        /// <param name="audioPlayer">The AudioPlayer instance that this event fired for</param>
        /// <param name="directPlay">If the AudioPlayer was playing Directly (-1 index)</param>
        /// <param name="queuePos">Position in the Queue of the track that is going to be selected</param>
        public delegate void TrackSelecting(AudioPlayerBase audioPlayer, bool directPlay, ref int queuePos);

        public static event TrackSelecting OnTrackSelecting;

        public static void InvokeTrackSelectingEvent(AudioPlayerBase audioPlayer, bool directPlay, ref int queuePos)
        {
            OnTrackSelecting?.Invoke(audioPlayer, directPlay, ref queuePos);
        }

        public static event TrackSelected OnTrackSelected;

        public static void InvokeTrackSelectedEvent(AudioPlayerBase audioPlayer, bool directPlay, int queuePos,
            ref string track)
        {
            OnTrackSelected?.Invoke(audioPlayer, directPlay, queuePos, ref track);
        }

        public static event TrackLoaded OnTrackLoaded;

        public static void InvokeTrackLoadedEvent(AudioPlayerBase audioPlayer, bool directPlay, int queuePos,
            string track)
        {
            OnTrackLoaded?.Invoke(audioPlayer, directPlay, queuePos, track);
        }

        public static event TrackFinished OnFinishedTrack;

        public static void InvokeFinishedTrackEvent(AudioPlayerBase audioPlayer, string track, bool directPlay,
            ref int nextQueuePos)
        {
            OnFinishedTrack?.Invoke(audioPlayer, track, directPlay, ref nextQueuePos);
        }
    }
}