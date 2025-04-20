using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Messages
{
    public class ConsoleSubscriber : IMessageSubscriber
    {
        public ConsoleSubscriber()
        {
            MessageBus.Instance.OnMessagePublished += DisplayMessage;
        }

        private void DisplayMessage(Message msg)
        {
            if (msg.Type == MessageType.LogOnly)
                return;

            Console.ForegroundColor = msg.Type switch
            {
                MessageType.Highlight=> ConsoleColor.Cyan,
                MessageType.Warning => ConsoleColor.Yellow,
                MessageType.Error => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            var lines = msg.Content.Split(new[] { "\\r\\n", "\\n", "\\r" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (msg.Type != MessageType.Info && msg.Type != MessageType.Highlight)
                    Console.WriteLine($"[{msg.Type}] {line}");
                else
                    Console.WriteLine(line);
            }

            Console.ResetColor();
        }
    }

}
