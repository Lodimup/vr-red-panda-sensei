#nullable enable
using System.Linq;
using TMPro;
using UnityEngine;
using EngineHint = Sensei.Engine.Events.HintCard;

namespace Sensei.App
{
    public sealed class HintCard : MonoBehaviour
    {
        private TextMeshPro text = null!;
        public void Initialize(Transform area, TMP_FontAsset? font) { transform.SetParent(area, false); transform.localPosition = new Vector3(.55f, 1.35f, 0); PanelText.Surface(transform, "Hint background", Vector3.zero, new Vector3(.4f, .25f, .01f)); text = PanelText.Create(transform, "Hint", new Vector3(0, 0, -.01f), new Vector2(.37f, .23f), 0.23f, font); gameObject.SetActive(false); }
        public void Show(EngineHint hint) { text.text = string.Join("\n", new[] { hint.Kind.Replace('_', ' '), hint.Japanese, hint.Reading, hint.Romaji, hint.English, hint.TargetKey }.Where(s => !string.IsNullOrEmpty(s))); gameObject.SetActive(true); }
        public void Clear() => gameObject.SetActive(false);
    }
}
