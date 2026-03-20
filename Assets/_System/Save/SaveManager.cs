using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;


public static class SaveManager
{
    #region Consts & Utils

    private const string SaveDirectoryName = "Saves";
    private static string SaveDirectoryPath => Application.persistentDataPath + "/" + SaveDirectoryName + "/";
    private static string GuestSaveDirectoryPath => Application.persistentDataPath + "/" + SaveDirectoryName + "/Guest/";

    private const string SaveFilePrefix = "SAVE_";
    private const string TextExtension = ".txt";
    private const string ArchiveEntryName = "save";

    #endregion


    #region Save Directory

    private static DirectoryInfo _saveDirectoryInfo;
    private static DirectoryInfo SaveDirectoryInfo
    {
        get
        {
            if (_saveDirectoryInfo == null)
                GetOrCreateSaveDirectory();

            return _saveDirectoryInfo;
        }
        set { _saveDirectoryInfo = value; }
    }

    private static DirectoryInfo GetOrCreateSaveDirectory()
    {
        if (_saveDirectoryInfo == null)
        {
            if (!Directory.Exists(SaveDirectoryPath))
            {
                Directory.CreateDirectory(SaveDirectoryPath);
            }

            string path = GuestSaveDirectoryPath;

            if (!Directory.Exists(path))
            {
                SaveDirectoryInfo = Directory.CreateDirectory(path);
            }
            else
            {
                SaveDirectoryInfo = Directory.GetParent(path);
            }

            Debug.Log(SaveDirectoryInfo.FullName);

            // Get old saves back
            DirectoryInfo oldSaveFolderDirectoryInfo = null;
            oldSaveFolderDirectoryInfo = Directory.GetParent(SaveDirectoryPath);

            if (oldSaveFolderDirectoryInfo != null)
            {
                foreach (var file in oldSaveFolderDirectoryInfo.EnumerateFiles())
                {
                    if (file.Exists && file.Name.StartsWith(SaveFilePrefix))
                    {
                        var newPath = GetSaveFileFullName(GetSaveFilenameFromFileInfo(file));
                        if (!File.Exists(newPath))
                        {
                            try
                            {
                                Debug.Log("move " + file.FullName + " to " + newPath);
                                file.MoveTo(newPath);
                            }
                            catch (Exception e)
                            {
                                Debug.LogException(e);
                            }
                        }
                    }
                }
            }
        }

        return SaveDirectoryInfo;
    }

#endregion

    #region Save Files

    private static List<FileInfo> m_saveFiles = new();
    public static int SaveFilesCount => m_saveFiles.Count;

