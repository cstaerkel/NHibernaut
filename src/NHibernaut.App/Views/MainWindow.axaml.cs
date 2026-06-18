using System;
using Avalonia.Controls;

namespace NHibernaut.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Keep the window from shrinking narrower than the full toolbar (otherwise the rightmost
        // button — the theme toggle — gets clipped). The toolbar is a horizontal StackPanel, so its
        // DesiredSize.Width is the natural width even while the parent clips it; track it and pin the
        // window's MinWidth to it (plus a small allowance for the window chrome).
        LayoutUpdated += (_, _) =>
        {
            var w = TopBar.DesiredSize.Width;
            if (w <= 0) return;
            var needed = w + 16;
            if (Math.Abs(MinWidth - needed) > 0.5)
                MinWidth = needed;
        };
    }
}
