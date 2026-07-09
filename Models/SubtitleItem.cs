using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Subtimer.Models;

public partial class SubtitleItem : ObservableObject
{
    [ObservableProperty]
    private int id;

    [ObservableProperty]
    private TimeSpan startTime;

    [ObservableProperty]
    private TimeSpan endTime;

    [ObservableProperty]
    private float speechSpeed;

    [ObservableProperty]
    private float speechSpeed2ndLang;

    [ObservableProperty]
    private string text = string.Empty;

    public string? Text2ndLang { get; set; }

    public string SpeechSpeedStr => ((int)Math.Round(SpeechSpeed, MidpointRounding.AwayFromZero)).ToString();

    public string StartTimeStr
    {
        get => StartTime.ToString(@"hh\:mm\:ss\,fff");
        set => StartTime = ParseTime(value, StartTime);
    }

    public string EndTimeStr
    {
        get => EndTime.ToString(@"hh\:mm\:ss\,fff");
        set => EndTime = ParseTime(value, EndTime);
    }

    public SubtitleItem(int id, TimeSpan startTime, TimeSpan endTime, string text)
    {
        Id = id;
        StartTime = startTime;
        EndTime = endTime;
        Text = text ?? string.Empty;
        SpeechSpeed = CalculateSpeechSpeed(Text, StartTime, EndTime);
    }

    // 起始时间改变,同步改变 StartTimeStr,并重算语速
    partial void OnStartTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(StartTimeStr));
        RecalculateSpeechSpeed();
    }

    // 结束时间改变,同步改变 EndTimeStr,并重算语速
    partial void OnEndTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(EndTimeStr));
        RecalculateSpeechSpeed();
    }

    // 文本改变,并重算语速
    partial void OnTextChanged(string value)
    {
        RecalculateSpeechSpeed();
    }

    // 语速改变,同步改变 SpeechSpeedStr
    partial void OnSpeechSpeedChanged(float value)
    {
        OnPropertyChanged(nameof(SpeechSpeedStr));
    }

    // 重算语速
    private void RecalculateSpeechSpeed()
    {
        SpeechSpeed = CalculateSpeechSpeed(Text, StartTime, EndTime);
    }

    // 解析时间戳
    private static TimeSpan ParseTime(string value, TimeSpan fallback)
    {
        if (TimeSpan.TryParseExact(value, @"hh\:mm\:ss\,fff", null, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static float CalculateSpeechSpeed(string text, TimeSpan startTime, TimeSpan endTime)
    {
        if (string.IsNullOrEmpty(text) || endTime <= startTime)
        {
            return 0f;
        }

        double durationInSeconds = (endTime - startTime).TotalSeconds;
        if (durationInSeconds <= 0)
        {
            return 0f;
        }

        float adjustedLength = 0f;
        foreach (char character in text)
        {
            if (character is '\r' or '\n')
            {
                continue;
            }

            adjustedLength += IsHalfWidthCharacter(character) ? 0.5f : 1f;
        }

        return (float)(adjustedLength / durationInSeconds);
    }

    // 半角符号判定
    private static bool IsHalfWidthCharacter(char character)
    {
        return character <= '\u007F' || (character >= '\uFF61' && character <= '\uFF9F');
    }
}