    /// <summary>
    /// Fetches all save files in the save directory and sorts them by last access time.
    /// </summary>
    private static void FetchSaveFiles()
    {
        if (SaveDirectoryInfo != null)
        {
            m_saveFiles.Clear();

            foreach (var file in SaveDirectoryInfo.EnumerateFiles())
            {
                if (file.Exists && file.Name.StartsWith(SaveFilePrefix))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(file.Extension))
                        {
                            // var saveFileInfo = new SaveFileInfo(file);
                            // if (saveFileInfo.isValid) m_saveFiles.Add(saveFileInfo);
                        }
                        else if (file.Extension == TextExtension)
                        {
                            // Get content
                            var path = file.FullName.Replace(TextExtension, "");
                            var content = File.ReadAllText(file.FullName);
                            // Delete
                            file.Delete();
                            // Create new zip
                            var fileInfo = CreateNewSaveFile(path, content, false);
                            // var saveFileInfo = new SaveFileInfo(fileInfo);
                            // if (saveFileInfo.isValid) m_saveFiles.Add(saveFileInfo);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            m_saveFiles.Sort((f1, f2) => -f1.LastWriteTime.CompareTo(f2.LastWriteTime));
        }
    }

    private static FileInfo CreateNewSaveFile(string content)
    {
        var now = DateTime.Now;
        var path = GetSaveFileFullName(now.Year.ToString() + now.Month.ToString() + now.Day.ToString() + now.Hour.ToString() + now.Minute.ToString() + now.Second.ToString());

        return CreateNewSaveFile(path, content, true);
    }
    private static FileInfo CreateNewSaveFile(string path, string content, bool setCurrent)
    {
        if (!File.Exists(path))
        {
            try
            {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry(ArchiveEntryName);

                    using (var stream = entry.Open())
                    {
                        stream.Write(Encoding.UTF8.GetBytes(content));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        var newSaveFile = new FileInfo(path);
        if (setCurrent) SetCurrentSaveFile(newSaveFile);
        Debug.Log("New save file :\n" + newSaveFile.FullName);
        return newSaveFile;
    }

    #endregion

    #region Save File Access

    private static bool ReadSaveFile(string path, out string content)
    {
        content = null;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            if (path.EndsWith(TextExtension))
            {
                Debug.Log("The file at this path is a text, " + path);

                try
                {
                    content = File.ReadAllText(path);
                }
                catch (Exception e)
                {
                    content = null;
                    Debug.LogException(e);
                }
            }

            else
            {
                try
                {
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
                        {
                            var entry = archive.GetEntry(ArchiveEntryName);

                            if (entry != null)
                            {
                                using (var stream = entry.Open())
                                {
                                    using (var memoryStream = new MemoryStream())
                                    {
                                        stream.CopyTo(memoryStream);
                                        content = Encoding.UTF8.GetString(memoryStream.ToArray());
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    content = null;
                    Debug.LogException(e);
                }
            }

            return !string.IsNullOrWhiteSpace(content)
                    && content.StartsWith('{')
                    && content.EndsWith('}');
        }
        return false;
    }

    private static bool WriteSaveFile(string path, string content)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    var formerEntry = archive.GetEntry(ArchiveEntryName);
                    if (formerEntry != null) formerEntry.Delete();

                    var entry = archive.CreateEntry(ArchiveEntryName);

                    using (var stream = entry.Open())
                    {
                        stream.Write(Encoding.UTF8.GetBytes(content));
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
        return false;
    }

    private static bool DeleteSaveFile(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        return false;
    }

    private static bool RenameSaveFile(string path, string newName)
    {
        if (File.Exists(path))
        {
            var newPath = GetSaveFileFullName(newName);

            if (!File.Exists(newPath))
            {
                try
                {
                    File.Move(path, newPath);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
        return false;
    }

    #endregion

    #region Save Files Management

    private static void Refresh()
    {
        GetOrCreateSaveDirectory();
        FetchSaveFiles();
    }

    private static string SelectedSaveFile { get; set; }
    public static void SetCurrentSaveFile(FileInfo fileInfo)
    {
        if (fileInfo == null)
        {
            SelectedSaveFile = null;
        }
        else
        {
            SelectedSaveFile = fileInfo.FullName;
        }
    }

    #endregion

    #region Save Files Utility

    private static string GetSaveFileFullName(string filename)
    {
        return SaveDirectoryInfo.FullName + "/" + SaveFilePrefix + filename; // No extension
    }
    public static string GetSaveFilenameFromFileInfo(FileInfo fileInfo)
    {
        var name = fileInfo.Name;
        if (!string.IsNullOrWhiteSpace(fileInfo.Extension))
        {
            name = name.Replace(fileInfo.Extension, "");
        }
        return name.Replace(SaveFilePrefix, "");
    }

    #endregion


    #region Save Access

    public static Save CurrentSave { get; private set; }
    public static T GetCurrentSaveAs<T>() where T : Save
    {
        if (CurrentSave is T t) return t;
        return null;
    }

    #endregion

    #region Save Loading
    public static void LoadSelectedSave()
    {
        Refresh();
        if (LoadSave(SelectedSaveFile, out var save))
        {
            CurrentSave = save;
        }
        else
        {
            CurrentSave = Modifier != null ? Modifier.CreateSave() : new Save();
        }
    }

    public static bool LoadSave(string path, out Save save)
    {
        if (ReadSaveFile(path, out var content))
        {
            try
            {
                save = Modifier != null ? Modifier.ReadSaveFromFile(content) : JsonUtility.FromJson<Save>(content);
#if !UNITY_EDITOR
                if (save != null && save.version != GameStateSettings.GameVersion) return false;
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                save = null;
            }
            return save != null;
        }
        save = null;
        return false;
    }

#endregion

    #region Save Writing
    
    public static void AutoSaveAfterClassicUpdate()
    {
        // Updater.OneShotAfterClassicUpdate += AutoSave;
    }
    
    /// <summary>
    /// Save on the selected save if not null, create new save if null
    /// </summary>
    public static void ManualSave()
    {
        if (!string.IsNullOrWhiteSpace(SelectedSaveFile))
        {
            WriteSaveFile(SelectedSaveFile, GetSaveContent());
        }
        else
        {
            CreateNewSaveFile(GetSaveContent());
        }
    }

    private static string GetSaveContent()
    {
        // CurrentSave.saveType = saveType;
        // CurrentSave.version = GameStateSettings.GameVersion;
        // return Modifier != null ? Modifier.GetSaveContent() : JsonUtility.ToJson(CurrentSave);
        

        
        return JsonUtility.ToJson(CurrentSave);
    }

    #endregion

    #region Save Modifications

    public static void DeleteSelectedSaveFile()
    {
        DeleteSaveFile(SelectedSaveFile);
    }
    public static void CreateNewEmptySaveFile()
    {
        CreateNewSaveFile("");
    }
    public static void RenameSaveFile(FileInfo fileInfo, string newName)
    {
        RenameSaveFile(fileInfo.FullName, newName);
    }

    #endregion
    
    #region Modifier

    public static ISaveManagerModifier Modifier { get; set; }

    #endregion

    #region Editor

#if UNITY_EDITOR
    
    [UnityEditor.MenuItem("Tools/Save/Open save directory")]
    private static void OpenSaveDirectory()
    {
        GetOrCreateSaveDirectory();
        UnityEditor.EditorUtility.RevealInFinder(SaveDirectoryPath);
    }
    
#endif

    #endregion
}
