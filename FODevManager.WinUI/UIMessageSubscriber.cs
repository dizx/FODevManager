using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using FODevManager.Messages;

namespace FODevManager.WinUI
{
    public class UIMessageSubscriber : IMessageSubscriber
    {
        public ObservableCollection<Message> RecentMessages { get; } = new();

        private const int MaxMessages = 8;

        public UIMessageSubscriber()
        {
            MessageBus.Instance.OnMessagePublished += OnMessageReceived;
        }

        private void OnMessageReceived(Message msg)
        {
            if (msg.Type == MessageType.LogOnly)
                return;

            var lines = msg.Content.Split(new[] { "\\r\\n ", "\\n ", "\\r " }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmedMsg = new Message(line, msg.Type);
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
                {
                    if (RecentMessages.Count >= MaxMessages)
                        RecentMessages.RemoveAt(0);

                    RecentMessages.Add(trimmedMsg);
                });
            }
        }

        
    }
}
