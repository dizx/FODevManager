using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using FODevManager.Shared.Models;

namespace FODevManager.WinUI.Converters
{
    public class RepoBranchHealthToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not RepoBranchHealth health)
                return new SolidColorBrush(Colors.Gray);

            return health switch
            {
                RepoBranchHealth.Clean => new SolidColorBrush(Colors.LimeGreen),
                RepoBranchHealth.Dirty => new SolidColorBrush(Colors.Gold),
                RepoBranchHealth.NeedsAttention => new SolidColorBrush(Colors.IndianRed),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
