#nullable enable
using System.Linq;
using Sensei.App.Core;
using TMPro;
using UnityEngine;
using EngineHint = Sensei.Engine.Events.HintCard;

namespace Sensei.App
{
    public sealed class HintCard : MonoBehaviour
    {
        private string? key; private TextMeshPro text = null!;
        public void SetKey(string? value) => key = value;
        public void Initialize(Transform area, TMP_FontAsset? font, Transform? head = null) { transform.SetParent(area, false); var slot = PanelLayout.Hint; transform.localPosition = slot.Centre; PanelText.Surface(transform, "Hint background", Vector3.zero, new Vector3(slot.Size.x, slot.Size.y, .01f)); text = PanelText.Create(transform, "Hint", new Vector3(0, 0, -.01f), slot.Size - new Vector2(.03f, .02f), 0.23f, font); if (head != null) gameObject.AddComponent<PanelBillboard>().Initialize(head); gameObject.SetActive(false); }
        public void Show(EngineHint hint) { text.text = LogRedactor.Redact(string.Join("\n", new[] { hint.Kind.Replace('_', ' '), hint.Japanese, hint.Reading, hint.Romaji, hint.English, hint.TargetKey }.Where(s => !string.IsNullOrEmpty(s))), key); gameObject.SetActive(true); }
        public void Clear() => gameObject.SetActive(false);
    }
}
