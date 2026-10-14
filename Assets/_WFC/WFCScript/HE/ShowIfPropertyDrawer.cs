#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace WFCContent.HE
{
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public sealed class ShowIfPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsVisible(property))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return IsVisible(property)
                ? EditorGUI.GetPropertyHeight(property, label, true)
                : -EditorGUIUtility.standardVerticalSpacing;
        }

        private bool IsVisible(SerializedProperty property)
        {
            var condition = (ShowIfAttribute)attribute;
            if (string.IsNullOrEmpty(condition.ConditionMember))
                return true;

            // Read pending inspector edits before falling back to reflection.
            string path = property.propertyPath;
            int separator = path.LastIndexOf('.');
            string conditionPath = separator < 0
                ? condition.ConditionMember
                : path.Substring(0, separator + 1) + condition.ConditionMember;
            var sibling = property.serializedObject.FindProperty(conditionPath);
            if (sibling != null && sibling.propertyType == SerializedPropertyType.Boolean)
                return sibling.hasMultipleDifferentValues || sibling.boolValue == condition.ExpectedValue;

            // Hide only when every selected target fails the condition.
            foreach (var target in property.serializedObject.targetObjects)
            {
                try
                {
                    object owner = GetOwner(target, path);
                    if (!TryReadMember(owner, condition.ConditionMember, out object value)
                        || !(value is bool enabled) || enabled == condition.ExpectedValue)
                        return true;
                }
                catch (Exception)
                {
                    // Invalid conditions must not make fields inaccessible.
                    return true;
                }
            }
            return false;
        }

        private static object GetOwner(object root, string propertyPath)
        {
            string[] parts = propertyPath.Replace(".Array.data[", "[").Split('.');
            object owner = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                int bracket = part.IndexOf('[');
                string member = bracket < 0 ? part : part.Substring(0, bracket);
                if (!TryReadMember(owner, member, out owner))
                    return null;
                if (bracket >= 0)
                {
                    int index = int.Parse(part.Substring(bracket + 1, part.Length - bracket - 2));
                    if (!(owner is IList list) || index < 0 || index >= list.Count)
                        return null;
                    owner = list[index];
                }
            }
            return owner;
        }

        private static bool TryReadMember(object owner, string name, out object value)
        {
            value = null;
            if (owner == null)
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    value = field.GetValue(owner);
                    return true;
                }
                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null)
                {
                    value = property.GetValue(owner);
                    return true;
                }
                var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(bool) && !method.ContainsGenericParameters)
                {
                    value = method.Invoke(owner, null);
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
