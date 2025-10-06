using System;
using System.Collections.Generic;

namespace FODevManager.Messages;

public sealed class MessageBus
{
    private static readonly Lazy<MessageBus> _instance = new(() => new MessageBus());
    public static MessageBus Instance => _instance.Value;

    // keep the raw event private
    private event Action<Message>? MessagePublished;

    public void Publish(Message message)
    {
        var handlers = MessagePublished;
        if (handlers == null) return;

        // enumerate safely so one bad handler doesn't kill the chain
        foreach (Action<Message> h in handlers.GetInvocationList())
        {
            try { h.Invoke(message); }
            catch { /* swallow – don’t let logging re-enter logging */ }
        }
    }

    // ✅ callers get a disposable they can keep and later dispose
    public static IDisposable Subscribe(Action<Message> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        Instance.MessagePublished += handler;
        return new Unsubscriber(handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Action<Message>? _handler;
        public Unsubscriber(Action<Message> handler) => _handler = handler;
        public void Dispose()
        {
            if (_handler != null)
            {
                Instance.MessagePublished -= _handler;
                _handler = null;
            }
        }
    }
}



