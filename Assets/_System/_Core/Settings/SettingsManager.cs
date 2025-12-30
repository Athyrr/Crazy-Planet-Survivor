using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Vous pouvez changer le namespace si nécessaire
namespace RuntimeSettings
{
    [DefaultExecutionOrder(-100)] // S'exécute très tôt
    public class SettingsManager : MonoBehaviour
    {
        #region Singleton
        public static SettingsManager Instance { get; private set; }
        #endregion

        #region Configuration Fields
        [Header("Config")]
        [Tooltip("Nom du fichier de configuration par défaut.")]
        public string defaultFileName = "settings.ini";
        
        [Tooltip("Intervalle de vérification des modifications du fichier (en secondes).")]
        public float fileWatchInterval = 2.0f;
        
        [Tooltip("Afficher les logs de debug ?")]
        public bool showDebugLogs = true;
        #endregion

        #region Events & Data
        
        // Structure pour l'événement de changement
        public readonly struct SettingChanged
        {
            public readonly string FullKey;   // ex: "Gameplay.speed"
            public readonly string Section;   // ex: "Gameplay"
            public readonly string Property;  // ex: "speed"
            public readonly object OldValue;
            public readonly object NewValue;

            public SettingChanged(string fullKey, string section, string property, object oldValue, object newValue)
            {
                FullKey = fullKey;
                Section = section;
                Property = property;
                OldValue = oldValue;
                NewValue = newValue;
            }
        }

        // L'événement auquel s'abonner
        public static event Action<SettingChanged> SettingChangedEvent;

