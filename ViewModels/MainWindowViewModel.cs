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
    private SubtitleItem? _selectedSubtitle;

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