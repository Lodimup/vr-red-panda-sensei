#nullable enable
using System.Collections;
using System.Text;
using Sensei.Engine.Speech;
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class CaptionPanel : MonoBehaviour
    {
        private Transform head = null!; private TextMeshPro text = null!; private Coroutine? clear;
        public void Initialize(Transform headTransform)
        {
            head = headTransform;
            text = gameObject.AddComponent<TextMeshPro>();
            text.font = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP");
            text.fontSize = 0.45f; text.richText = false; text.alignment = TextAlignmentOptions.Center; text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(0.6f, 0.24f);
            text.textWrappingMode = TextWrappingModes.Normal;
            transform.localPosition = new Vector3(-0.70f, 0.72f, -0.25f);
        }
        public void Show(SpeechSegment segment, SpeechCaptions visibility)
        {
            if (clear != null) StopCoroutine(clear);
            Debug.Log("RPS_CAPTION lang=" + segment.Lang);
            var lines = new StringBuilder();
            if (segment.Lang == "en" || visibility.ShowJapanese) lines.Append(segment.Text);
            if (segment.Lang == "ja")
            {
                if (visibility.ShowReading && !string.IsNullOrWhiteSpace(segment.Reading)) lines.Append('\n').Append(segment.Reading);
                if (visibility.ShowRomaji && !string.IsNullOrWhiteSpace(segment.Romaji)) lines.Append('\n').Append(segment.Romaji);
                if (visibility.ShowTranslation && !string.IsNullOrWhiteSpace(segment.Translation)) lines.Append('\n').Append(segment.Translation);
            }
            text.text = lines.ToString();
        }
        public void ShowError(string message) { if (clear != null) StopCoroutine(clear); text.text = message; clear = StartCoroutine(ClearLater()); }
        public void End() { if (clear != null) StopCoroutine(clear); clear = StartCoroutine(ClearLater()); }
        private IEnumerator ClearLater() { yield return new WaitForSeconds(4); text.text = ""; clear = null; }
        private void LateUpdate() { var direction = head.position - transform.position; direction.y = 0; if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-direction); }
    }
}
