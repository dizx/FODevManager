using Serilog;
using FoDevManager.Messages;

namespace FODevManager.Logging
{
    public class SerilogSubscriber
    {
        public SerilogSubscriber()
        {
            MessageBus.Instance.OnMessagePublished += LogWithSerilog;
        }

        private void LogWithSerilog(Message msg)
        {
            switch (msg.Type)
            {
                case MessageType.LogOnly:
                    Log.Information(msg.Content);
                    break;
                case MessageType.Info:
                    Log.Information(msg.Content);
                    break;
                case MessageType.Warning:
                    Log.Warning(msg.Content);
                    break;
                case MessageType.Error:
                    Log.Error(msg.Content);
                    break;
                case MessageType.Highlight:
                    Log.Information("[HIGHLIGHT] " + msg.Content);
                    break;
                default:
                    Log.Debug(msg.Content);
                    break;
            }
        }
    }
}
