#nullable enable
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Sensei.App.Core
{
    public enum SenseiPose { IdleFloat, Listen, Think, Speak, Point, Encourage, Demonstrate }

    public sealed class PoseState
    {
        private bool talkHeld;
        private string state = "idle";
        private SenseiPose animation = SenseiPose.IdleFloat;
        private double animationAt = double.NegativeInfinity;
        public void SetTalkHeld(bool held) => talkHeld = held;
        public void SetEngineState(string value) => state = value;
        public void OnAnimate(string value, double now)
        {
            switch (value)
            {
                case "idle_float": animation = SenseiPose.IdleFloat; break;
                case "listen": animation = SenseiPose.Listen; break;
                case "think": animation = SenseiPose.Think; break;
                case "speak": animation = SenseiPose.Speak; break;
                case "point": animation = SenseiPose.Point; break;
                case "encourage": animation = SenseiPose.Encourage; break;
                case "demonstrate": animation = SenseiPose.Demonstrate; break;
                default: return;
            }
            animationAt = now;
        }
        public SenseiPose Current(double now)
        {
            if (talkHeld) return SenseiPose.Listen;
            if (state == "thinking") return SenseiPose.Think;
            if (state == "speaking") return SenseiPose.Speak;
            return now >= animationAt && now - animationAt < 3 ? animation : SenseiPose.IdleFloat;
        }
    }

    public sealed class MainThreadQueue
    {
        private readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        public void Post(Action action) => actions.Enqueue(action ?? throw new ArgumentNullException(nameof(action)));
        public int Drain(int max = 64)
        {
            var count = 0;
            while (count < max && actions.TryDequeue(out var action)) { action(); count++; }
            return count;
        }
    }

    public static class TranscriptGate
    {
        public const float MinHoldSeconds = 0.3f;
        public static bool ShouldSend(float holdSeconds, string? transcript) => holdSeconds >= MinHoldSeconds && !string.IsNullOrWhiteSpace(transcript);
    }

    public static class TurnMessages
    {
        public static string? ForTurn(string status, string? errorCode)
        {
            if (status != "failed") return null;
            switch (errorCode)
            {
                case "auth_failed": return "The API key was rejected.";
                case "quota_exceeded": return "The model quota is used up.";
                case "network_error": return "No connection to the model.";
                case "provider_error": return "The model service had an error.";
                default: return "Something went wrong. Try again.";
            }
        }
    }
}
