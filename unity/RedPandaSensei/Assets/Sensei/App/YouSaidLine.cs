#nullable enable
using Sensei.App.Core;
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class YouSaidLine : MonoBehaviour
    {
        private string? key; public void SetKey(string? value) => key = value;
        private Transform head = null!; private TextMeshPro text = null!;
        public void Initialize(Transform headTransform)
        {
            head = headTransform; text = gameObject.AddComponent<TextMeshPro>(); text.font = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP");
            text.fontSize = 0.28f; text.richText = false; text.alignment = TextAlignmentOptions.Center; text.color = Color.white;
            text.rectTransform.sizeDelta = PanelLayout.YouSaid.Size; transform.localPosition = PanelLayout.YouSaid.Centre;
        }
        public void Set(string value) => text.text = LogRedactor.Redact(value, key);
        private void LateUpdate() { var direction = head.position - transform.position; direction.y = 0; if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-direction); }
    }
}
