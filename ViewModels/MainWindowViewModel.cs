using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Subtimer.Models;
using Subtimer.Services;

namespace Subtimer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<SubtitleItem> _subtitleList = new();

    [ObservableProperty]
    private ObservableCollection<SubtitleItem> _selectedSubtitles = new();

    [ObservableProperty]
    private SubtitleItem? _currentSubtitle;
    [ObservableProperty]
    private string _startTimeStr = "00:00:00,000";

    [ObservableProperty]
    private string _endTimeStr = "00:00:00,000";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _speechSpeedStr = "0";

    public void OpenSubtitleFile(string filePath)
    {
        var items = SubtitleParser.ParseSrt(filePath);
        SubtitleList.Clear();
        foreach (var item in items)
        {
            SubtitleList.Add(item);
        }
        Console.WriteLine("SubtitleList Editing Complete!");
    }
}