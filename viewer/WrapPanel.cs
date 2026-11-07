using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace KeepAwake.Viewer
{
    /// <summary>简单的自动换行面板：子元素按可用宽度换行排列。</summary>
    public sealed class WrapPanel : Panel
    {
        public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
            "HorizontalSpacing", typeof(double), typeof(WrapPanel), new PropertyMetadata(10.0, OnSpacingChanged));

        public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
            "VerticalSpacing", typeof(double), typeof(WrapPanel), new PropertyMetadata(8.0, OnSpacingChanged));

        public double HorizontalSpacing
        {
            get { return (double)GetValue(HorizontalSpacingProperty); }
            set { SetValue(HorizontalSpacingProperty, value); }
        }

        public double VerticalSpacing
        {
            get { return (double)GetValue(VerticalSpacingProperty); }
            set { SetValue(VerticalSpacingProperty, value); }
        }

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WrapPanel panel = d as WrapPanel;
            if (panel != null) panel.InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
            double lineWidth = 0.0;
            double lineHeight = 0.0;
            double totalHeight = 0.0;
            double widest = 0.0;

            foreach (UIElement child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size desired = child.DesiredSize;

                if (lineWidth > 0.0 && lineWidth + HorizontalSpacing + desired.Width > maxWidth)
                {
                    totalHeight += lineHeight + VerticalSpacing;
                    widest = Math.Max(widest, lineWidth);
                    lineWidth = 0.0;
                    lineHeight = 0.0;
                }

                if (lineWidth > 0.0) lineWidth += HorizontalSpacing;
                lineWidth += desired.Width;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }

            totalHeight += lineHeight;
            widest = Math.Max(widest, lineWidth);
            if (!double.IsInfinity(maxWidth)) widest = Math.Min(widest, maxWidth);
            return new Size(widest, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0.0;
            double y = 0.0;
            double lineHeight = 0.0;

            foreach (UIElement child in Children)
            {
                Size desired = child.DesiredSize;

                if (x > 0.0 && x + HorizontalSpacing + desired.Width > finalSize.Width)
                {
                    x = 0.0;
                    y += lineHeight + VerticalSpacing;
                    lineHeight = 0.0;
                }

                if (x > 0.0) x += HorizontalSpacing;
                child.Arrange(new Rect(x, y, desired.Width, desired.Height));
                x += desired.Width;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }

            return finalSize;
        }
    }
}
