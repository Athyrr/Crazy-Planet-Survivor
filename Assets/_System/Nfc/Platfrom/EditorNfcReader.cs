using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NfcPoc
{
    // Mock reader for all non-mobile platforms (Editor + standalone builds).
    // NfcManager.Update() calls Tick() each frame so this file has no UnityEditor dependency.
    public class EditorNfcReader : INfcReader
    {
        public event Action<NfcTagData> OnTagDetected;
        public event Action<string> OnError;
        public bool IsReading { get; private set; }

        private readonly NfcManager _manager;

        public EditorNfcReader(NfcManager manager)
        {
            _manager = manager;
        }

        public void StartReading()
        {
            IsReading = true;
            Debug.Log("[NFC] Editor mock active — press Space to simulate a tag tap.");
        }

        public void StopReading()
        {
            IsReading = false;
        }

        // Called every frame from NfcManager.Update()
        internal void Tick()
        {
            if (!IsReading) return;

            // New Input System: read the keyboard via the Keyboard device.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                EmitMockTag();
        }

        private void EmitMockTag()
        {
            var uid = new byte[4];
            new System.Random().NextBytes(uid);

            var records = new List<NdefRecord>
            {
                new NdefRecord("T", "Mock NFC payload — Editor simulation")
            };

            var tag = new NfcTagData(uid, records);
            _manager.EnqueueOnMainThread(() => OnTagDetected?.Invoke(tag));
        }
    }
}
