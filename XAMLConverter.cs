using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PiWpfUi
{
    internal class TextLineCounter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var tb = values[0] as TextBox;
            var text = values[1] as string ?? "";
            if (tb == null) return 940.0;

            // 上限 = TextBox 自己的 MaxWidth/Width，兜底 940
            double max = !double.IsInfinity(tb.MaxWidth) && tb.MaxWidth > 0 ? tb.MaxWidth
                      : !double.IsNaN(tb.Width) && tb.Width > 0 ? tb.Width
                      : 940.0;

            // 早退：超长直接满宽，不测量
            if (text.Length > 80) return max;

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var ft = new FormattedText(text, culture, FlowDirection.LeftToRight,
                typeface, tb.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(tb).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace + 20 <= max ? double.NaN : max;
        }
        public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class TextBoxTrimmer : IMultiValueConverter
    {
        double default_width = 940;
        string cutOfSign = "…";
        static readonly ConditionalWeakTable<TextBox, object> SizeHooked = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var tb = values[0] as TextBox;
            var text = values[1] as string ?? "";
            var btn = values[2] as Button;   // 第三个参数：按钮，直接控制它
            if (tb == null) return text;

            // 上限优先级：MaxWidth > Width > ActualWidth（真正布局出来的当前宽度）> 兜底 940
            double max = ResolveMaxWidth(tb);

            // 没显式设 MaxWidth/Width、且首次布局还没完成（ActualWidth 还是 0）时，
            // 挂一次 SizeChanged：等布局结束再强制重算绑定，否则会一直拿 940。
            if (double.IsInfinity(tb.MaxWidth) && double.IsNaN(tb.Width) && tb.ActualWidth <= 0)
                HookSizeChanged(tb);

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            double dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;

            // 量一次，没超就直接返回原文
            var ft = new FormattedText(text, culture, FlowDirection.LeftToRight,
                typeface, tb.FontSize, Brushes.Black, dpi);
            bool truncated = ft.WidthIncludingTrailingWhitespace > max;
            if (btn != null) btn.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;   // 截断→显示按钮
            if (!truncated) return text;

            // 超了：二分找最长的"前缀 + 省略号"能放下的长度
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var candidate = text[..mid] + cutOfSign;
                var f = new FormattedText(candidate, culture, FlowDirection.LeftToRight,
                    typeface, tb.FontSize, Brushes.Black, dpi);
                if (f.WidthIncludingTrailingWhitespace <= max) lo = mid;
                else hi = mid - 1;
            }
            if (btn != null) btn.Visibility = Visibility.Visible;
            return text[..lo] + cutOfSign;
        }

        private double ResolveMaxWidth(TextBox tb)
        {
            if (!double.IsInfinity(tb.MaxWidth) && tb.MaxWidth > 0) return tb.MaxWidth;
            if (!double.IsNaN(tb.Width) && tb.Width > 0) return tb.Width;
            if (tb.ActualWidth > 0) return Math.Max(0, tb.ActualWidth - 6);   // 留一点内边距，避免顶着边
            return default_width;
        }

        private static void HookSizeChanged(TextBox tb)
        {
            if (SizeHooked.TryGetValue(tb, out _)) return;
            SizeHooked.Add(tb, null!);
            tb.SizeChanged += (s, e) =>
            {
                // 布局变化后重新跑一次 Text 的 MultiBinding，让 Converter 拿到新的 ActualWidth
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal class TextBoxLineLimiter : IMultiValueConverter
    {
        int limit = 10;
        string cutOfSign = "";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var tb = values[0] as TextBox;
            var text = values[1] as string ?? "";
            var btn = values[2] as Button;   // 第三个参数：按钮，直接控制它
            if (tb == null) return text;

            int maxLines = parameter != null && int.TryParse(parameter.ToString(), out var ml) && ml > 0
                ? ml : limit;

            double availWidth = !double.IsInfinity(tb.MaxWidth) && tb.MaxWidth > 0
                ? tb.MaxWidth - 20 : 10000;

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            double dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;

            // 参考高度：恰好 maxLines 行的文本量出来，直接当上限，绕开除法误差
            var probe = new FormattedText(
                string.Join("\n", Enumerable.Repeat("中", maxLines)),
                culture, FlowDirection.LeftToRight, typeface, tb.FontSize, Brushes.Black, dpi);
            probe.MaxTextWidth = availWidth;
            double maxLinesHeight = probe.Height;

            // 判定：候选高度 <= 参考高，就是放得下 maxLines 行
            bool Fits(string s)
            {
                var f = new FormattedText(s, culture, FlowDirection.LeftToRight,
                    typeface, tb.FontSize, Brushes.Black, dpi);
                f.MaxTextWidth = availWidth;
                return f.Height <= maxLinesHeight;
            }

            bool fits = Fits(text);
            if (btn != null) btn.Visibility = fits ? Visibility.Collapsed : Visibility.Visible;   // 超行→显示按钮
            if (fits) return text;   // 行数够，原样返回

            // 超行：二分找"砍到几字符能放下 maxLines 行"，再补省略号
            int lo = 0, hi = text.Length;
            string result = "";
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Fits(text[..mid])) { result = text[..mid]; lo = mid; }
                else hi = mid - 1;
            }
            return result + cutOfSign;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
