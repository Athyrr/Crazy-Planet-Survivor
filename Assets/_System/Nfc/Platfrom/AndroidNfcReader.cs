using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NfcPoc
{
    public class AndroidNfcReader : INfcReader
    {
        public event Action<NfcTagData> OnTagDetected;
        public event Action<string> OnError;
        public bool IsReading { get; private set; }

        private readonly NfcManager _manager;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _nfcAdapter;
        private AndroidJavaObject _activity;
        private NfcReaderCallback _callback;

        // NFC Reader Mode flags: NFC_A=1, NFC_B=2, NFC_F=4, NFC_V=8
        private const int ReaderFlags = 0xF;
#endif

        public AndroidNfcReader(NfcManager manager)
        {
            _manager = manager;
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            using var nfcClass = new AndroidJavaClass("android.nfc.NfcAdapter");
            _nfcAdapter = nfcClass.CallStatic<AndroidJavaObject>("getDefaultAdapter", _activity);

            if (_nfcAdapter == null)
                Debug.LogWarning("[NFC] NFC not available on this device.");
            else
                Debug.Log("[NFC] NFC adapter found");
#endif
        }

        public void StartReading()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nfcAdapter == null || IsReading)
                return;

            _callback = new NfcReaderCallback(
                tag => _manager.EnqueueOnMainThread(() => OnTagDetected?.Invoke(tag)),
                err => _manager.EnqueueOnMainThread(() => OnError?.Invoke(err))
            );

            _nfcAdapter.Call("enableReaderMode", _activity, _callback, ReaderFlags, null);
            IsReading = true;
#endif
        }

        public void StopReading()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsReading)
                return;
            _nfcAdapter?.Call("disableReaderMode", _activity);
            IsReading = false;
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class NfcReaderCallback : AndroidJavaProxy
    {
        private readonly Action<NfcTagData> _onDetected;
        private readonly Action<string> _onError;

        public NfcReaderCallback(Action<NfcTagData> onDetected, Action<string> onError)
            : base("android.nfc.NfcAdapter$ReaderCallback")
        {
            _onDetected = onDetected;
            _onError = onError;
        }

        // Called by Android on a background thread
        void onTagDiscovered(AndroidJavaObject tag)
        {
            try
            {
                byte[] uid = tag.Call<byte[]>("getId");
                var records = ReadNdef(tag);
                _onDetected(new NfcTagData(uid, records));
            }
            catch (Exception ex)
            {
                _onError($"Tag read failed: {ex.Message}");
            }
        }

        private static List<NdefRecord> ReadNdef(AndroidJavaObject tag)
        {
            var list = new List<NdefRecord>();

            using var ndefClass = new AndroidJavaClass("android.nfc.tech.Ndef");
            using var ndefTech = ndefClass.CallStatic<AndroidJavaObject>("get", tag);
            if (ndefTech == null)
                return list;

            try
            {
                ndefTech.Call("connect");

                using var ndefMsg = ndefTech.Call<AndroidJavaObject>("getNdefMessage");
                if (ndefMsg == null)
                    return list;

                var rawRecords = ndefMsg.Call<AndroidJavaObject[]>("getRecords");
                foreach (var rec in rawRecords)
                {
                    using (rec)
                    {
                        byte[] typeBytes = rec.Call<byte[]>("getType");
                        byte[] payloadBytes = rec.Call<byte[]>("getPayload");

                        string type = Encoding.UTF8.GetString(typeBytes);
                        string payload = DecodeNdefPayload(payloadBytes);
                        list.Add(new NdefRecord(type, payload));
                    }
                }
            }
            finally
            {
                try
                {
                    ndefTech.Call("close");
                }
                catch
                { /* ignore close errors */
                }
            }

            return list;
        }

        // NDEF well-known text records have a 1-byte status + language bytes prefix
        private static string DecodeNdefPayload(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            int langLen = bytes[0] & 0x3F;
            int offset = 1 + langLen;
            if (offset < bytes.Length)
                return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            return Encoding.UTF8.GetString(bytes);
        }
    }
#endif
}
