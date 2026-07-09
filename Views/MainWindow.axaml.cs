using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Subtimer.ViewModels;

namespace Subtimer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OpenSrtClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
}