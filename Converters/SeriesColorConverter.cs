using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BmsHostUi.Converters
{
    public sealed class SeriesColorConverter : IValueConverter
    {
        private static readonly Brush[] Palette =
        {
            Brushes.SteelBlue,
            Brushes.IndianRed,
            Brushes.SeaGreen,
            Brushes.DarkOrange,
            Brushes.MediumVioletRed,
            Brushes.Teal,
            Brushes.SaddleBrown,
            Brushes.DimGray,
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index;
            if (value is int i)
            {
                index = i;
            }
            else if (!int.TryParse(value?.ToString(), out index))
            {
                index = 0;
            }

            return Palette[Math.Abs(index) % Palette.Length];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
