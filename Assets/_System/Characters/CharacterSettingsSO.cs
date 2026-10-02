using UnityEngine;

/// <summary>Marks a ScriptableObject type as a per-character settings domain (Dash today). CharacterSO
/// gains one field of this type per domain as each is introduced — the Settings Browser window discovers
/// them by reflecting over CharacterSO's own fields, never a hardcoded list. No members: a pure marker.</summary>
public abstract class CharacterSettingsSO : ScriptableObject { }
