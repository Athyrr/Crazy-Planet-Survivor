// PC/SC (CCID) NFC reader for desktop USB readers such as the ACS ACR122U.
// Windows-only: communicates with winscard.dll. Used in the Windows Editor and
// Windows standalone builds so real tags can be tested without a phone.
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NfcPoc
{
    public class PcscNfcReader : INfcReader
    {
        public event Action<NfcTagData> OnTagDetected;
        public event Action<string> OnError;
        public bool IsReading { get; private set; }

        private readonly NfcManager _manager;
        private Thread _thread;
        private volatile bool _running;
        private IntPtr _context = IntPtr.Zero;

        public PcscNfcReader(NfcManager manager)
        {
            _manager = manager;
        }

        // ---- Public lifecycle ----

        public void StartReading()
        {
            if (IsReading) return;
            IsReading = true;
            _running = true;
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "PcscNfcReader" };
            _thread.Start();
            Debug.Log("[NFC] PC/SC reader started — place a tag on the reader.");
        }

        public void StopReading()
        {
            _running = false;
            IsReading = false;
            if (_context != IntPtr.Zero)
                SCardCancel(_context); // unblock SCardGetStatusChange
            if (_thread != null && _thread.IsAlive)
                _thread.Join(1500);
            _thread = null;
        }

        /// <summary>True if at least one PC/SC reader is currently connected.</summary>
        public static bool IsReaderAvailable()
        {
            IntPtr ctx = IntPtr.Zero;
            try
            {
                if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out ctx) != SCARD_S_SUCCESS)
                    return false;
                return GetFirstReader(ctx) != null;
            }
            catch (DllNotFoundException) { return false; }
            catch (Exception) { return false; }
            finally
            {
                if (ctx != IntPtr.Zero) SCardReleaseContext(ctx);
            }
        }

        // ---- Background polling ----

        private void PollLoop()
        {
            try
            {
                if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out _context) != SCARD_S_SUCCESS)
                {
                    Report("Failed to establish PC/SC context.");
                    return;
                }

                string reader = GetFirstReader(_context);
                if (reader == null)
                {
                    Report("No PC/SC reader found.");
                    return;
                }

                var state = new SCARD_READERSTATE[1];
                state[0].szReader = reader;
                state[0].dwCurrentState = SCARD_STATE_UNAWARE;

                while (_running)
                {
                    int rc = SCardGetStatusChange(_context, 500, state, 1);
                    if (!_running) break;

                    if (rc == SCARD_E_TIMEOUT)
                        continue;
                    if (rc != SCARD_S_SUCCESS)
                    {
                        Thread.Sleep(250); // reader unplugged / transient; back off
                        continue;
                    }

                    uint evt = state[0].dwEventState;
                    bool nowPresent = (evt & SCARD_STATE_PRESENT) != 0;
                    bool wasPresent = (state[0].dwCurrentState & SCARD_STATE_PRESENT) != 0;

                    if (nowPresent && !wasPresent)
                        HandleCardPresent(reader);

                    // Acknowledge the new state for the next change detection.
                    state[0].dwCurrentState = evt & ~SCARD_STATE_CHANGED;
                }
            }
            catch (Exception ex)
            {
                Report("PC/SC poll loop error: " + ex.Message);
            }
            finally
            {
                if (_context != IntPtr.Zero)
                {
                    SCardReleaseContext(_context);
                    _context = IntPtr.Zero;
                }
            }
        }

        private void HandleCardPresent(string reader)
        {
            IntPtr card = IntPtr.Zero;
            try
            {
                if (SCardConnect(_context, reader, SCARD_SHARE_SHARED,
                        SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1, out card, out uint proto) != SCARD_S_SUCCESS)
                {
                    Report("Could not connect to card.");
                    return;
                }

                byte[] uid = Transmit(card, proto, new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 }, out bool uidOk);
                if (!uidOk || uid == null)
                {
                    Report("Failed to read UID.");
                    return;
                }

                var records = TryReadNdef(card, proto); // best-effort; never throws
                var tag = new NfcTagData(uid, records);
                _manager.EnqueueOnMainThread(() => OnTagDetected?.Invoke(tag));
            }
            catch (Exception ex)
            {
                Report("Error reading tag: " + ex.Message);
            }
            finally
            {
                if (card != IntPtr.Zero) SCardDisconnect(card, SCARD_LEAVE_CARD);
            }
        }

        // ---- APDU helpers ----

        // Sends an APDU, returns the response WITHOUT the SW1/SW2 trailer.
        // ok=true when the status word is 0x9000.
        private static byte[] Transmit(IntPtr card, uint protocol, byte[] apdu, out bool ok)
        {
            ok = false;
            var pci = new SCARD_IO_REQUEST { dwProtocol = protocol, cbPciLength = 8 };
            byte[] recv = new byte[258];
            uint recvLen = (uint)recv.Length;

            int rc = SCardTransmit(card, ref pci, apdu, (uint)apdu.Length, IntPtr.Zero, recv, ref recvLen);
            if (rc != SCARD_S_SUCCESS || recvLen < 2)
                return null;

            byte sw1 = recv[recvLen - 2];
            byte sw2 = recv[recvLen - 1];
            ok = (sw1 == 0x90 && sw2 == 0x00);

            int dataLen = (int)recvLen - 2;
            byte[] data = new byte[dataLen];
            Array.Copy(recv, 0, data, 0, dataLen);
            return data;
        }

        // Best-effort NDEF read for NTAG21x / Mifare Ultralight (4-byte pages, FF B0 reads).
        // Returns an empty list when the tag is not NDEF-readable this way (e.g. Mifare Classic without auth).
        private static List<NdefRecord> TryReadNdef(IntPtr card, uint protocol)
        {
            var records = new List<NdefRecord>();
            try
            {
                var mem = new List<byte>();
                // User memory starts at page 4. Read 16 bytes (4 pages) at a time.
                for (int page = 4; page < 4 + 60; page += 4)
                {
                    byte[] apdu = { 0xFF, 0xB0, 0x00, (byte)page, 0x10 };
                    byte[] chunk = Transmit(card, protocol, apdu, out bool ok);
                    if (!ok || chunk == null || chunk.Length == 0)
                        break;
                    mem.AddRange(chunk);
                    if (Array.IndexOf(chunk, (byte)0xFE) >= 0) // terminator TLV seen
                        break;
                }

                byte[] data = mem.ToArray();
                byte[] ndefMsg = ExtractNdefMessage(data);
                if (ndefMsg != null)
                    ParseNdefMessage(ndefMsg, records);
            }
            catch
            {
                records.Clear(); // degrade to UID-only
            }
            return records;
        }

        // Walk the Type-Length-Value structure to find the NDEF Message TLV (type 0x03).
        private static byte[] ExtractNdefMessage(byte[] data)
        {
            int i = 0;
            while (i < data.Length)
            {
                byte t = data[i];
                if (t == 0x00) { i++; continue; }   // NULL TLV padding
                if (t == 0xFE) return null;          // Terminator before any NDEF TLV

                if (i + 1 >= data.Length) return null;
                int len;
                int valueStart;
                if (data[i + 1] == 0xFF)             // 3-byte length form
                {
                    if (i + 3 >= data.Length) return null;
                    len = (data[i + 2] << 8) | data[i + 3];
                    valueStart = i + 4;
                }
                else
                {
                    len = data[i + 1];
                    valueStart = i + 2;
                }

                if (t == 0x03)                       // NDEF Message TLV
                {
                    if (valueStart + len > data.Length)
                        len = Math.Max(0, data.Length - valueStart);
                    byte[] msg = new byte[len];
                    Array.Copy(data, valueStart, msg, 0, len);
                    return msg;
                }

                i = valueStart + len;                // skip other TLVs
            }
            return null;
        }

        private static void ParseNdefMessage(byte[] msg, List<NdefRecord> records)
        {
            int pos = 0;
            while (pos < msg.Length)
            {
                byte header = msg[pos++];
                bool sr = (header & 0x10) != 0;     // short record
                bool il = (header & 0x08) != 0;     // ID length present
                byte tnf = (byte)(header & 0x07);
                bool me = (header & 0x40) != 0;     // message end

                if (pos >= msg.Length) break;
                int typeLen = msg[pos++];

                int payloadLen;
                if (sr) { payloadLen = msg[pos++]; }
                else
                {
                    payloadLen = (msg[pos] << 24) | (msg[pos + 1] << 16) | (msg[pos + 2] << 8) | msg[pos + 3];
                    pos += 4;
                }

                int idLen = il ? msg[pos++] : 0;

                string type = ReadAscii(msg, pos, typeLen); pos += typeLen;
                pos += idLen; // skip ID

                if (pos + payloadLen > msg.Length) payloadLen = Math.Max(0, msg.Length - pos);
                byte[] payload = new byte[payloadLen];
                Array.Copy(msg, pos, payload, 0, payloadLen);
                pos += payloadLen;

                records.Add(DecodeRecord(tnf, type, payload));

                if (me) break;
            }
        }

        private static NdefRecord DecodeRecord(byte tnf, string type, byte[] payload)
        {
            // Well-known Text record ("T")
            if (tnf == 0x01 && type == "T" && payload.Length >= 1)
            {
                int langLen = payload[0] & 0x3F;
                int start = 1 + langLen;
                if (start <= payload.Length)
                    return new NdefRecord("T", Encoding.UTF8.GetString(payload, start, payload.Length - start));
            }
            // Well-known URI record ("U")
            if (tnf == 0x01 && type == "U" && payload.Length >= 1)
            {
                string prefix = (payload[0] < UriPrefixes.Length) ? UriPrefixes[payload[0]] : "";
                return new NdefRecord("U", prefix + Encoding.UTF8.GetString(payload, 1, payload.Length - 1));
            }
            // Fallback: show printable text if possible, else hex
            string typeLabel = string.IsNullOrEmpty(type) ? "?" : type;
            return new NdefRecord(typeLabel, BytesToReadable(payload));
        }

        private static string ReadAscii(byte[] b, int start, int len)
        {
            if (len <= 0 || start + len > b.Length) return string.Empty;
            return Encoding.ASCII.GetString(b, start, len);
        }

        private static string BytesToReadable(byte[] b)
        {
            if (b == null || b.Length == 0) return string.Empty;
            bool printable = true;
            foreach (var c in b)
                if (c < 0x20 || c > 0x7E) { printable = false; break; }
            return printable ? Encoding.ASCII.GetString(b) : BitConverter.ToString(b).Replace("-", " ");
        }

        private void Report(string message)
        {
            _manager.EnqueueOnMainThread(() => OnError?.Invoke(message));
        }

        private static string GetFirstReader(IntPtr ctx)
        {
            uint len = 0;
            if (SCardListReaders(ctx, null, null, ref len) != SCARD_S_SUCCESS || len == 0)
                return null;

            byte[] buf = new byte[len];
            if (SCardListReaders(ctx, null, buf, ref len) != SCARD_S_SUCCESS)
                return null;

            var readers = new List<string>();
            int start = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] == 0)
                {
                    if (i > start)
                        readers.Add(Encoding.ASCII.GetString(buf, start, i - start));
                    start = i + 1;
                }
            }
            if (readers.Count == 0) return null;

            // Prefer an ACR122-style reader if multiple are present.
            foreach (var r in readers)
                if (r.IndexOf("ACR122", StringComparison.OrdinalIgnoreCase) >= 0)
                    return r;
            return readers[0];
        }

        // ---- winscard.dll P/Invoke ----

        private const uint SCARD_SCOPE_USER = 0;
        private const uint SCARD_SHARE_SHARED = 2;
        private const uint SCARD_PROTOCOL_T0 = 1;
        private const uint SCARD_PROTOCOL_T1 = 2;
        private const uint SCARD_LEAVE_CARD = 0;

        private const uint SCARD_STATE_UNAWARE = 0x0000;
        private const uint SCARD_STATE_CHANGED = 0x0002;
        private const uint SCARD_STATE_EMPTY = 0x0010;
        private const uint SCARD_STATE_PRESENT = 0x0020;

        private const int SCARD_S_SUCCESS = 0;
        private const int SCARD_E_TIMEOUT = unchecked((int)0x8010000A);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct SCARD_READERSTATE
        {
            public string szReader;
            public IntPtr pvUserData;
            public uint dwCurrentState;
            public uint dwEventState;
            public uint cbAtr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
            public byte[] rgbAtr;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCARD_IO_REQUEST
        {
            public uint dwProtocol;
            public uint cbPciLength;
        }

        [DllImport("winscard.dll")]
        private static extern int SCardEstablishContext(uint dwScope, IntPtr r1, IntPtr r2, out IntPtr phContext);

        [DllImport("winscard.dll")]
        private static extern int SCardReleaseContext(IntPtr hContext);

        [DllImport("winscard.dll")]
        private static extern int SCardCancel(IntPtr hContext);

        [DllImport("winscard.dll", EntryPoint = "SCardListReadersA", CharSet = CharSet.Ansi)]
        private static extern int SCardListReaders(IntPtr hContext, byte[] mszGroups, byte[] mszReaders, ref uint pcchReaders);

        [DllImport("winscard.dll", EntryPoint = "SCardGetStatusChangeA", CharSet = CharSet.Ansi)]
        private static extern int SCardGetStatusChange(IntPtr hContext, uint dwTimeout, [In, Out] SCARD_READERSTATE[] rgReaderStates, uint cReaders);

        [DllImport("winscard.dll", EntryPoint = "SCardConnectA", CharSet = CharSet.Ansi)]
        private static extern int SCardConnect(IntPtr hContext, string szReader, uint dwShareMode, uint dwPreferredProtocols, out IntPtr phCard, out uint pdwActiveProtocol);

        [DllImport("winscard.dll")]
        private static extern int SCardDisconnect(IntPtr hCard, uint dwDisposition);

        [DllImport("winscard.dll")]
        private static extern int SCardTransmit(IntPtr hCard, ref SCARD_IO_REQUEST pioSendPci, byte[] pbSendBuffer, uint cbSendLength, IntPtr pioRecvPci, byte[] pbRecvBuffer, ref uint pcbRecvLength);

        // NFC Forum URI prefix abbreviation table (record type "U", first payload byte).
        private static readonly string[] UriPrefixes =
        {
            "", "http://www.", "https://www.", "http://", "https://", "tel:", "mailto:",
            "ftp://anonymous:anonymous@", "ftp://ftp.", "ftps://", "sftp://", "smb://",
            "nfs://", "ftp://", "dav://", "news:", "telnet://", "imap:", "rtsp://",
            "urn:", "pop:", "sip:", "sips:", "tftp:", "btspp://", "btl2cap://",
            "btgoep://", "tcpobex://", "irdaobex://", "file://", "urn:epc:id:",
            "urn:epc:tag:", "urn:epc:pat:", "urn:epc:raw:", "urn:epc:", "urn:nfc:"
        };
    }
}
#endif