        // Stockage interne des données chargées (Section.Key => Value)
        private Dictionary<string, string> _loadedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Liste des objets enregistrés à peupler (Section => Objet C#)
        private Dictionary<string, List<object>> _registeredObjects = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        // Variables pour le file watcher
        private string _currentFilePath;
        private long _lastWriteTime;
        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            // 1. Initialiser le chemin et les arguments
            InitializePathAndArgs();
            
            // 2. Premier chargement
            LoadSettingsFromFile();
            
            // 3. Lancer la surveillance du fichier
            if (Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                InvokeRepeating(nameof(CheckFileModification), fileWatchInterval, fileWatchInterval);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Enregistre un objet (ScriptableObject ou MonoBehaviour) pour qu'il soit rempli par les données d'une section INI.
        /// </summary>
        /// <param name="sectionName">Le nom de la section dans le fichier INI (ex: "Gameplay")</param>
        /// <param name="target">L'objet contenant les variables à modifier</param>
        public void RegisterSettingsObject(string sectionName, object target)
        {
            if (target == null) return;

            if (!_registeredObjects.ContainsKey(sectionName))
            {
                _registeredObjects[sectionName] = new List<object>();
            }

            if (!_registeredObjects[sectionName].Contains(target))
            {
                _registeredObjects[sectionName].Add(target);
                // Appliquer immédiatement les settings déjà chargés sur cet objet
                ApplySettingsToTarget(sectionName, target);
            }
        }

        /// <summary>
        /// Récupère une valeur brute (string) depuis les settings chargés.
        /// </summary>
        public string GetRawValue(string section, string key)
        {
            string fullKey = $"{section}.{key}";
            if (_loadedSettings.TryGetValue(fullKey, out string val))
            {
                return val;
            }
            return null;
        }

        #endregion

        #region Core Logic

        private void InitializePathAndArgs()
        {
            // Chemin par défaut
            _currentFilePath = Path.Combine(Application.dataPath, defaultFileName);

            // Gestion des arguments de lancement (-c config.ini ou key=value)
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                // Custom config file: -c path/to/config.ini
                if (args[i] == "-c" && i + 1 < args.Length)
                {
                    string pathArg = args[i + 1];
                    if (!pathArg.Contains("="))
                    {
                        if (File.Exists(pathArg)) _currentFilePath = pathArg;
                        else _currentFilePath = Path.Combine(Application.dataPath, pathArg);
                    }
                }
            }
            
            if (showDebugLogs) Debug.Log($"[SettingsManager] Using config file: {_currentFilePath}");
        }

        private void LoadSettingsFromFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                if(showDebugLogs) Debug.LogWarning($"[SettingsManager] File not found at {_currentFilePath}");
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(_currentFilePath);
                var newSettings = ParseIniLines(lines); // Utilise votre logique de parsing
                
                // Mettre à jour les settings
                ApplyNewSettings(newSettings);
                
                _lastWriteTime = File.GetLastWriteTime(_currentFilePath).Ticks;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SettingsManager] Error loading file: {ex.Message}");
            }
        }

        private void CheckFileModification()
        {
            if (!File.Exists(_currentFilePath)) return;

            long currentTicks = File.GetLastWriteTime(_currentFilePath).Ticks;
            if (currentTicks != _lastWriteTime)
            {
                if(showDebugLogs) Debug.Log("[SettingsManager] File modified. Reloading...");
                LoadSettingsFromFile();
            }
        }

        private void ApplyNewSettings(Dictionary<string, string> newSettings)
        {
            // On parcourt toutes les nouvelles valeurs
            foreach (var kvp in newSettings)
            {
                string fullKey = kvp.Key; // "Section.Property"
                string newValueStr = kvp.Value;

                // Mettre à jour le dictionnaire interne
                _loadedSettings[fullKey] = newValueStr;

                // Décomposer la clé
                int dotIndex = fullKey.IndexOf('.');
                if (dotIndex == -1) continue; // Pas de section, on ignore

                string section = fullKey.Substring(0, dotIndex);
                string property = fullKey.Substring(dotIndex + 1);

                // Si on a des objets enregistrés pour cette section, on applique
                if (_registeredObjects.TryGetValue(section, out var targets))
                {
                    foreach (var target in targets)
                    {
                        ApplySingleSetting(target, section, property, newValueStr, fullKey);
                    }
                }
            }
        }

        private void ApplySettingsToTarget(string section, object target)
        {
            // Applique toutes les valeurs connues pour cette section sur la cible
            foreach (var kvp in _loadedSettings)
            {
                string fullKey = kvp.Key;
                if (fullKey.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase))
                {
                    string property = fullKey.Substring(section.Length + 1);
                    ApplySingleSetting(target, section, property, kvp.Value, fullKey);
                }
            }
        }

        private void ApplySingleSetting(object target, string section, string propertyName, string valueStr, string fullKey)
        {
            if (target == null) return;
            Type t = target.GetType();

            // Chercher Field ou Property (Case insensitive)
            MemberInfo member = GetMemberInfo(t, propertyName);

            if (member == null) return;

            try
            {
                object oldValue = GetValue(member, target);
                Type valueType = GetMemberType(member);
                object newValue = ConvertValue(valueStr, valueType);

                // Si la valeur a changé, on applique et on notifie
                if (!Equals(oldValue, newValue)) 
                {
                    SetValue(member, target, newValue);
                    
                    if(showDebugLogs) Debug.Log($"[Settings] Updated {section}.{propertyName} = {newValue}");
                    
                    SettingChangedEvent?.Invoke(new SettingChanged(fullKey, section, propertyName, oldValue, newValue));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SettingsManager] Failed to set {propertyName} on {t.Name}: {e.Message}");
            }
        }

        #endregion

        #region Reflection Helpers

        // Trouve un champ ou une propriété sans tenir compte de la casse
        private MemberInfo GetMemberInfo(Type type, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            
            FieldInfo f = type.GetField(name, flags);
            if (f != null) return f;

            PropertyInfo p = type.GetProperty(name, flags);
            if (p != null) return p;

            return null;
        }

        private Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo f) return f.FieldType;
            if (member is PropertyInfo p) return p.PropertyType;
            return typeof(object);
        }

        private object GetValue(MemberInfo member, object target)
        {
            if (member is FieldInfo f) return f.GetValue(target);
            if (member is PropertyInfo p) return p.GetValue(target);
            return null;
        }

        private void SetValue(MemberInfo member, object target, object value)
        {
            if (member is FieldInfo f) f.SetValue(target, value);
            else if (member is PropertyInfo p) p.SetValue(target, value);
        }

        /// <summary>
        /// Convertit un string en type cible (int, float, bool, List, Array...)
        /// </summary>
        private static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (targetType.IsEnum) return Enum.Parse(targetType, value, true);

            // Gestion des Listes et Tableaux (separateur virgule)
            if (typeof(IList).IsAssignableFrom(targetType))
            {
                Type itemType = targetType.IsArray 
                    ? targetType.GetElementType() 
                    : targetType.GetGenericArguments()[0];

                var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Créer une liste générique
                var listType = typeof(List<>).MakeGenericType(itemType);
                var list = (IList)Activator.CreateInstance(listType);

                foreach (var part in parts)
                {
                    list.Add(ConvertValue(part.Trim(), itemType));
                }

                // Si c'est un tableau array[]
                if (targetType.IsArray)
                {
                    var array = Array.CreateInstance(itemType, list.Count);
                    list.CopyTo(array, 0);
                    return array;
                }

                return list;
            }

            // Gestion Nullable
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                return string.IsNullOrEmpty(value) ? null : ConvertValue(value, underlyingType);
            }

            return Convert.ChangeType(value, targetType);
        }

        #endregion

        #region INI Parser (From User)
        
        // C'est exactement votre code fourni, intégré ici pour ne pas avoir 2 fichiers
        private static Dictionary<string, string> ParseIniLines(string[] lines)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string currentSection = "";

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
                if (trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#")) continue;

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
                    continue;
                }

                if (trimmedLine.Contains("="))
                {
                    string[] parts = trimmedLine.Split(new char[] { '=' }, 2);
                    if (parts.Length < 2) continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    int commentIndex = value.IndexOf(';');
                    if (commentIndex > 0)
                    {
                        value = value.Substring(0, commentIndex).Trim();
                    }

                    string fullKey = string.IsNullOrEmpty(currentSection) 
                        ? key 
                        : $"{currentSection}.{key}";

                    result[fullKey] = value;
                }
            }
            return result;
        }
        #endregion
    }
}