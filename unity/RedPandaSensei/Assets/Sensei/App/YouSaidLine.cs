#nullable enable
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class YouSaidLine : MonoBehaviour
    {
        private Transform head = null!; private TextMeshPro text = null!;
        public void Initialize(Transform headTransform)
        {
            head = headTransform; text = gameObject.AddComponent<TextMeshPro>(); text.font = Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP");
            text.fontSize = 0.28f; text.richText = false; text.alignment = TextAlignmentOptions.Center; text.color = Color.white;
            text.rectTransform.sizeDelta = new Vector2(0.6f, 0.1f); transform.localPosition = new Vector3(-0.85f, 0.49f, -0.40f);
        }
        public void Set(string value) => text.text = value;
        private void LateUpdate() { var direction = head.position - transform.position; direction.y = 0; if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-direction); }
    }
}
