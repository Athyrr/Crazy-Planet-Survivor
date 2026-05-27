using System;
using System.Collections.Generic;

namespace NfcPoc
{
    public class NfcTagData
    {
        public byte[] Uid { get; }
        public string UidHex { get; }
        public List<NdefRecord> NdefRecords { get; }

        public NfcTagData(byte[] uid, List<NdefRecord> ndefRecords)
        {
            Uid = uid ?? Array.Empty<byte>();
            UidHex = uid != null ? BitConverter.ToString(uid).Replace("-", "") : string.Empty;
            NdefRecords = ndefRecords ?? new List<NdefRecord>();
        }

        public override string ToString()
        {
            return $"UID={UidHex} Records={NdefRecords.Count}";
        }
    }

    public class NdefRecord
    {
        public string Type { get; }
        public string Payload { get; }

        public NdefRecord(string type, string payload)
        {
            Type = type ?? string.Empty;
            Payload = payload ?? string.Empty;
        }

        public override string ToString() => $"[{Type}] {Payload}";
    }
}
