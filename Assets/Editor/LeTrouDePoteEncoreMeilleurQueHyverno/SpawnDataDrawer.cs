using UnityEditor;
using UnityEngine;

// Ce drawer cible spécifiquement la struct SpawnData à l'intérieur de SpawnerAuthoring
[CustomPropertyDrawer(typeof(SpawnerAuthoring.SpawnData))]
public class SpawnDataDrawer : PropertyDrawer
{
    // Définit la hauteur totale de la propriété dans l'inspecteur
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var modeProp = property.FindPropertyRelative("Mode");

        // Hauteur de base : Mode + Prefab + Amount + KillPercentageToAdvance (4 lignes)
        int lines = 4;

        // Si le mode est "Single", on ajoute 2 lignes pour SpawnerPrefab et SpawnDelay
        if (IsMode(modeProp, "Single"))
        {
            lines += 2;
        }
        else if (IsMode(modeProp, "AroundPlayer"))
        {
            lines += 2;
        }

        // Calcul final avec l'espacement standard entre les lignes
        return lines * EditorGUIUtility.singleLineHeight + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    // Dessine l'interface
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Récupération des sous-propriétés
        var modeProp = property.FindPropertyRelative("Mode");
        var prefabProp = property.FindPropertyRelative("Prefab");
        var amountProp = property.FindPropertyRelative("Amount");
        var spawnerPrefabProp = property.FindPropertyRelative("SpawnerPrefab");
        var spawnDelayProp = property.FindPropertyRelative("SpawnDelay");
        var minRangeProp = property.FindPropertyRelative("MinSpawnRange");
        var maxRangeProp = property.FindPropertyRelative("MaxSpawnRange");
        var killPercentageProp = property.FindPropertyRelative("KillPercentageToAdvance");

        // Rectangle pour la première ligne
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        // 1. Dessiner le Mode
        EditorGUI.PropertyField(rect, modeProp);
        rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // 2. Dessiner Prefab (Toujours visible)
        EditorGUI.PropertyField(rect, prefabProp);
        rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // 3. Dessiner Amount (Toujours visible)
        EditorGUI.PropertyField(rect, amountProp);
        rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        
        // 4. Dessiner KillPercentageToAdvance (Toujours visible)
        EditorGUI.PropertyField(rect, killPercentageProp);
        rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // 5. Dessiner les champs conditionnels si Mode == Single
        if (IsMode(modeProp, "Single"))
        {
            EditorGUI.PropertyField(rect, spawnerPrefabProp);
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.PropertyField(rect, spawnDelayProp);
        }
        else if (IsMode(modeProp, "AroundPlayer"))
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, minRangeProp);
            if (EditorGUI.EndChangeCheck())
            {
                if (minRangeProp.floatValue > maxRangeProp.floatValue)
                {
                    maxRangeProp.floatValue = minRangeProp.floatValue;
                }
            }

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, maxRangeProp);
            if (EditorGUI.EndChangeCheck())
            {
                if (maxRangeProp.floatValue < minRangeProp.floatValue)
                {
                    minRangeProp.floatValue = maxRangeProp.floatValue;
                }
            }
        }

        EditorGUI.EndProperty();
    }

    // Helper pour vérifier la valeur de l'enum de manière sécurisée
    private bool IsMode(SerializedProperty modeProp, string modeName)
    {
        // Vérifie si le nom de l'enum sélectionné correspond
        if (modeProp.enumValueIndex >= 0 && modeProp.enumValueIndex < modeProp.enumNames.Length)
        {
            return modeProp.enumNames[modeProp.enumValueIndex] == modeName;
        }
        return false;
    }
}