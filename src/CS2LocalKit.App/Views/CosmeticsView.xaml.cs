using System.Windows;
using System.Windows.Controls;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App.Views;

public partial class CosmeticsView : UserControl
{
    /// <summary>Card margins, added to the target card width to get one grid cell.</summary>
    private const double CardGutter = 8;

    /// <summary>
    /// Reserved for the grid's own scrollbar, so the count never assumes width the scrollbar will
    /// take and then has to drop a column the first time the list gets long enough to scroll.
    /// </summary>
    private const double ScrollbarReserve = 12;

    private const int MaxColumns = 6;

    public CosmeticsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumnCount();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) UpdateColumnCount();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CosmeticsViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CosmeticsViewModel.IsMusicKitSection)) UpdateColumnCount();
                };
            }
            UpdateColumnCount();
        };

        // The card area, not the window, decides how many cards fit: it is measured after the shell
        // is visible and after a section swap, which is when the first honest width appears.
        SkinGridBorder.SizeChanged += (_, _) => UpdateColumnCount();
        MusicGridBorder.SizeChanged += (_, _) => UpdateColumnCount();
    }

    /// <summary>
    /// The grid virtualizes rows, so the number of cards per row comes from the width the card area
    /// actually has. Recomputed on resize, when the view appears, and when the section swaps the
    /// music grid in for the weapon browser.
    /// </summary>
    private void UpdateColumnCount()
    {
        if (DataContext is not CosmeticsViewModel vm) return;
        var host = vm.IsMusicKitSection ? MusicGridBorder : SkinGridBorder;
        if (host is null) return;

        var available = host.ActualWidth - host.Padding.Left - host.Padding.Right - ScrollbarReserve;
        if (available <= 0) return;

        var cell = vm.SkinCardWidth + CardGutter;
        var columns = Math.Clamp((int)(available / cell), 1, MaxColumns);
        if (vm.ColumnCount != columns) vm.ColumnCount = columns;
    }
}
