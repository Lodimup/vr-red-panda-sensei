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
            var text = go.AddComponent<TextMeshPro>(); text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, .5f); text.rectTransform.pivot = new Vector2(.5f, .5f); text.rectTransform.sizeDelta = size; text.rectTransform.localPosition = position; text.fontSize = fontSize; text.enableAutoSizing = true; text.fontSizeMin = 0.10f; text.fontSizeMax = fontSize;
            text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.Normal;
            if (font != null) text.font = font;
            return text;
        }
        internal static GameObject Surface(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            var material = new Material(Resources.Load<Material>("SenseiCard")); material.color = new Color(.12f, .10f, .09f); material.renderQueue = 2980; go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>(); if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider); return go;
        }
    }
}
