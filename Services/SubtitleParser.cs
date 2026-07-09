using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Subtimer.Models;

namespace Subtimer.Services;

public static class SubtitleParser
{
    // 读取文件并转换为以 SubtitleItem 为单项的列表
    public static List<SubtitleItem> ParseSrt(string filePath)
    {
        var result = new List<SubtitleItem>();
        string[] lines = File.ReadAllLines(filePath);
        var currentBlock = new List<string>();

        // 步进遍历，检测到空行就把前面的部分打包处理，然后清空临时列表
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentBlock.Count > 0)
                {
                    var item = ParseSingleBlock(currentBlock);
                    if (item != null) result.Add(item);
                    currentBlock.Clear();
                }
            }
            else
            {
                currentBlock.Add(line.Trim());
            }
        }

        if (currentBlock.Count > 0)
        {
            var item = ParseSingleBlock(currentBlock);
            if (item != null) result.Add(item);
        }

        return result;
    }

    private static SubtitleItem? ParseSingleBlock(List<string> blockLines)
    {
        // 假设首行是 ID
        // 如果操作失败说明首行的 ID 缺失了，考虑首行是时间戳
        if (int.TryParse(blockLines[0], out int id))
        {
            blockLines.RemoveAt(0);
        }

        // 分离出两个时间戳
        var timeParts = blockLines[0].Split(["-->"], StringSplitOptions.RemoveEmptyEntries);
        if (timeParts.Length != 2)
        {
            throw new FormatException($"Expected 2 time stamps for one sub line, received {timeParts.Length}");
        }

        // 使用 System.Globalization，以确保在世界各地都能正常处理
        if (!TimeSpan.TryParseExact(timeParts[0].Trim(), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture, out TimeSpan start)) 
        {
            throw new FormatException("Starting timestamp is corrupted");
        }
        if (!TimeSpan.TryParseExact(timeParts[1].Trim(), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture, out TimeSpan end))
        {
            throw new FormatException("Ending timestamp is corrupted");
        }

        blockLines.RemoveAt(0);

        // 获取文本信息
        string content = string.Join(Environment.NewLine, blockLines);

        return new SubtitleItem(id, start, end, content);        
    }
}