using System;
using System.Collections.Generic;

namespace FODevManager.Messages;

public class MessageBus
{
    public event Action<Message>? OnMessagePublished;

    private static readonly Lazy<MessageBus> _instance = new(() => new MessageBus());

    public static MessageBus Instance => _instance.Value;

    public void Publish(Message message)
    {
        OnMessagePublished?.Invoke(message);
    }
}
