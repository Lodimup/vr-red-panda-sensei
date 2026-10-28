using UnityEngine;

namespace Sensei.App
{
    public sealed class HighlightView : MonoBehaviour
    {
        private LineRenderer ring = null!;
        private float until;
        public void Initialize(LineRenderer line) { ring = line; gameObject.SetActive(false); }
        public void Show(int durationMs) { until = Time.time + durationMs / 1000f; gameObject.SetActive(true); }
        private void Update()
        {
            if (Time.time >= until) { gameObject.SetActive(false); return; }
            ring.material.color = new Color(1f, 0.835f, 0.29f, 0.65f + 0.25f * Mathf.Sin(Time.time * Mathf.PI * 4));
        }
    }
}
