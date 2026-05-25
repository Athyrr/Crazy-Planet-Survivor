using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace NfcPoc
{
    public class NfcManager : MonoBehaviour
    {
        public static NfcManager Instance { get; private set; }

        public static event Action<NfcTagData> OnTagDetected;
        public static event Action<string> OnError;

        public bool IsReading => _reader?.IsReading ?? false;

        private INfcReader _reader;
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateReader();
        }

        void OnDestroy()
        {
            _reader?.StopReading();
            if (Instance == this)
                Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                _reader?.StopReading();
            // Reading is not auto-restarted on resume — user re-taps Start
        }

        void Update()
        {
            lock (_mainThreadQueue)
            {
                while (_mainThreadQueue.Count > 0)
                    _mainThreadQueue.Dequeue()?.Invoke();
            }

            (_reader as EditorNfcReader)?.Tick();
        }

        public void StartReading() => _reader?.StartReading();

        public void StopReading() => _reader?.StopReading();

        internal void EnqueueOnMainThread(Action action)
        {
            lock (_mainThreadQueue)
                _mainThreadQueue.Enqueue(action);
        }

        // Called by iOS native plugin via UnitySendMessage — already on main thread.
        // [Preserve] prevents IL2CPP from stripping this method (no static call site).
        [Preserve]
        private void OnNfcTagDetected(string raw)
        {
            try
            {
                var tag = ParseNativePayload(raw);
                Debug.Log($"[NFC] Tag detected: {tag}");
                OnTagDetected?.Invoke(tag);
            }
            catch (Exception ex)
            {
                OnNfcError($"Failed to parse tag payload: {ex.Message}");
            }
        }

        [Preserve]
        private void OnNfcError(string error)
        {
            Debug.LogError($"[NFC] Error: {error}");
            OnError?.Invoke(error);
        }

        private void CreateReader()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _reader = new IosNfcReader(gameObject.name);
            // iOS callbacks arrive via UnitySendMessage → OnNfcTagDetected / OnNfcError above
#elif UNITY_ANDROID && !UNITY_EDITOR
            _reader = new AndroidNfcReader(this);
            WireReaderEvents();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Desktop: use a PC/SC USB reader (e.g. ACS ACR122U) if one is connected,
            // otherwise fall back to the Space-key mock so testing works without hardware.
            if (PcscNfcReader.IsReaderAvailable())
            {
                Debug.Log("[NFC] PC/SC reader detected — using ACR122U / PC-SC reader.");
                _reader = new PcscNfcReader(this);
            }
            else
            {
                Debug.Log("[NFC] No PC/SC reader detected — using Editor Space-key mock.");
                _reader = new EditorNfcReader(this);
            }
            WireReaderEvents();
#else
            _reader = new EditorNfcReader(this);
            WireReaderEvents();
#endif
        }

        private void WireReaderEvents()
        {
            _reader.OnTagDetected += tag =>
            {
                Debug.Log($"[NFC] Tag detected: {tag}");
                OnTagDetected?.Invoke(tag);
            };
            _reader.OnError += err =>
            {
                Debug.LogError($"[NFC] Error: {err}");
                OnError?.Invoke(err);
            };
        }

        // Wire format from iOS plugin:  "UIDHEX;type1:payload1|type2:payload2"
        private static NfcTagData ParseNativePayload(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return new NfcTagData(Array.Empty<byte>(), null);

            var parts = raw.Split(';');
            byte[] uid = HexToBytes(parts.Length > 0 ? parts[0] : string.Empty);

            var records = new List<NdefRecord>();
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
            {
                foreach (var entry in parts[1].Split('|'))
                {
                    int colon = entry.IndexOf(':');
                    if (colon >= 0)
                        records.Add(new NdefRecord(entry[..colon], entry[(colon + 1)..]));
                    else
                        records.Add(new NdefRecord(string.Empty, entry));
                }
            }

            return new NfcTagData(uid, records);
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                return Array.Empty<byte>();
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (
                    !byte.TryParse(
                        hex.AsSpan(i * 2, 2),
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out bytes[i]
                    )
                )
                    return Array.Empty<byte>();
            }
            return bytes;
        }
    }
}
