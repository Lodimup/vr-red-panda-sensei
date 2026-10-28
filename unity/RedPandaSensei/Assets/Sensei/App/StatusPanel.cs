#nullable enable
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class StatusPanel : MonoBehaviour
    {
        private TextMeshPro text = null!; private Renderer background = null!; private readonly List<string> lines = new List<string>(); private float completedAt = -1; private bool error;
        public void Initialize(Transform head, TMP_FontAsset? font)
        {
            transform.SetParent(head, false); transform.localPosition = new Vector3(0, .15f, 1.3f);
            background = PanelText.Surface(transform, "Status background", Vector3.zero, new Vector3(.8f, .35f, .01f)).GetComponent<Renderer>();
            text = PanelText.Create(transform, "Status", new Vector3(0, 0, -.01f), new Vector2(.75f, .32f), 0.24f, font);
        }
        public void Add(string line, bool isError = false) { lines.Add(line); text.text = string.Join("\n", lines); error |= isError; if (isError) { text.alpha = 1; var color = background.sharedMaterial.color; color.a = 1; background.sharedMaterial.color = color; } gameObject.SetActive(true); }
        public void Complete() { completedAt = Time.time; }
        private void Update()
        {
            if (error || completedAt < 0) return;
            var elapsed = Time.time - completedAt; if (elapsed < 5) return;
            var alpha = 1 - Mathf.Clamp01((elapsed - 5) / .5f); text.alpha = alpha;
            var color = background.sharedMaterial.color; color.a = alpha; background.sharedMaterial.color = color;
            if (alpha <= 0) gameObject.SetActive(false);
        }
    }
}
