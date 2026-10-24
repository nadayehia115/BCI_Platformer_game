using UnityEditor;

// The start screen picture (Assets/Resources/StartLogo.jpg) is a Sprite that keeps its size: as a plain texture Unity would stretch a
// picture that is not a power of two (1024 x 731) to 1024 x 1024.
public class StartLogoImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath != "Assets/Resources/StartLogo.jpg") return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
    }
}
