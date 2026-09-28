using System.Windows;
using System.Windows.Controls;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App.Views;

public partial class CosmeticsView : UserControl
{
    public CosmeticsView()
    {
        InitializeComponent();
    }

    private void OnWeaponsTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CosmeticsViewModel vm) vm.CurrentSubTab = CosmeticsSubTab.Weapons;
    }

    private void OnKnifeTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CosmeticsViewModel vm) vm.CurrentSubTab = CosmeticsSubTab.Knife;
    }

    private void OnGlovesTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CosmeticsViewModel vm) vm.CurrentSubTab = CosmeticsSubTab.Gloves;
    }

    private void OnMusicKitTabClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CosmeticsViewModel vm) vm.CurrentSubTab = CosmeticsSubTab.MusicKit;
    }
}
