using System.Collections.Generic;
using System.IO;
using Subtimer.Models;
using Subtimer.Services;

using Xunit;

namespace Subtimer.Tests;

public class SubtitleParserTests
{
    [Fact]
    public void Parse_ValidSrtString_ReturnsCorrectSubtitleItems()
    {
        string fixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestNormal.srt"));
        List<SubtitleItem> result = SubtitleParser.ParseSrt(fixturePath);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // 预期应该成功解析出 2 条字幕

        // 验证第一条字幕
        var firstItem = result[0];
        Assert.Equal(1, firstItem.Id);
        Assert.Equal(TimeSpan.FromSeconds(0), firstItem.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(1).Add(TimeSpan.FromMilliseconds(500)), firstItem.EndTime);
        Assert.Equal("Hello,World!\nHello,C#!", firstItem.Text.Replace("\r\n", "\n"));

        // 验证第二条字幕
        var secondItem = result[1];
        Assert.Equal(2, secondItem.Id);
        Assert.Equal(TimeSpan.FromSeconds(2), secondItem.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(3).Add(TimeSpan.FromMilliseconds(325)), secondItem.EndTime);
        Assert.Equal("Hello,user!", secondItem.Text);
    }

    [Fact]
    public void Constructor_UsesHalfWidthAdjustedSpeechSpeed()
    {
        var item = new SubtitleItem(1, TimeSpan.Zero, TimeSpan.FromSeconds(2), "A中B");

        Assert.Equal(1f, item.SpeechSpeed, 3);
    }
}