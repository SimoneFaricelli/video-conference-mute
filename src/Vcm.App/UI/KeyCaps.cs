using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vcm.App.UI;

/// <summary>Row of key caps ("Win" "Shift" "Q") in the PowerToys style.</summary>
internal sealed class KeyCaps : ItemsControl
{
    public static readonly DependencyProperty IsAccentProperty =
        DependencyProperty.Register(nameof(IsAccent), typeof(bool), typeof(KeyCaps), new PropertyMetadata(true));

    public static readonly DependencyProperty KeySizeProperty =
        DependencyProperty.Register(nameof(KeySize), typeof(double), typeof(KeyCaps), new PropertyMetadata(12.0));

    public KeyCaps()
    {
        Focusable = false;
        IsTabStop = false;
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        ItemsPanel = new ItemsPanelTemplate(panel);
        ItemTemplateSelector = new KeyTemplateSelector(this);
    }

    public bool IsAccent
    {
        get => (bool)GetValue(IsAccentProperty);
        set => SetValue(IsAccentProperty, value);
    }

    public double KeySize
    {
        get => (double)GetValue(KeySizeProperty);
        set => SetValue(KeySizeProperty, value);
    }

    private sealed class KeyTemplateSelector(KeyCaps owner) : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.PaddingProperty, new Thickness(owner.KeySize * 0.7, owner.KeySize * 0.3, owner.KeySize * 0.7, owner.KeySize * 0.35));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
            border.SetValue(FrameworkElement.MinWidthProperty, owner.KeySize * 2.4);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetResourceReference(Border.BackgroundProperty, owner.IsAccent ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush");
            border.SetResourceReference(Border.BorderBrushProperty, owner.IsAccent ? "AccentFillColorDefaultBrush" : "ControlElevationBorderBrush");

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            text.SetValue(TextBlock.FontSizeProperty, owner.KeySize);
            text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            text.SetResourceReference(TextBlock.ForegroundProperty, owner.IsAccent ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
            border.AppendChild(text);

            return new DataTemplate { VisualTree = border };
        }
    }
}
