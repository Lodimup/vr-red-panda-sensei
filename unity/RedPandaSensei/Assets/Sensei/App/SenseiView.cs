#nullable enable
using Sensei.App.Core;
using UnityEngine;

namespace Sensei.App
{
    public sealed class SenseiView : MonoBehaviour
    {
        private readonly PoseState state = new PoseState(); private Transform head = null!; private Transform image = null!; private Renderer renderer = null!; private SenseiPose last = (SenseiPose)(-1); private float bounceAt = -10; private float nextBounce; private string? pointTarget; private PracticeAreaView area = null!;
        public void Initialize(Transform headTransform, PracticeAreaView areaView)
        {
            head = headTransform; area = areaView;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(go.GetComponent<Collider>()); go.name = "Sensei"; image = go.transform; image.SetParent(transform, false); renderer = go.GetComponent<Renderer>(); renderer.material = new Material(Resources.Load<Material>("SenseiGhost"));
        }
        public void SetState(string value) => state.SetEngineState(value);
        public void SetTalkHeld(bool held) => state.SetTalkHeld(held);
        public void Animate(string name, string? target) { state.OnAnimate(name, Time.timeAsDouble); pointTarget = target; }
        // U2 calls this for an RMS peak over 0.02 in a 50 ms window.
        public void OnSpeechPeak(float rms) { if (rms > 0.02f && Time.time >= nextBounce) { bounceAt = Time.time; nextBounce = Time.time + 0.125f; } }
        private void Update()
        {
            var pose = state.Current(Time.timeAsDouble);
            if (pose != last)
            {
                var name = pose switch { SenseiPose.IdleFloat => "idle_float", SenseiPose.Listen => "listen", SenseiPose.Think => "think", SenseiPose.Speak => "speak", SenseiPose.Point => "point", SenseiPose.Encourage => "encourage", _ => "demonstrate" };
                var texture = Resources.Load<Texture2D>("Sensei/" + name + "_card") ?? Resources.Load<Texture2D>("Sensei/" + name) ?? Resources.Load<Texture2D>("Sensei/idle_float_card") ?? Resources.Load<Texture2D>("Sensei/idle_float");
                renderer.material.mainTexture = texture; last = pose;
            }
            var textureNow = renderer.material.mainTexture; float aspect = textureNow != null ? (float)textureNow.width / textureNow.height : 1;
            var bounce = Mathf.Clamp01(1 - (Time.time - bounceAt) / 0.125f); var squish = Mathf.Sin(bounce * Mathf.PI) * 0.03f;
            float flip = 1; if (pose == SenseiPose.Point && pointTarget != null) { var card = area.Find(pointTarget); if (card != null && transform.InverseTransformPoint(card.transform.position).x > -0.85f) flip = -1; }
            image.localScale = new Vector3(0.75f * aspect * flip, 0.75f * (1 - squish), 1);
            image.localPosition = new Vector3(-0.85f, 0.75f + 0.375f + 0.02f * Mathf.Sin(Time.time * 0.4f * 2 * Mathf.PI) + squish / 3, 0.25f);
            var direction = head.position - image.position; direction.y = 0; if (direction.sqrMagnitude > 0.001f) image.rotation = Quaternion.LookRotation(-direction);
        }
    }
}
