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
        using var writer = new StreamWriter(filePath,false,System.Text.Encoding.UTF8); // using 意味着临时，出域即回收

        var finalItems = items
            .OrderBy(item => item.StartTime)  // 先按开始时间从早到晚排
            .ThenBy(item => item.EndTime)     // 再按结束时间从早到晚排
            .ToList();

        int index = 1;
        foreach (var item in finalItems)
        {
            writer.WriteLine(index); // 忽略原本的 ID 顺序，直接按序写入
            writer.WriteLine($"{item.StartTimeStr} --> {item.EndTimeStr}");
            writer.WriteLine(item.Text);
            writer.WriteLine(); // 每个字幕块之间空一行
            index++;
        }
    }

}