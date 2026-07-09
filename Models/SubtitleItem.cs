using System;

namespace Subtimer.Models;

public class SubtitleItem
{
    public int Id {get; set;}
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Text { get; set; }
    public string StartTimeStr => StartTime.ToString(@"hh\:mm\:ss\.ff");
    public string EndTimeStr => EndTime.ToString(@"hh\:mm\:ss\.ff");

    public SubtitleItem(int id, TimeSpan startTime, TimeSpan endTime, string text)
    {
        Id = id;
        StartTime = startTime;
        EndTime = endTime;
        Text = text;
    }
}