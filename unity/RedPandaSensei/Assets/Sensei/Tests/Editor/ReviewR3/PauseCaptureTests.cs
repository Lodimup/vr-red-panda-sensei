using System.Reflection;
using NUnit.Framework;
using Sensei.App;
using UnityEngine;

public class PauseCaptureTests
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    [Test] public void PauseCancelsHeldTalkAndInvalidatesPendingRecognition()
    {
        var go = new GameObject("host");
        try {
            var host = go.AddComponent<EngineHost>();
            var ptt = go.AddComponent<PushToTalk>();
            typeof(EngineHost).GetField("pushToTalk", PrivateInstance).SetValue(host, ptt);
            typeof(PushToTalk).GetField("held", PrivateInstance).SetValue(ptt, true);
            typeof(PushToTalk).GetField("captureEpoch", PrivateInstance).SetValue(ptt, 3);
            typeof(EngineHost).GetMethod("OnApplicationPause", PrivateInstance).Invoke(host, new object[] { true });
            Assert.That((bool)typeof(PushToTalk).GetField("held", PrivateInstance).GetValue(ptt), Is.False, "pause must cancel held microphone capture");
            Assert.That((int)typeof(PushToTalk).GetField("captureEpoch", PrivateInstance).GetValue(ptt), Is.GreaterThan(3), "pause must invalidate recognition in flight");
        } finally { Object.DestroyImmediate(go); }
    }
}
