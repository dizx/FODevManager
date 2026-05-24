using FODevManager.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace FODevManager.WinUI.Converters
{
    public sealed class ModelTypeToBrushConverter : IValueConverter
    {
        public SolidColorBrush SourceBrush { get; set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        public SolidColorBrush CompiledBrush { get; set; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 86, 156, 214));

        public object Convert(object value, Type targetType, object parameter, string language)
            => value is ModelType mt && mt != ModelType.Source ? CompiledBrush : SourceBrush;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
