using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CMPDriverTool.Core.Gaming;
using CMPDriverTool.Core.Models;
using Microsoft.Win32;

namespace CMPDriverTool.App.ViewModels;

public partial class GamesViewModel : ObservableObject
{
    private readonly GameDetector _gameDetector;
    private readonly DirectXRegistryManager _registryManager;

    [ObservableProperty]
    private ObservableCollection<GameProfile> _games = new();

    [ObservableProperty]
    private ObservableCollection<GameProfile> _displayGames = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Готов к сканированию установленных игр";

    [ObservableProperty]
    private int _cmpAssignedCount;

    public GamesViewModel(GameDetector? gameDetector = null, DirectXRegistryManager? registryManager = null)
    {
        _gameDetector = gameDetector ?? new GameDetector();
        _registryManager = registryManager ?? new DirectXRegistryManager();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task RefreshGamesAsync()
    {
        IsScanning = true;
        StatusMessage = "Поиск установленных игр в библиотеках Steam, Epic Games и настройках Windows...";

        try
        {
            var detected = await Task.Run(() => _gameDetector.DetectGames());

            Games.Clear();
            foreach (var g in detected)
            {
                Games.Add(g);
            }

            ApplyFilter();
            UpdateStats();

            StatusMessage = $"Найдено игр: {Games.Count}. Направлено на CMP: {CmpAssignedCount}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сканирования: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void ToggleCmpPreference(GameProfile game)
    {
        if (game == null) return;

        // Toggle state
        game.IsCmpPreferred = !game.IsCmpPreferred;

        bool ok = _registryManager.SetGpuPreference(game.ExecutablePath, game.Preference);
        if (ok)
        {
            StatusMessage = game.IsCmpPreferred
                ? $"Игра '{game.Title}' успешно перенаправлена на рендер CMP (Высокая производительность)!"
                : $"Для игры '{game.Title}' сброшено предпочтение GPU на значение по умолчанию.";
        }
        else
        {
            // Revert on error
            game.IsCmpPreferred = !game.IsCmpPreferred;
            MessageBox.Show("Не удалось записать настройку в реестр Windows (HKCU\\Software\\Microsoft\\DirectX\\UserGpuPreferences).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateStats();
    }

    [RelayCommand]
    private void AddCustomGame()
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Исполняемые файлы игр (*.exe)|*.exe",
            Title = "Выберите исполняемый файл игры (.exe)"
        };

        if (ofd.ShowDialog() == true)
        {
            string exe = ofd.FileName;
            var existing = Games.FirstOrDefault(g => g.ExecutablePath.Equals(exe, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                MessageBox.Show($"Игра '{existing.Title}' уже есть в списке.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string title = Path.GetFileNameWithoutExtension(exe);
            var newProfile = new GameProfile
            {
                ExecutablePath = exe,
                Title = title,
                Source = "Добавлена вручную",
                Preference = DirectXGpuPreference.HighPerformance
            };

            _registryManager.SetGpuPreference(exe, DirectXGpuPreference.HighPerformance);
            Games.Insert(0, newProfile);
            ApplyFilter();
            UpdateStats();

            StatusMessage = $"Игра '{title}' добавлена и настроена на запуск через CMP!";
        }
    }

    [RelayCommand]
    public void RemoveCustomGame(GameProfile game)
    {
        if (game == null) return;

        _registryManager.RemoveGpuPreference(game.ExecutablePath);
        Games.Remove(game);
        ApplyFilter();
        UpdateStats();

        StatusMessage = $"Игра '{game.Title}' удалена из настроек.";
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? Games
            : Games.Where(g => g.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                               g.ExecutablePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        DisplayGames.Clear();
        foreach (var g in filtered)
        {
            DisplayGames.Add(g);
        }
    }

    private void UpdateStats()
    {
        CmpAssignedCount = Games.Count(g => g.IsCmpPreferred);
    }
}
