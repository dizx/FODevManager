using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Converters
{
    public sealed class BoolToChevronConverter : IValueConverter
    {
        // Collapsed: ChevronRight (E76C). Expanded: ChevronDown (E70D).
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value as bool? == true) ? "\uE70D" : "\uE76C";

        public object ConvertBack(object value, Type targetType, object parameter, string language) => false;
    }

}
