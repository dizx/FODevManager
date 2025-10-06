using System;

namespace FODevManager.WinUI.Framework
{
    public class BusyHandler
    {
        // isBusy, message, operationId
        public event Action<bool, string?, Guid>? BusyChanged;

        public void Start(string message, Guid operationId)
            => BusyChanged?.Invoke(true, message, operationId);

        public void Stop(Guid operationId)
            => BusyChanged?.Invoke(false, null, operationId);
    }
}
