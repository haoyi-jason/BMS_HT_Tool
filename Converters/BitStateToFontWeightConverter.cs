using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BmsHostUi.Converters
{
    public sealed class BitStateToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int raw = ToInt(value);
            int bit = ToInt(parameter);
            bool isOn = (raw & (1 << bit)) != 0;
            return isOn ? FontWeights.Bold : FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static int ToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0;
        }
    }
}
