using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ArxisStudio.Surface
{
    /// <summary>
    /// Умножает число на коэффициент масштаба.
    /// </summary>
    internal class ScaleDoubleConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] is double d1 && values[1] is double d2)
                return d1 * d2;
            return null;
        }

        /// <inheritdoc />
        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
