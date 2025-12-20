using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;   
using FODevManager.Messages;
using Microsoft.UI;

namespace FODevManager.WinUI.Converters
{
    public class MessageTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                MessageType.Error => new SolidColorBrush(Colors.DarkRed),
                MessageType.Warning => new SolidColorBrush(Colors.Goldenrod),
                MessageType.Highlight => new SolidColorBrush(Colors.Turquoise),
                _ => new SolidColorBrush(Colors.White)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
