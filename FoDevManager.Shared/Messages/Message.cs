using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoDevManager.Messages
{
    public class Message
    {
        public string Content { get; }
        public MessageType Type { get; }

        public Message(string content, MessageType type)
        {
            Content = content;
            Type = type;
        }
    }

    public enum MessageType
    {
        Info,
        Warning,
        Error,
        Highlight,
        LogOnly
    }

}
