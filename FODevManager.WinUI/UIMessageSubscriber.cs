using System;
using System.Collections.Generic;
using FODevManager.Messages;

namespace FODevManager.WinUI
{
    public class UIMessageSubscriber
    {
        public event Action<string, MessageType> MessageReceived;

        private readonly Queue<Message> _recentMessages = new();
        private const int MaxMessages = 10;

        public UIMessageSubscriber()
        {
            MessageBus.Instance.OnMessagePublished += DisplayMessage;
        }

        private void DisplayMessage(Message msg)
        {
            if (msg.Type == MessageType.LogOnly)
                return;

            if (_recentMessages.Count >= MaxMessages)
                _recentMessages.Dequeue();

            _recentMessages.Enqueue(msg);

            // Flatten message to one-line for status bar
            var firstLine = msg.Content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)[0];
            MessageReceived?.Invoke(firstLine, msg.Type);
        }

        public IEnumerable<Message> GetRecentMessages() => _recentMessages;
    }
}
