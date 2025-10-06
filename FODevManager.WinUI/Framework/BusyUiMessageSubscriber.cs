// FODevManager.WinUI/BusyUiMessageSubscriber.cs
using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using FODevManager.Messages;
using FODevManager.Utils;

namespace FODevManager.WinUI
{
    public sealed class BusyUiMessageSubscriber : IDisposable
    {
        private readonly ObservableCollection<string> _lines;
        private readonly DispatcherQueue _dispatcher;
        private readonly IDisposable _subscription;
        private readonly Func<Guid?> _getActiveOperationId;
        private const int MaxMessages = 500;

        public BusyUiMessageSubscriber(ObservableCollection<string> targetLines, DispatcherQueue dispatcher, Func<Guid?> getActiveOperationId)
        {
            _lines = targetLines ?? throw new ArgumentNullException(nameof(targetLines));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _getActiveOperationId = getActiveOperationId ?? throw new ArgumentNullException(nameof(getActiveOperationId));

            _subscription = MessageBus.Subscribe(OnMessageReceived);
        }

        private void OnMessageReceived(Message msg)
        {
            if (msg == null || msg.Type == MessageType.LogOnly) return;

            // The message's operation context at publish time:
            var messageOpId = OperationScope.CurrentId;
            var activeOpId = _getActiveOperationId();

            if (messageOpId == null || activeOpId == null || messageOpId != activeOpId)
                return; // ❌ not for the current action — ignore

            var content = msg.Content ?? string.Empty;
            var split = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            _dispatcher.TryEnqueue(() =>
            {
                foreach (var line in split)
                {
                    var s = line?.Trim();
                    if (string.IsNullOrEmpty(s)) continue;

                    if (_lines.Count >= MaxMessages) _lines.RemoveAt(0);
                    _lines.Add($"{DateTime.Now:HH:mm:ss} {s}");
                }
            });
        }

        public void Dispose() => _subscription.Dispose();
    }
}
