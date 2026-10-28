using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Sensei.App.Core;

namespace Sensei.Voice
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SpeechPlayer : MonoBehaviour
    {
        private sealed class Playback
        {
            internal TtsAudio Audio;
            internal long Generation;
            internal CancellationToken Token;
            internal bool Started;
            internal Action OnStarted;
            internal readonly TaskCompletionSource<PlayOutcome> Result = new TaskCompletionSource<PlayOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly ConcurrentQueue<Playback> pending = new ConcurrentQueue<Playback>();
        private readonly object admission = new object();
        private AudioSource source;
        private Playback active;
        private AudioClip clip;
        private long generation;
        private int stop;
        private int closed;
        private int mainThreadId;
        public bool IsPlaying => source != null && source.isPlaying;

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
        }

        public Task<PlayOutcome> PlayAsync(TtsAudio audio, long generation, CancellationToken ct, Action onStarted)
        {
            lock (admission)
            {
                if (ct.IsCancellationRequested || generation != Interlocked.Read(ref this.generation) || closed != 0)
                    return Task.FromResult(PlayOutcome.NotStarted);
                if (audio.Samples == null || audio.Samples.Length == 0 || audio.SampleRate <= 0)
                    throw new ArgumentException("Empty or invalid audio", nameof(audio));
                var item = new Playback { Audio = audio, Generation = generation, Token = ct, OnStarted = onStarted };
                pending.Enqueue(item);
                return item.Result.Task;
            }
        }

        // Safe from another thread. Update stops the AudioSource within one frame.
        public void StopAll(long newGeneration)
        {
            lock (admission)
            {
                Interlocked.Exchange(ref generation, newGeneration);
                if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
                {
                    Interlocked.Exchange(ref stop, 0);
                    Finish(false);
                }
                else Interlocked.Exchange(ref stop, 1);
            }
        }

        private void Update()
        {
            if (Interlocked.Exchange(ref stop, 0) != 0) Finish(false);
            if (active != null)
            {
                if (active.Token.IsCancellationRequested || active.Generation != Interlocked.Read(ref generation)) Finish(false);
                else if (!source.isPlaying) Finish(true);
            }
            while (pending.TryDequeue(out Playback item))
            {
                lock (admission)
                {
                    if (item.Token.IsCancellationRequested || item.Generation != Interlocked.Read(ref generation))
                    { item.Result.TrySetResult(PlayOutcome.NotStarted); continue; }
                    Finish(false);
                    active = item;
                    clip = AudioClip.Create("Sensei speech", item.Audio.Samples.Length, 1, item.Audio.SampleRate, false);
                    clip.SetData(item.Audio.Samples, 0);
                    source.clip = clip;
                    source.Play();
                    if (source.isPlaying) { item.Started = true; item.OnStarted?.Invoke(); }
                    Debug.Log($"RPS_PLAY generation={item.Generation}");
                }
            }
        }

        private void Finish(bool success)
        {
            if (source != null) { source.Stop(); source.clip = null; }
            if (clip != null) Destroy(clip);
            clip = null;
            if (active != null) active.Result.TrySetResult(active.Started ? (success ? PlayOutcome.Completed : PlayOutcome.StoppedAfterStart) : PlayOutcome.NotStarted);
            active = null;
        }

        private void OnDisable()
        {
            Finish(false);
            lock (admission)
            {
                closed = 1;
                while (pending.TryDequeue(out Playback item)) item.Result.TrySetResult(PlayOutcome.NotStarted);
            }
        }

        private void OnEnable() { lock (admission) closed = 0; }
    }
}
