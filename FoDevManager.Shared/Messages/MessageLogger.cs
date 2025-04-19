using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoDevManager.Messages
{
    public static class MessageLogger
    {
        
        public static void Info(string message) => Write(message);
        public static void Warning(string message) => Write(message, MessageType.Warning);
        public static void Error(string message) => Write(message, MessageType.Error);
        public static void Highlight(string message) => Write(message, MessageType.Highlight);
        public static void LogOnly(string message) => Write(message, MessageType.LogOnly);

        private static void Write(string message, MessageType type = MessageType.Info)
        {
            var msg = new Message(message, type);
            MessageBus.Instance.Publish(msg);
        }
    }

}
