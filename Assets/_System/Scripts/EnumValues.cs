using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class EnumValues<T, U> : IEnumerable<KeyValuePair<T, U>> where T : Enum
{
    #region Members

    [SerializeField] private U[] m_enumValues;

    #endregion

    #region Properties

    public U this[T enumValue]
    {
        get => Get(enumValue);
        set => Set(enumValue, value);
    }

    #endregion

    #region Accessors

    private U Get(T enumValue)
    {
        // if (!m_enumValues.IsValid()) return default;

        int index = Convert.ToInt32(enumValue);
        if (index < m_enumValues.Length)
            return m_enumValues[index];
        else
        {
            Debug.LogError("Enum Values for type " + typeof(U) + " not complete, might need to serialize it in the inspector");
            return default;
        }
    }
    private void Set(T enumValue, U value)
    {
        // if (!m_enumValues.IsValid()) return;

        int index = Convert.ToInt32(enumValue);
        if (index >= m_enumValues.Length)
        {
            var newEnumValues = new U[index + 1];
            for (int i = 0; i < m_enumValues.Length; i++)
            {
                newEnumValues[i] = m_enumValues[i];
            }
            m_enumValues = newEnumValues;
        }
        m_enumValues[index] = value;
    }

    #endregion

    #region Enumerators

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    public IEnumerator<KeyValuePair<T, U>> GetEnumerator()
    {
        for (int i = 0; i < m_enumValues.Length; i++)
        {
            if (Enum.IsDefined(typeof(T), i))
            {
                yield return new KeyValuePair<T, U>((T)Enum.ToObject(typeof(T), i), m_enumValues[i]);
            }
        }
    }

    public IEnumerator<U> GetSimpleEnumerator()
    {
        foreach (var u in m_enumValues)
        {
            yield return u;
        }
    }

    #endregion
}

#region Drawer

#if UNITY_EDITOR

[CustomPropertyDrawer(typeof(EnumValues<,>))]
public class EnumValuesDrawer : PropertyDrawer
{
    #region GUI

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var enumType = GetEnumType();
        var enumValues = Enum.GetValues(enumType);

        var p_enumValues = property.FindPropertyRelative("m_enumValues");

        // Get the array size
        int arraySize = 0;
        int intValue;
        for (int i = 0; i < enumValues.Length; i++)
        {
            intValue = (int)enumValues.GetValue(i);
            if (intValue >= arraySize) arraySize = intValue + 1;
        }
        p_enumValues.arraySize = arraySize;

        // --- GUI ---
        EditorGUI.BeginProperty(position, label, property);

        Rect rect = new(position.x, position.y, position.width, 20f);

        // Click
        if (Event.current.type == EventType.MouseDown
            && Event.current.button == 0
            && rect.Contains(Event.current.mousePosition))
        {
            property.isExpanded = !property.isExpanded;
            Event.current.Use();
        }
        // Repaint
        if (Event.current.type == EventType.Repaint)
        {
            EditorStyles.foldout.Draw(rect, label, 0, property.isExpanded);
        }

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            rect.y += 25f;

            int index;
            foreach (var v in enumValues)
            {
                index = (int)v;
                if (index >= 0 && index < arraySize)
                {
                    EditorGUI.PropertyField(rect, p_enumValues.GetArrayElementAtIndex(index), new GUIContent(v.ToString()), true);
                    rect.y += Mathf.Max(20f, EditorGUI.GetPropertyHeight(p_enumValues.GetArrayElementAtIndex(index)));
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    #endregion

    #region GUI Height

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (property.isExpanded)
        {
            float height = 25f;

            var p_enumValues = property.FindPropertyRelative("m_enumValues");
            int index;
            foreach (var v in Enum.GetValues(GetEnumType()))
            {
                index = (int)v;
                if (index >= 0 && index < p_enumValues.arraySize)
                {
                    height += Mathf.Max(20f, EditorGUI.GetPropertyHeight(p_enumValues.GetArrayElementAtIndex(index)));
                }
            }

            return height;
        }
        return 20f;
    }

    #endregion

    #region Utility

    private Type GetEnumType()
    {
        var enumType = fieldInfo.FieldType;
        while (enumType.IsGenericType) enumType = enumType.GetGenericArguments()[0];
        return enumType;
    }

    #endregion
}

#endif

#endregion