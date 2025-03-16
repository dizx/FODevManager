using Microsoft.UI.Xaml.Data;
using System;

namespace FODevManager.WinUI.Converters
{
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isDeployed)
                return isDeployed ? "✅ Deployed" : "❌ Not Deployed";
            return "❓ Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
