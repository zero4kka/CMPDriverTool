using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CMPDriverTool.Core.Models;

namespace CMPDriverTool.App.ViewModels;

public enum NavigationTab
{
    Dashboard,
    Installer,
    Games,
    Tools
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private NavigationTab _currentTab = NavigationTab.Dashboard;

    [ObservableProperty]
    private DashboardViewModel _dashboardVm;

    [ObservableProperty]
    private InstallerViewModel _installerVm;

    [ObservableProperty]
    private GamesViewModel _gamesVm;

    [ObservableProperty]
    private ToolsViewModel _toolsVm;

    [ObservableProperty]
    private string _windowTitle = "CMP Driver Tool — Утилита для NVIDIA CMP 30HX / 40HX";

    public MainViewModel()
    {
        _installerVm = new InstallerViewModel(onReturnToDashboard: () => CurrentTab = NavigationTab.Dashboard);

        _dashboardVm = new DashboardViewModel(onNavigateToInstall: (driver, localPath, useNvenc, customGpuName) =>
        {
            CurrentTab = NavigationTab.Installer;
            _ = _installerVm.StartInstallationAsync(driver, localPath, useNvenc, customGpuName);
        });

        _gamesVm = new GamesViewModel();
        _toolsVm = new ToolsViewModel();
    }

    public async void Initialize()
    {
        await DashboardVm.RefreshAllAsync();
        await GamesVm.RefreshGamesAsync();
        await ToolsVm.RefreshAllAsync();
    }

    [RelayCommand]
    private void Navigate(NavigationTab tab)
    {
        CurrentTab = tab;
    }
}
