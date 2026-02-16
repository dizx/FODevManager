using FODevManager.Messages;
using Microsoft.UI.Dispatching;
using System;

namespace EasyGit.WinUI.Framework
{
    internal sealed class StatusMessageSubscriber : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly Action<string> _applyStatus;
        private readonly IDisposable _subscription;

        public StatusMessageSubscriber(DispatcherQueue dispatcher, Action<string> applyStatus)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _applyStatus = applyStatus ?? throw new ArgumentNullException(nameof(applyStatus));
            _subscription = MessageBus.Subscribe(OnMessageReceived);
        }

        private void OnMessageReceived(Message message)
        {
            if (message == null || message.Type == MessageType.LogOnly)
                return;

            var content = message.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return;

            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var text = string.Empty;
            foreach (var line in lines)
            {
                var trimmed = line?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    text = trimmed;
            }

            if (string.IsNullOrWhiteSpace(text))
                return;

            _dispatcher.TryEnqueue(() => _applyStatus(text));
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
