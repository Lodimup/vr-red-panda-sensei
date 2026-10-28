#nullable enable
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    internal static class PanelText
    {
        internal static TextMeshPro Create(Transform parent, string name, Vector3 position, Vector2 size, float fontSize, TMP_FontAsset? font)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var text = go.AddComponent<TextMeshPro>(); text.rectTransform.sizeDelta = size; text.fontSize = fontSize;
            text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.Normal;
            if (font != null) text.font = font;
            return text;
        }
        internal static GameObject Surface(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().material = new Material(Resources.Load<Material>("SenseiCard")); go.GetComponent<Renderer>().material.color = new Color(.12f, .10f, .09f);
            Object.Destroy(go.GetComponent<Collider>()); return go;
        }
    }
}
