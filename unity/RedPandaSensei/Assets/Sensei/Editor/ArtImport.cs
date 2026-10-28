using UnityEditor;

namespace Sensei.Editor
{
    public sealed class ArtImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Art/")) return;
            var texture = (TextureImporter)assetImporter;
            texture.mipmapEnabled = true;
            texture.maxTextureSize = 1024;
            texture.alphaIsTransparency = true;
        }
    }
}
