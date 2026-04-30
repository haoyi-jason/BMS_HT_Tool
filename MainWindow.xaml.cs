using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BmsHostUi.Models;
using BmsHostUi.Services;
using BmsHostUi.ViewModels;

namespace BmsHostUi
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _plotTimer;
        private bool _isZhTw;
        private static readonly Brush[] SeriesPalette =
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

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel(new ModbusRtuService(), new CsvLoggerService());
            _isZhTw = false;

            _plotTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _plotTimer.Tick += (_, __) => RenderPlot();
            _plotTimer.Start();

            SizeChanged += (_, __) => RenderPlot();
            Loaded += (_, __) => RenderPlot();
        }

        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        private void RegisterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionsAndRender();
        }

        private void UpdateSelectionsAndRender()
        {
            var vm = ViewModel;
            if (vm == null)
            {
                return;
            }

            var left = LeftRegisterList.SelectedItems.Cast<RegisterSelectionItem>();
            var right = RightRegisterList.SelectedItems.Cast<RegisterSelectionItem>();
            vm.SetSelectedRegisters(left, right);
            RenderPlot();
        }

        private void RenderPlot()
        {
            var vm = ViewModel;
            if (vm == null || PlotCanvas == null)
            {
                return;
            }

            double width = PlotCanvas.ActualWidth;
            double height = PlotCanvas.ActualHeight;
            if (width < 40 || height < 40)
            {
                return;
            }

            PlotCanvas.Children.Clear();

            const double leftPad = 46;
            const double rightPad = 46;
            const double topPad = 14;
            const double bottomPad = 24;
            double plotWidth = Math.Max(10, width - leftPad - rightPad);
            double plotHeight = Math.Max(10, height - topPad - bottomPad);

            DrawAxes(leftPad, rightPad, topPad, bottomPad, width, height);

            var snapshots = vm.GetSelectedSeriesSnapshots(vm.LatestCount);
            if (snapshots.Count == 0)
            {
                DrawHintText(GetUiString("HintSelectRegs", "Select Y1/Y2 registers to plot"));
                return;
            }

            var leftSeries = snapshots.Where(s => s.IsLeftAxis && s.Values != null && s.Values.Length >= 2).ToArray();
            var rightSeries = snapshots.Where(s => !s.IsLeftAxis && s.Values != null && s.Values.Length >= 2).ToArray();

            double leftMin;
            double leftMax;
            ComputeBounds(leftSeries, out leftMin, out leftMax);
            ResolveAxisBounds(vm.IsY1AutoRange, vm.Y1Min, vm.Y1Max, leftSeries.Length > 0, ref leftMin, ref leftMax);

            double rightMin;
            double rightMax;
            ComputeBounds(rightSeries, out rightMin, out rightMax);
            ResolveAxisBounds(vm.IsY2AutoRange, vm.Y2Min, vm.Y2Max, rightSeries.Length > 0, ref rightMin, ref rightMax);

            foreach (var series in leftSeries)
            {
                DrawSeries(series, GetSeriesBrush(series.Index), true, leftMin, leftMax, leftPad, topPad, plotWidth, plotHeight);
            }

            foreach (var series in rightSeries)
            {
                DrawSeries(series, GetSeriesBrush(series.Index), false, rightMin, rightMax, leftPad, topPad, plotWidth, plotHeight);
            }

            DrawAxisLabel(leftMin, leftMax, true, leftPad, topPad, plotHeight, width);
            DrawAxisLabel(rightMin, rightMax, false, leftPad, topPad, plotHeight, width);
            DrawLegend(snapshots);
        }

        private void DrawAxes(double leftPad, double rightPad, double topPad, double bottomPad, double width, double height)
        {
            var axisBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));

            PlotCanvas.Children.Add(new Line
            {
                X1 = leftPad,
                Y1 = topPad,
                X2 = leftPad,
                Y2 = height - bottomPad,
                Stroke = axisBrush,
                StrokeThickness = 1
            });

            PlotCanvas.Children.Add(new Line
            {
                X1 = width - rightPad,
                Y1 = topPad,
                X2 = width - rightPad,
                Y2 = height - bottomPad,
                Stroke = axisBrush,
                StrokeThickness = 1
            });

            PlotCanvas.Children.Add(new Line
            {
                X1 = leftPad,
                Y1 = height - bottomPad,
                X2 = width - rightPad,
                Y2 = height - bottomPad,
                Stroke = axisBrush,
                StrokeThickness = 1
            });
        }

        private void DrawSeries(
            MainViewModel.SeriesSnapshot series,
            Brush brush,
            bool isLeft,
            double min,
            double max,
            double leftPad,
            double topPad,
            double plotWidth,
            double plotHeight)
        {
            var values = series.Values;
            if (values == null || values.Length < 2)
            {
                return;
            }

            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = isLeft ? 1.8 : 1.2,
                Opacity = isLeft ? 1.0 : 0.8
            };

            int count = values.Length;
            double span = Math.Max(1e-6, max - min);
            for (int i = 0; i < count; i++)
            {
                double x = leftPad + (count == 1 ? 0 : (i * plotWidth / (count - 1)));
                double norm = (values[i] - min) / span;
                double y = topPad + (1.0 - norm) * plotHeight;
                polyline.Points.Add(new Point(x, y));
            }

            PlotCanvas.Children.Add(polyline);
        }

        private static void ComputeBounds(IEnumerable<MainViewModel.SeriesSnapshot> seriesList, out double min, out double max)
        {
            min = 0;
            max = 1;

            var flat = seriesList.SelectMany(s => s.Values).ToArray();
            if (flat.Length == 0)
            {
                return;
            }

            min = flat.Min();
            max = flat.Max();
            if (Math.Abs(max - min) < 1e-6)
            {
                max = min + 1.0;
            }
        }

        private static void ResolveAxisBounds(bool isAuto, double manualMin, double manualMax, bool hasSeries, ref double min, ref double max)
        {
            if (isAuto)
            {
                if (!hasSeries)
                {
                    min = 0;
                    max = 1;
                }

                return;
            }

            if (!double.IsNaN(manualMin) && !double.IsInfinity(manualMin)
                && !double.IsNaN(manualMax) && !double.IsInfinity(manualMax)
                && manualMax > manualMin)
            {
                min = manualMin;
                max = manualMax;
                return;
            }

            if (Math.Abs(max - min) < 1e-6)
            {
                max = min + 1.0;
            }
        }

        private void DrawAxisLabel(double min, double max, bool leftAxis, double leftPad, double topPad, double plotHeight, double width)
        {
            string maxText = max.ToString("0.##");
            string minText = min.ToString("0.##");

            var maxLabel = new TextBlock
            {
                Text = maxText,
                Foreground = leftAxis ? Brushes.SteelBlue : Brushes.IndianRed,
                FontSize = 10
            };
            var minLabel = new TextBlock
            {
                Text = minText,
                Foreground = leftAxis ? Brushes.SteelBlue : Brushes.IndianRed,
                FontSize = 10
            };

            double x = leftAxis ? 2 : width - 42;
            Canvas.SetLeft(maxLabel, x);
            Canvas.SetTop(maxLabel, topPad - 2);
            Canvas.SetLeft(minLabel, x);
            Canvas.SetTop(minLabel, topPad + plotHeight - 12);

            PlotCanvas.Children.Add(maxLabel);
            PlotCanvas.Children.Add(minLabel);
        }

        private void DrawLegend(IReadOnlyList<MainViewModel.SeriesSnapshot> snapshots)
        {
            double x = 52;
            const double y = 4;

            for (int i = 0; i < snapshots.Count; i++)
            {
                var brush = GetSeriesBrush(snapshots[i].Index);

                var swatch = new Rectangle
                {
                    Width = 12,
                    Height = 6,
                    Fill = brush
                };
                Canvas.SetLeft(swatch, x);
                Canvas.SetTop(swatch, y + 5);
                PlotCanvas.Children.Add(swatch);

                var text = new TextBlock
                {
                    Text = snapshots[i].Name,
                    Foreground = Brushes.Black,
                    FontSize = 10
                };
                Canvas.SetLeft(text, x + 16);
                Canvas.SetTop(text, y);
                PlotCanvas.Children.Add(text);

                x += 16 + (snapshots[i].Name.Length * 7);
                if (x > Math.Max(90, PlotCanvas.ActualWidth - 120))
                {
                    break;
                }
            }
        }

        private void DrawHintText(string text)
        {
            var hint = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Gray,
                FontSize = 12
            };

            Canvas.SetLeft(hint, 16);
            Canvas.SetTop(hint, Math.Max(8, PlotCanvas.ActualHeight / 2 - 8));
            PlotCanvas.Children.Add(hint);
        }

        private static Brush GetSeriesBrush(int seriesIndex)
        {
            return SeriesPalette[Math.Abs(seriesIndex) % SeriesPalette.Length];
        }

        private void ToggleLanguage_Click(object sender, RoutedEventArgs e)
        {
            _isZhTw = !_isZhTw;
            ApplyLanguageDictionary(_isZhTw ? "Resources/Strings.zh-TW.xaml" : "Resources/Strings.en.xaml");
            RenderPlot();
        }

        private static void ApplyLanguageDictionary(string relativePath)
        {
            if (Application.Current == null)
            {
                return;
            }

            var merged = Application.Current.Resources.MergedDictionaries;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source;
                if (src != null && src.OriginalString.IndexOf("Resources/Strings.", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    merged.RemoveAt(i);
                }
            }

            merged.Add(new ResourceDictionary { Source = new Uri(relativePath, UriKind.Relative) });
        }

        private string GetUiString(string key, string fallback)
        {
            if (Application.Current == null)
            {
                return fallback;
            }

            object value = Application.Current.TryFindResource(key);
            return value as string ?? fallback;
        }
    }
}
