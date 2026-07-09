using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Subtimer.Models;

namespace Subtimer.Services;

public class SubtitleExporter
{
    // 导出为 .srt
    public void ExportToSrt(string filePath, IEnumerable<SubtitleItem> items)
    {
        using var writer = new StreamWriter(filePath,false,System.Text.Encoding.UTF8);

        var finalItems = items
            .OrderBy(item => item.StartTime)  // 1. 先按开始时间从早到晚排
            .ThenBy(item => item.EndTime)          // 2. 如果时间一样，按序号从小到大排
            .ToList();

        int index = 1;
        foreach (var item in finalItems)
        {
            writer.WriteLine(index); // 忽略原本的 ID 顺序，直接按序写入
            writer.WriteLine($"{FormatTime(item.StartTime)} --> {FormatTime(item.EndTime)}");
            writer.WriteLine(item.Text);
            writer.WriteLine(); // 每个字幕块之间空一行
            index++;
        }
    }

    private string FormatTime(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}