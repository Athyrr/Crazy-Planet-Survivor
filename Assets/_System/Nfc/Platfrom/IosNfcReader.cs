using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NfcPoc
{
    // iOS implementation delegates all work to NfcPlugin.mm via DllImport.
    // Tag callbacks arrive on the main thread through UnitySendMessage → NfcManager,
    // so NfcManager.OnNfcTagDetected / OnNfcError handle event raising directly.
    // This class only wires the Start/Stop calls and surfaces IsReading.
    public class IosNfcReader : INfcReader
    {
        public event Action<NfcTagData> OnTagDetected { add { } remove { } }
        public event Action<string> OnError { add { } remove { } }

        public bool IsReading { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void NFC_Init(string gameObjectName);
        [DllImport("__Internal")] private static extern void NFC_StartReading();
        [DllImport("__Internal")] private static extern void NFC_StopReading();
        [DllImport("__Internal")] private static extern bool NFC_IsAvailable();
#endif

        public IosNfcReader(string nfcManagerGameObjectName)
        {
#if UNITY_IOS && !UNITY_EDITOR
            NFC_Init(nfcManagerGameObjectName);
            if (!NFC_IsAvailable())
                Debug.LogWarning("[NFC] CoreNFC not available on this device.");
#endif
        }

        public void StartReading()
        {
#if UNITY_IOS && !UNITY_EDITOR
            NFC_StartReading();
            IsReading = true;
#endif
        }

        public void StopReading()
        {
#if UNITY_IOS && !UNITY_EDITOR
            NFC_StopReading();
            IsReading = false;
#endif
        }
    }
}
