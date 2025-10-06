using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using FODevManager.Messages;

namespace FODevManager.WinUI.Framework
{
    public class UIMessageSubscriber : IMessageSubscriber, IDisposable
    {
        public ObservableCollection<Message> RecentMessages { get; } = new();

        private const int MaxMessages = 8;
        private readonly DispatcherQueue _dispatcher;
        private readonly IDisposable _subscription;

        public UIMessageSubscriber(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            _subscription = MessageBus.Subscribe(OnMessageReceived);
        }

        private void OnMessageReceived(Message msg)
        {
            if (msg == null || msg.Type == MessageType.LogOnly) return;

            var content = msg.Content ?? string.Empty;
            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            _dispatcher.TryEnqueue(() =>
            {
                foreach (var line in lines)
                {
                    var text = line?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    if (RecentMessages.Count >= MaxMessages)
                        RecentMessages.RemoveAt(0);

                    RecentMessages.Add(new Message(text, msg.Type));
                }
            });
        }

        public void Dispose() => _subscription.Dispose();
    }
}
