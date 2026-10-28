using TMPro;
using UnityEditor;
using UnityEngine;

namespace Sensei.Editor
{
    // A dynamic SDF retains the source font for Japanese glyphs added at runtime.
    public static class NotoFontAsset
    {
        private const string FontPath = "Assets/Resources/Fonts/NotoSansJP-Regular.otf";
        private const string AssetPath = "Assets/Resources/Fonts/NotoSansJP.asset";
        [InitializeOnLoadMethod]
        private static void Schedule() => EditorApplication.delayCall += Ensure;
        public static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return;
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
            AssetDatabase.SaveAssets();
        }
    }
}
