using System.Windows;
using System.Windows.Controls;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App.Views;

public partial class CosmeticsView : UserControl
{
    /// <summary>Card margins in the CardToggle style, added to the target card width.</summary>
    private const double CardGutter = 10;

    public CosmeticsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumnCount();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) UpdateColumnCount();
        };
    }

    /// <summary>
    /// The grid virtualizes rows, so the number of cards per row comes from the width the grid
    /// actually has. Recomputed on resize and when the section changes.
    /// </summary>
    private void UpdateColumnCount()
    {
        if (DataContext is not CosmeticsViewModel vm) return;
        var host = vm.IsMusicKitSection ? MusicHost : SkinHost;
        if (host is null) return;

        var available = host.ActualWidth - CardGutter;
        if (available <= 0) return;

        var cell = vm.SkinCardWidth + CardGutter;
        var columns = Math.Clamp((int)(available / cell), 1, 12);
        if (vm.ColumnCount != columns) vm.ColumnCount = columns;
    }
}
