using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Subtimer.ViewModels;
using Subtimer.Models;

namespace Subtimer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

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
                Console.WriteLine(filePath);
                viewModel.OpenSubtitleFile(filePath);
            }
        }
    }

    private void SubtitleList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (viewModel.SelectedSubtitles is null)
            {
                viewModel.SelectedSubtitles = new ObservableCollection<SubtitleItem>();
            }

            foreach (var removedItem in e.RemovedItems.OfType<SubtitleItem>())
            {
                viewModel.SelectedSubtitles.Remove(removedItem);
            }

            foreach (var addedItem in e.AddedItems.OfType<SubtitleItem>())
            {
                if (!viewModel.SelectedSubtitles.Contains(addedItem))
                {
                    viewModel.SelectedSubtitles.Add(addedItem);
                }
            }

            if (e.AddedItems.Count > 0)
            {
                var lastClicked = e.AddedItems[e.AddedItems.Count - 1] as SubtitleItem;
                if (lastClicked != null)
                {
                    viewModel.CurrentSubtitle = lastClicked;
                    // viewModel.MoveTimelineTo(lastClicked.StartTime);
                }
            }
            else if (viewModel.SelectedSubtitles.Count > 0)
            {
                viewModel.CurrentSubtitle = viewModel.SelectedSubtitles[^1];
            }
            else
            {
                viewModel.CurrentSubtitle = null;
            }
        }
    }
}