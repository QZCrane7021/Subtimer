using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Subtimer.ViewModels;
using Subtimer.Models;

namespace Subtimer.Views;

public partial class MainWindow : Window
{
    private bool _isSyncingSelection;

    public MainWindow()
    {
        InitializeComponent();
    }

    public static class MediaFileFilters
    {
        // 1. 视频文件过滤器
        public static FilePickerFileType VideoFiles { get; } = new("视频文件")
        {
            Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.ts", "*.flv", "*.wmv", "*.m4v" },
            MimeTypes = new[] { "video/*" }
        };

        // 2. 音频文件过滤器
        public static FilePickerFileType AudioFiles { get; } = new("音频文件")
        {
            Patterns = new[] { "*.mp3", "*.wav", "*.aac", "*.flac", "*.m4a", "*.ogg", "*.wma", "*.opus" },
            MimeTypes = new[] { "audio/*" }
        };

        // 3. 所有多媒体文件（音视频合并，方便用户一次性查看）
        public static FilePickerFileType AllMediaFiles { get; } = new("所有多媒体文件")
        {
            Patterns = new[] 
            { 
                "*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.ts", "*.flv", "*.wmv", "*.m4v",
                "*.mp3", "*.wav", "*.aac", "*.flac", "*.m4a", "*.ogg", "*.wma", "*.opus" 
            }
        };
    }

    // 打开字幕按钮
    private async void OpenSrt_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "请选择要打开的字幕文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("支持的字幕格式")
                {
                    Patterns = new[] { "*.srt", "*.ass" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            string filePath = files[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.OpenSubtitleFile(filePath);
                SyncSelectionToGrid(viewModel.CurrentSubtitle);
            }
        }
    }

    private async void OpenMedia_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "请选择要打开的字幕文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
                {
                    MediaFileFilters.AllMediaFiles, // 默认显示“所有多媒体”
                    MediaFileFilters.VideoFiles,    // 切换下拉菜单可以只看视频
                    MediaFileFilters.AudioFiles,    // 切换下拉菜单可以只看音频
                    FilePickerFileTypes.All         // “所有文件 (*.*)”
                }
        });

        if (files.Count > 0)
        {
            string filePath = files[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.OpenMediaFile(filePath);
            }
        }
    } 

    // 字幕列表选择区域变化
    private void SubtitleList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            // 先让 ViewModel 默默更新数据（SelectedSubtitles 和 CurrentSubtitle）
            viewModel.SyncSelection(e.RemovedItems.OfType<SubtitleItem>(), e.AddedItems.OfType<SubtitleItem>());
            
            // 检查 DataGrid 此时真实的选中数量
            // 只有当数量真的归零了（即用户按 Ctrl 取消了最后一条，或者全选后误触取消干净），
            // 我们才老老实实启动 Aegisub 的保底机制，强行把 View 掰回来。
            if (SubtitleDataGrid != null && SubtitleDataGrid.SelectedItems.Count == 0)
            {
                SyncSelectionToGrid(viewModel.CurrentSubtitle);
            }
        }
    }

    private void SyncSelectionToGrid(SubtitleItem? subtitle)
    {
        if (_isSyncingSelection || DataContext is not MainWindowViewModel)
        {
            return;
        }

        if (SubtitleDataGrid is null)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            if (subtitle is null)
            {
                SubtitleDataGrid.SelectedIndex = -1;
                return;
            }

            var index = (DataContext as MainWindowViewModel)?.SubtitleList.IndexOf(subtitle) ?? -1;
            SubtitleDataGrid.SelectedIndex = index;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}