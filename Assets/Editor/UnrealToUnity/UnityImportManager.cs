using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class UnityImportManager : EditorWindow
{
    private string importPath = "H:/EXPORT_UNREALENGINE/";
    internal ImportReport importReport;
    private Vector2 scrollPosition;
    private bool showImportSettings = true;
    private bool showAssetsList = true;
    private bool createSubfolders = true;
    private bool overwriteExisting = true;
    private TextureImportSettings textureSettings = new TextureImportSettings();
    
    [System.Serializable]
    public class ImportReport
    {
        public ExportSession export_session;
        public List<ExportedAsset> assets;
    }
    
    [System.Serializable]
    public class ExportSession
    {
        public string timestamp;
        public int total_assets;
        public string export_path;
    }
    
    [System.Serializable]
    public class ExportedAsset
    {
        public string type;
        public string name;
        public string path;
        public string timestamp;
        public List<TextureExport> textures;
    }

    [System.Serializable]
    public class TextureExport
    {
        public string parameter;
        public string path;
    }

    [System.Serializable]
    public class TextureImportSettings
    {
        public bool generateMipMaps = true;
        public FilterMode filterMode = FilterMode.Bilinear;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public int maxTextureSize = 2048;
    }

    [MenuItem("Tools/Unreal Importer")]
    public static void ShowWindow()
    {
        GetWindow<UnityImportManager>("Unreal Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Unreal to Unity Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Import Path Section
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Import Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        importPath = EditorGUILayout.TextField("Export Path:", importPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string newPath = EditorUtility.OpenFolderPanel("Select Export Directory", importPath, "");
            if (!string.IsNullOrEmpty(newPath))
            {
                importPath = newPath + "/";
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Import Options Section
        EditorGUILayout.BeginVertical("box");
        showImportSettings = EditorGUILayout.Foldout(showImportSettings, "Advanced Settings", true);
        
        if (showImportSettings)
        {
            EditorGUI.indentLevel++;
            
            createSubfolders = EditorGUILayout.Toggle("Create Type Subfolders", createSubfolders);
            overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", overwriteExisting);
            
            EditorGUILayout.Space();
            GUILayout.Label("Texture Settings:", EditorStyles.miniBoldLabel);
            textureSettings.generateMipMaps = EditorGUILayout.Toggle("Generate Mip Maps", textureSettings.generateMipMaps);
            textureSettings.filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", textureSettings.filterMode);
            textureSettings.wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup("Wrap Mode", textureSettings.wrapMode);
            textureSettings.maxTextureSize = EditorGUILayout.IntSlider("Max Size", textureSettings.maxTextureSize, 32, 8192);
            
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Action Buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Export Report", GUILayout.Height(30)))
        {
            LoadExportReport();
        }
        
        GUI.enabled = importReport != null;
        if (GUILayout.Button("Import All Assets", GUILayout.Height(30)))
        {
            ImportAllAssets();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Assets List Section
        if (importReport != null && importReport.assets != null)
        {
            EditorGUILayout.BeginVertical("box");
            showAssetsList = EditorGUILayout.Foldout(showAssetsList, $"Assets to Import ({importReport.assets.Count})", true);
            
            if (showAssetsList)
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
                
                foreach (var asset in importReport.assets)
                {
                    EditorGUILayout.BeginHorizontal();
                    
                    // Icon based on asset type
                    GUIContent content = new GUIContent($" {asset.name}", GetAssetIcon(asset.type));
                    EditorGUILayout.LabelField(content, GUILayout.Width(250));
                    
                    EditorGUILayout.LabelField(asset.type, GUILayout.Width(100));
                    
                    // Quick import button
                    if (GUILayout.Button("Import", GUILayout.Width(60)))
                    {
                        ImportSingleAsset(asset);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        // Status Section
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Status", EditorStyles.boldLabel);
        
        if (importReport == null)
        {
            EditorGUILayout.HelpBox("No export report loaded. Click 'Load Export Report' to begin.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"Ready to import {importReport.assets?.Count ?? 0} assets from Unreal Engine export.", MessageType.Info);
        }
        
        EditorGUILayout.EndVertical();
    }

    private Texture2D GetAssetIcon(string assetType)
    {
        string iconName = "DefaultAsset Icon";
        
        switch (assetType)
        {
            case "StaticMesh":
            case "SkeletalMesh":
                iconName = "MeshFilter Icon";
                break;
            case "Animation":
                iconName = "AnimationClip Icon";
                break;
            case "Material":
                iconName = "Material Icon";
                break;
            case "Texture":
                iconName = "Texture2D Icon";
                break;
        }
        
        return EditorGUIUtility.IconContent(iconName).image as Texture2D;
    }

    internal void LoadExportReport()
    {
        string reportPath = Path.Combine(importPath, "export_report.json");
        
        if (!File.Exists(reportPath))
        {
            EditorUtility.DisplayDialog("Error", $"Export report not found at:\n{reportPath}", "OK");
            return;
        }

        try
        {
            string json = File.ReadAllText(reportPath);
            importReport = JsonUtility.FromJson<ImportReport>(json);
            
            if (importReport?.assets != null)
            {
                Debug.Log($"✅ Export report loaded: {importReport.assets.Count} assets found");
                ShowNotification(new GUIContent($"Loaded {importReport.assets.Count} assets"));
            }
            else
            {
                EditorUtility.DisplayDialog("Warning", "Export report is empty or invalid.", "OK");
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to load export report:\n{e.Message}", "OK");
            Debug.LogError($"Failed to load export report: {e}");
        }
    }

    internal void ImportAllAssets()
    {
        if (importReport?.assets == null || importReport.assets.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No assets to import.", "OK");
            return;
        }

        int successCount = 0;
        int errorCount = 0;

        // Create root import directory
        string rootImportPath = "Assets/Imported/UnrealAssets";
        EnsureDirectoryExists(rootImportPath);

        // Group assets by type for organized import
        var assetsByType = importReport.assets.GroupBy(a => a.type);

        foreach (var typeGroup in assetsByType)
        {
            string typeFolder = createSubfolders ? Path.Combine(rootImportPath, typeGroup.Key) : rootImportPath;
            
            if (createSubfolders)
            {
                EnsureDirectoryExists(typeFolder);
            }

            foreach (var asset in typeGroup)
            {
                if (ImportAsset(asset, typeFolder))
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                }
            }
        }

        // Apply texture settings to all imported textures
        ApplyTextureSettings();

        // Refresh asset database
        AssetDatabase.Refresh();

        // Show results
        string results = $"Import Complete!\n\nSuccess: {successCount}\nErrors: {errorCount}";
        EditorUtility.DisplayDialog("Import Results", results, "OK");
        
        Debug.Log($"🎉 Import completed: {successCount} successful, {errorCount} failed");
    }

    private void ImportSingleAsset(ExportedAsset asset)
    {
        string rootImportPath = "Assets/Imported/UnrealAssets";
        string typeFolder = createSubfolders ? Path.Combine(rootImportPath, asset.type) : rootImportPath;
        
        if (createSubfolders)
        {
            EnsureDirectoryExists(typeFolder);
        }

        if (ImportAsset(asset, typeFolder))
        {
            AssetDatabase.Refresh();
            ApplyTextureSettings();
            
            Debug.Log($"✅ Imported: {asset.name}");
            ShowNotification(new GUIContent($"Imported {asset.name}"));
        }
        else
        {
            EditorUtility.DisplayDialog("Error", $"Failed to import: {asset.name}", "OK");
        }
    }

    private bool ImportAsset(ExportedAsset asset, string destinationFolder)
    {
        string sourcePath = asset.path;
        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(destinationFolder, fileName);

        if (!File.Exists(sourcePath))
        {
            Debug.LogWarning($"Source file missing: {sourcePath}");
            return false;
        }

        // Check if file already exists
        if (File.Exists(destPath) && !overwriteExisting)
        {
            Debug.Log($"Skipped (already exists): {asset.name}");
            return true;
        }

        try
        {
            // Ensure destination directory exists
            EnsureDirectoryExists(Path.GetDirectoryName(destPath));

            // Copy the file
            File.Copy(sourcePath, destPath, overwriteExisting);
            
            Debug.Log($"✅ Imported: {asset.type}/{asset.name}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Failed to import {asset.name}: {e.Message}");
            return false;
        }
    }

    private void ApplyTextureSettings()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Imported/UnrealAssets" });
        
        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            
            if (importer != null)
            {
                importer.mipmapEnabled = textureSettings.generateMipMaps;
                importer.filterMode = textureSettings.filterMode;
                importer.wrapMode = textureSettings.wrapMode;
                importer.maxTextureSize = textureSettings.maxTextureSize;
                importer.SaveAndReimport();
            }
        }

        if (textureGuids.Length > 0)
        {
            Debug.Log($"🔧 Applied texture settings to {textureGuids.Length} textures");
        }
    }

    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    // Auto-refresh when import path changes
    private void OnInspectorUpdate()
    {
        Repaint();
    }

    // Context menu for quick access
    [MenuItem("Assets/Import From Unreal", false, 300)]
    static void QuickImportFromUnreal()
    {
        ShowWindow();
    }

    // Validate context menu item
    [MenuItem("Assets/Import From Unreal", true)]
    static bool ValidateQuickImportFromUnreal()
    {
        return true;
    }
}

// Editor utility for batch processing
public static class UnrealImportUtilities
{
    [MenuItem("Tools/Unreal Importer/Quick Import Latest")]
    public static void QuickImportLatest()
    {
        var window = EditorWindow.GetWindow<UnityImportManager>();
        window.LoadExportReport();
        
        if (window.importReport != null)
        {
            window.ImportAllAssets();
        }
    }

    [MenuItem("Tools/Unreal Importer/Open Export Folder")]
    public static void OpenExportFolder()
    {
        string defaultPath = "H:/EXPORT_UNREALENGINE/";
        if (Directory.Exists(defaultPath))
        {
            EditorUtility.RevealInFinder(defaultPath);
        }
        else
        {
            EditorUtility.DisplayDialog("Info", "Export folder not found. Please check the path in Unreal Importer window.", "OK");
        }
    }

    [MenuItem("Tools/Unreal Importer/Clean Imported Assets")]
    public static void CleanImportedAssets()
    {
        if (EditorUtility.DisplayDialog("Clean Imported Assets", 
            "This will delete all assets in Assets/Imported/UnrealAssets. Continue?", "Yes", "No"))
        {
            if (Directory.Exists("Assets/Imported/UnrealAssets"))
            {
                Directory.Delete("Assets/Imported/UnrealAssets", true);
                File.Delete("Assets/Imported/UnrealAssets.meta");
                AssetDatabase.Refresh();
                Debug.Log("🧹 Cleaned imported assets");
            }
        }
    }
}