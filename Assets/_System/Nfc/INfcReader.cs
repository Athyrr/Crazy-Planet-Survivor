using System;

namespace NfcPoc
{
    public interface INfcReader
    {
        event Action<NfcTagData> OnTagDetected;
        event Action<string> OnError;

        bool IsReading { get; }

        void StartReading();
        void StopReading();
    }
}
