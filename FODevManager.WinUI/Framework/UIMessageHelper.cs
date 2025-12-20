using FODevManager.Messages;

public static class UIMessageHelper
{
    public static void LogToUI(string message, MessageType type = MessageType.Info)
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
        {
            switch (type)
            {
                case MessageType.Error:
                    MessageLogger.Error(message);
                    break;
                case MessageType.Warning:
                    MessageLogger.Warning(message);
                    break;
                case MessageType.Highlight:
                    MessageLogger.Highlight(message);
                    break;
                case MessageType.LogOnly:
                    // Optional: skip or route to a silent log if needed
                    break;
                default:
                    MessageLogger.Info(message);
                    break;
            }
        });
    }
}
