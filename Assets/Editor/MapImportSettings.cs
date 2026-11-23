using UnityEditor;

// Makes sure the map FBX is imported with mesh colliders (so the gorilla hands can grab it).
public class MapImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Resources/Map/")) return;
        var importer = (ModelImporter)assetImporter;
        importer.addCollider = true;
        importer.isReadable = true;
        importer.importCameras = false;
        importer.importLights = false;
    }
}
