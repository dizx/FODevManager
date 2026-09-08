using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Messages
{
    public class ConsoleSubscriber : IMessageSubscriber, IDisposable
    {
        private readonly IDisposable _busSub;

        public ConsoleSubscriber()
        {
            _busSub = MessageBus.Subscribe(DisplayMessage);
        }

        private void DisplayMessage(Message msg)
        {
            if (msg.Type == MessageType.LogOnly)
                return;

            var output = msg.Type == MessageType.Error ? Console.Error : Console.Out;
            var useColor = !Console.IsOutputRedirected && !Console.IsErrorRedirected;
            if (useColor)
                Console.ForegroundColor = msg.Type switch
                {
                    MessageType.Highlight => ConsoleColor.Cyan,
                    MessageType.Warning => ConsoleColor.Yellow,
                    MessageType.Error => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };

            var lines = msg.Content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            try
            {
                foreach (var line in lines)
                {
                    if (msg.Type != MessageType.Info && msg.Type != MessageType.Highlight)
                        output.WriteLine($"[{msg.Type}] {line}");
                    else
                        output.WriteLine(line);
                }
            }
            finally
            {
                if (useColor)
                    Console.ResetColor();
            }
        }
        public void Dispose()
        {
            _busSub.Dispose(); // unsubscribe
        }
    }
}
