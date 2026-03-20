using System;
using UnityEngine;

namespace _System.Save
{
    public class SaveManagerExtender : MonoBehaviour
    {
        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlanetSelected += OnGameStateChanged;

            LoadLastSave();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlanetSelected -= OnGameStateChanged;
        }

        private void LoadLastSave()
        {
            SaveManager.LoadSelectedSave();
        }

        private void OnGameStateChanged(EPlanetID planetID)
        {
            switch (planetID)
            {
                case EPlanetID.Lobby:
                    SaveManager.ManualSave();
                    break;
            }
        }
    }
}