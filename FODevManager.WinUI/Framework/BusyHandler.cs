using System;

namespace FODevManager.WinUI.Framework
{
    public class BusyHandler
    {
        public event Action<bool, string?, Guid>? BusyChanged;

        private volatile bool _isBusy;
        private DateTime _lastBusyEndedUtc = DateTime.MinValue;

        public bool IsBusy => _isBusy;
        public DateTime LastBusyEndedUtc => _lastBusyEndedUtc;

        public void Start(string? message, Guid operationId)
        {
            _isBusy = true;
            BusyChanged?.Invoke(true, message, operationId);
        }

        public void Stop(Guid operationId)
        {
            _isBusy = false;
            _lastBusyEndedUtc = DateTime.UtcNow;
            BusyChanged?.Invoke(false, null, operationId);
        }
    }
}
