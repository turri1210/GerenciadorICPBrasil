using System;
using System.Windows;
using System.Windows.Controls;

namespace ConfigAuditoria.Controls;

/// <summary>
///     Panel that automatically adapts the number of columns according to the available width
///     and allows items to span multiple columns.
/// </summary>
public class AdaptiveGridPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth),
            typeof(double),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(240d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(
            nameof(MaxColumns),
            typeof(int),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinColumnsProperty =
        DependencyProperty.Register(
            nameof(MinColumns),
            typeof(int),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(
            nameof(RowSpacing),
            typeof(double),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached(
            "ColumnSpan",
            typeof(int),
            typeof(AdaptiveGridPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    ///     Minimum width that each column should occupy before the panel reduces the column count.
    /// </summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>
    ///     Maximum number of columns supported by the panel.
    /// </summary>
    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>
    ///     Minimum number of columns that should be rendered.
    /// </summary>
    public int MinColumns
    {
        get => (int)GetValue(MinColumnsProperty);
        set => SetValue(MinColumnsProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public static void SetColumnSpan(UIElement element, int value) =>
        element.SetValue(ColumnSpanProperty, Math.Max(1, value));

    public static int GetColumnSpan(UIElement element) =>
        (int)element.GetValue(ColumnSpanProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        var effectiveWidth = GetEffectiveWidth(availableSize.Width);
        var columns = CalculateColumnCount(effectiveWidth);
        var cellWidth = CalculateCellWidth(effectiveWidth, columns);

        var totalHeight = 0d;
        var currentRowHeight = 0d;
        var currentColumn = 0;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            if (child is null)
            {
                continue;
            }

            var span = AdjustSpan(child, columns);

            if (currentColumn > 0 && currentColumn + span > columns)
            {
                totalHeight += currentRowHeight + RowSpacing;
                currentRowHeight = 0;
                currentColumn = 0;
            }

            var childWidth = cellWidth * span + ColumnSpacing * (span - 1);
            child.Measure(new Size(childWidth, double.PositiveInfinity));

            currentRowHeight = Math.Max(currentRowHeight, child.DesiredSize.Height);
            currentColumn += span;

            if (currentColumn >= columns)
            {
                totalHeight += currentRowHeight;
                currentRowHeight = 0;
                currentColumn = 0;

                if (index < InternalChildren.Count - 1)
                {
                    totalHeight += RowSpacing;
                }
            }
        }

        if (currentColumn > 0)
        {
            totalHeight += currentRowHeight;
        }

        return new Size(effectiveWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var effectiveWidth = GetEffectiveWidth(finalSize.Width);
        var columns = CalculateColumnCount(effectiveWidth);
        var cellWidth = CalculateCellWidth(effectiveWidth, columns);

        var y = 0d;
        var currentColumn = 0;
        var currentRowHeight = 0d;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            if (child is null)
            {
                continue;
            }

            var span = AdjustSpan(child, columns);

            if (currentColumn > 0 && currentColumn + span > columns)
            {
                y += currentRowHeight + RowSpacing;
                currentRowHeight = 0;
                currentColumn = 0;
            }

            var childWidth = cellWidth * span + ColumnSpacing * (span - 1);
            var x = currentColumn * (cellWidth + ColumnSpacing);
            var height = child.DesiredSize.Height;

            child.Arrange(new Rect(new Point(x, y), new Size(childWidth, height)));

            currentRowHeight = Math.Max(currentRowHeight, height);
            currentColumn += span;

            if (currentColumn >= columns)
            {
                currentColumn = 0;
                y += currentRowHeight;
                currentRowHeight = 0;

                if (index < InternalChildren.Count - 1)
                {
                    y += RowSpacing;
                }
            }
        }

        return finalSize;
    }

    private double GetEffectiveWidth(double availableWidth)
    {
        if (!double.IsNaN(availableWidth) && !double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return availableWidth;
        }

        var fallbackColumns = Math.Max(1, MaxColumns);
        return fallbackColumns * MinColumnWidth + ColumnSpacing * (fallbackColumns - 1);
    }

    private int CalculateColumnCount(double width)
    {
        var minWidth = Math.Max(1d, MinColumnWidth);
        var spacing = Math.Max(0d, ColumnSpacing);
        var tentative = (int)Math.Floor((width + spacing) / (minWidth + spacing));

        var minColumns = Math.Max(1, MinColumns);
        var maxColumns = Math.Max(minColumns, MaxColumns);

        if (tentative < minColumns)
        {
            return minColumns;
        }

        if (tentative > maxColumns)
        {
            return maxColumns;
        }

        return tentative;
    }

    private double CalculateCellWidth(double width, int columns)
    {
        if (columns <= 0)
        {
            return width;
        }

        var spacing = Math.Max(0d, ColumnSpacing);
        var totalSpacing = spacing * (columns - 1);
        var freeWidth = Math.Max(0d, width - totalSpacing);

        return columns == 0 ? freeWidth : freeWidth / columns;
    }

    private int AdjustSpan(UIElement element, int columns)
    {
        var span = GetColumnSpan(element);
        if (span <= 1)
        {
            return 1;
        }

        if (columns <= 0)
        {
            return 1;
        }

        return Math.Max(1, Math.Min(columns, span));
    }
}
