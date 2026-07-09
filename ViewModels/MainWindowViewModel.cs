using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subtimer.Models;
using Subtimer.Services;

namespace Subtimer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // 字幕导出对象
    private readonly SubtitleExporter _subtitleExporter = new(); // read-only,只可读,值可以在构造函数中动态决定,适用于对象
    
    // 目前正编辑的字幕文件位置
    private string? _currentSubtitleFilePath;

    // 字幕列表
    [ObservableProperty] // CommunityToolkit 新增的标签,在其他地方可以 PascalCase 格式调用,变化时自动调用 On[Property]Changed
    private ObservableCollection<SubtitleItem> _subtitleList = new();

    // 选中的字幕集合
    [ObservableProperty]
    private ObservableCollection<SubtitleItem> _selectedSubtitles = new();

    // 选中的当前字幕
    [ObservableProperty]
    private SubtitleItem? _currentSubtitle;

    // 当前字幕的起始时间
    [ObservableProperty]
    private string _startTimeStr = "00:00:00,000";

    // 当前字幕的结束时间
    [ObservableProperty]
    private string _endTimeStr = "00:00:00,000";

    // 当前字幕的文字内容
    [ObservableProperty]
    private string _text = "";

    // 当前字幕的语速
    [ObservableProperty]
    private string _speechSpeedStr = "0";

    // 打开 srt 文件
    public void OpenSubtitleFile(string filePath)
    {
        var items = SubtitleParser.ParseSrt(filePath);
        SubtitleList.Clear();
        foreach (var item in items)
        {
            SubtitleList.Add(item);
        }

        _currentSubtitleFilePath = filePath;
        EnsureSelection();
        Console.WriteLine("SubtitleList Filling Complete!");
    }

    public void SyncSelection(IEnumerable<SubtitleItem> removedItems, IEnumerable<SubtitleItem> addedItems)
    {
        if (SelectedSubtitles is null)
        {
            SelectedSubtitles = new ObservableCollection<SubtitleItem>();
        }

        foreach (var removedItem in removedItems)
        {
            SelectedSubtitles.Remove(removedItem);
        }

        foreach (var addedItem in addedItems)
        {
            if (!SelectedSubtitles.Contains(addedItem))
            {
                SelectedSubtitles.Add(addedItem);
            }
        }

        SubtitleItem? fallbackSelection = null;
        if (addedItems.Any())
        {
            fallbackSelection = addedItems.Last();
        }
        else if (SelectedSubtitles.Count > 0)
        {
            fallbackSelection = SelectedSubtitles[^1];
        }
        else if (CurrentSubtitle is not null && SubtitleList.Contains(CurrentSubtitle))
        {
            fallbackSelection = CurrentSubtitle;
        }
        else if (SubtitleList.Count > 0)
        {
            fallbackSelection = SubtitleList[0];
        }

        if (fallbackSelection is null)
        {
            SelectedSubtitles.Clear();
            CurrentSubtitle = null;
            return;
        }

        if (!SelectedSubtitles.Contains(fallbackSelection))
        {
            SelectedSubtitles.Add(fallbackSelection);
        }

        CurrentSubtitle = fallbackSelection;
    }

    public void EnsureSelection()
    {
        if (SubtitleList.Count == 0)
        {
            SelectedSubtitles.Clear();
            CurrentSubtitle = null;
            return;
        }

        if (CurrentSubtitle is null || !SubtitleList.Contains(CurrentSubtitle))
        {
            CurrentSubtitle = SubtitleList[0];
        }

        SelectedSubtitles.Clear();
        SelectedSubtitles.Add(CurrentSubtitle);
    }

    // 用于保存
    [RelayCommand]
    private void SaveSrt(string? filePath = null)
    {
        var actuallFilePath = filePath ?? _currentSubtitleFilePath;

        if (string.IsNullOrWhiteSpace(actuallFilePath))
        {
            return;
        }

        _subtitleExporter.ExportToSrt(actuallFilePath, SubtitleList);
        Console.WriteLine($"Subtitles exported to: {actuallFilePath}");
    }
}