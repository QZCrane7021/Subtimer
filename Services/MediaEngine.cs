using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ManagedBass;

public class MediaEngine
{
    // FFmpeg 上下文
    private unsafe AVFormatContext* _audioFormatCtx; // 音频上下文 - 暂时用不上
    private unsafe AVFormatContext* _videoFormatCtx; // 视频上下文 - 视频处理暂时没写

    // 全量 PCM 数据缓存
    private short[] _pcmData;

    // 取样相关
    private int _sampleRate = 44100;        // 取样率
    private int _channels = 2;              // 双声道    
    private long _currentSampleIndex = 0;   // 目前取样点的索引值

    public short[] PcmData => _pcmData;     // 暴露给其他模块的 PCM 全量缓存
    public double Duration =>               // 倒推总时长
        _pcmData != null ? (double)_pcmData.Length / (_sampleRate * _channels) : 0;   
    
    // BASS 推送相关
    private int _pushStreamHandle;          // BASS 句柄
    private System.Timers.Timer _feedTimer; // BASS 推送流计时器 - 如果剩余缓存时间不足则再推入一段

    // 播放变量
    private double _rangeStartTime = 0;                     // 当前片段的绝对起点时间
    private double _rangeEndTime = 0;                       // 当前片段的绝对终点时间
    private bool _isRangePlaying = false;                   // 当前片段是否在播放
    private int _playEndSyncHandle = 0;                     // 处理播放结束的同步器
    private readonly SyncProcedure _playEndSyncProcedure;   // 播放结束时的同步进程

    // 媒体引擎类构造
    public MediaEngine() {
        _playEndSyncProcedure = new SyncProcedure(OnAudioPlayEndReached);
    }
    
    // 视频帧缓存
    public WriteableBitmap VideoFrameBitmap { get; private set; }

    // 假如用户只导入一个视频文件
    public void LoadMedia(string filePath)
    {
        InitAudio(filePath);    // 让音频部分解这个文件的音频流，存入内存 PCM 并初始化 BASS
        InitVideo(filePath);    // 让视频部分解这个文件的视频流，记录视频流索引、宽高、帧率
    }

    // 假如用户导入了视频，又单独导入了外部音频
    public void LoadExternalAudio(string audioPath)
    {
        InitAudio(audioPath); // 重新初始化音频源，主时钟以新音频为准
    }

    public unsafe void InitAudio(string filePath)
    {
        // 初始化 FFmpeg
        AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();
        ffmpeg.avformat_open_input(&pFormatContext, filePath, null, null);
        ffmpeg.avformat_find_stream_info(pFormatContext, null);

        // 找到音频流索引
        int audioStreamIndex = -1;
        for (int i = 0; i < pFormatContext->nb_streams; i++)
        {
            if (pFormatContext -> streams[i] -> codecpar -> codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                audioStreamIndex = i;
                break;
            }
        }

        // 初始化解码器
        AVCodecParameters* pCodecParams = pFormatContext->streams[audioStreamIndex]->codecpar;
        AVCodec* pCodec = ffmpeg.avcodec_find_decoder(pCodecParams->codec_id);
        AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
        ffmpeg.avcodec_parameters_to_context(pCodecContext, pCodecParams);
        ffmpeg.avcodec_open2(pCodecContext, pCodec, null);

        // 配置重采样器 目标格式为 AV_SAMPLE_FMT_S16 (16bit Signed Integer) 
        SwrContext* swrContext = ffmpeg.swr_alloc();
        ffmpeg.av_opt_set_chlayout(swrContext, "out_chlayout", &pCodecContext->ch_layout, 0); // 保持通道或强制双声道
        ffmpeg.av_opt_set_int(swrContext, "out_sample_rate", _sampleRate, 0);
        ffmpeg.av_opt_set_sample_fmt(swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
        
        ffmpeg.av_opt_set_chlayout(swrContext, "in_chlayout", &pCodecContext->ch_layout, 0);
        ffmpeg.av_opt_set_int(swrContext, "in_sample_rate", pCodecContext->sample_rate, 0);
        ffmpeg.av_opt_set_sample_fmt(swrContext, "in_sample_fmt", pCodecContext->sample_fmt, 0);
        ffmpeg.swr_init(swrContext);

        // 解码循环
        AVPacket* pPacket = ffmpeg.av_packet_alloc();
        AVFrame* pFrame = ffmpeg.av_frame_alloc();
        AVFrame* pSwrFrame = ffmpeg.av_frame_alloc(); // 存储重采样后的帧

        List<short> tempPcmList = new List<short>();

        while (ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
        {
            if (pPacket->stream_index == audioStreamIndex)
            {
                if (ffmpeg.avcodec_send_packet(pCodecContext, pPacket) >= 0)
                {
                    while (ffmpeg.avcodec_receive_frame(pCodecContext, pFrame) >= 0)
                    {
                        // 计算重采样输出样本数并转换 
                        int outSamples = (int)ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(swrContext, pFrame->sample_rate) + pFrame->nb_samples,
                            _sampleRate, pFrame->sample_rate, AVRounding.AV_ROUND_UP);

                        byte* outBuffer;
                        int outLineSize;
                        ffmpeg.av_samples_alloc(&outBuffer, &outLineSize, _channels, outSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
                        
                        int converted = ffmpeg.swr_convert(swrContext, &outBuffer, outSamples, pFrame->extended_data, pFrame->nb_samples);
                        
                        // 将裸数据写入内存缓存 
                        short* pShortBuffer = (short*)outBuffer;
                        for (int k = 0; k < converted * _channels; k++)
                        {
                            tempPcmList.Add(pShortBuffer[k]);
                        }
                        
                        ffmpeg.av_freep(&outBuffer);
                    }
                }
            }
            ffmpeg.av_packet_unref(pPacket);
        }
        
        _pcmData = tempPcmList.ToArray(); // 全量缓存完毕 
        
        // 释放 FFmpeg 内存指针
        ffmpeg.av_frame_free(&pFrame);
        ffmpeg.av_frame_free(&pSwrFrame);
        ffmpeg.avcodec_free_context(&pCodecContext);
        ffmpeg.avformat_close_input(&pFormatContext);
        
        InitBassStream();   // 初始化播放流
    }

    private void InitBassStream()
    {
        Bass.Init(); // 确保音频设备已初始化
        
        _pushStreamHandle = Bass.CreateStream(
            _sampleRate,
            _channels,
            BassFlags.Default,
            StreamProcedureType.Push
        );  // 创建一个空的推送流
        
        _feedTimer = new System.Timers.Timer(50);
        _feedTimer.Elapsed += (s, e) => FeedAudioData();
    }

    private void FeedAudioData()
    {
        if (_pcmData == null || _pushStreamHandle == 0) return;

        //  使用 ManagedBass 自带的 BASSData.Available 枚举，它的底层正是 0x80000000
        int inboundBytes = Bass.ChannelGetData(_pushStreamHandle, IntPtr.Zero, (int)DataFlags.Available);
        if (inboundBytes < 0) inboundBytes = 0; 

        // 如果缓冲区数据少于 100ms 的量，赶快推入新数据
        int minBytesNeeded = _sampleRate * _channels * sizeof(short) / 10;
        if (inboundBytes < minBytesNeeded)
        {
            // 每次喂 200ms 的量
            int samplesToFeed = _sampleRate * _channels / 5; 
            if (_currentSampleIndex + samplesToFeed > _pcmData.Length)
            {
                samplesToFeed = _pcmData.Length - (int)_currentSampleIndex;
            }

            if (samplesToFeed > 0)
            {
                unsafe
                {
                    fixed (short* pData = &_pcmData[_currentSampleIndex])
                    {
                        Bass.StreamPutData(_pushStreamHandle, (IntPtr)pData, samplesToFeed * sizeof(short));
                    }
                }
                _currentSampleIndex += samplesToFeed;
            }
        }
    }

    // 获取当前最高主时钟（秒） 
    public double GetCurrentTime()
    {
        if (_pushStreamHandle == 0) return 0;
        // 注意：在 Push 模式下，用已播放的 Sample 索引换算时间更精准
        return (double)_currentSampleIndex / (_sampleRate * _channels);
    }

    // 点击跳转逻辑 
    public void Seek(double seconds)
    {
        _currentSampleIndex = (long)(seconds * _sampleRate * _channels);
        if (_currentSampleIndex < 0) _currentSampleIndex = 0;
        if (_currentSampleIndex > _pcmData.Length) _currentSampleIndex = _pcmData.Length;

        Bass.ChannelSetPosition(_pushStreamHandle, 0); // 清空 BASS 内部缓冲区，使其瞬间定位 
    }

    // 暂停方法
    public void Pause()
    {
        _isRangePlaying = false; // 清除区间播放状态
        Bass.ChannelPause(_pushStreamHandle);
        _feedTimer.Stop();
    }

    // 播放区间
    public void PlayRange(double startTime, double endTime)
    {
        // 1. 赋值你定义的播放变量
        _rangeStartTime = startTime;
        _rangeEndTime = endTime;
        _isRangePlaying = true;

        // 2. 安全清理上一次可能残留的同步器句柄
        if (_playEndSyncHandle != 0)
        {
            Bass.ChannelRemoveSync(_pushStreamHandle, _playEndSyncHandle);
            _playEndSyncHandle = 0;
        }

        // 3. 停止当前播放并清空 BASS 缓冲区（这会让 _pushStreamHandle 的内部播放字节计数器归零）
        Bass.ChannelStop(_pushStreamHandle); 
        Bass.ChannelSetPosition(_pushStreamHandle, 0);

        // 4. 将你自己的全量 PCM 数组指针，定位到目标开始时间对应的 short 索引位置
        // 使用你声明的 _sampleRate 和 _channels
        _currentSampleIndex = (long)(startTime * _sampleRate * _channels);

        // 根据【持续时间】计算目标截止字节数
        double duration = endTime - startTime;
        long durationBytes = Bass.ChannelSeconds2Bytes(_pushStreamHandle, duration);

        // 6. 注册底层同步器：当播放字节数达到 durationBytes 时，让底层直接刹车
        _playEndSyncHandle = Bass.ChannelSetSync(
            _pushStreamHandle, 
            SyncFlags.Position, 
            durationBytes, 
            _playEndSyncProcedure, 
            IntPtr.Zero
        );

        // 7. 启动喂数据定时器，并开始播放推送流
        _feedTimer?.Start();
        Bass.ChannelPlay(_pushStreamHandle, false);
    }

    // 
    private void OnAudioPlayEndReached(int handle, int channel, int data, IntPtr user)
    {
        // 1. 底层音频线程瞬间急刹车，绝对卡死字尾，没有任何轮询带来的延时
        Bass.ChannelPause(channel);

        // 2. 停掉你的喂数据定时器，防止它在后台继续往已经暂停的流里塞 PCM 数据
        _feedTimer?.Stop();

        // 3. 更新你的状态变量
        _isRangePlaying = false;

        // 4. 如果你需要让 Avalonia UI 界面发生变化（比如让“播放中”的图标变回“暂停”）
        // 记得一定要切回 Avalonia 的 UI 线程去操作
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // 触发你的属性通知或 UI 刷新逻辑，例如：
            // this.RaisePropertyChanged(nameof(IsPlaying));
            System.Diagnostics.Debug.WriteLine($"字幕区间已精准播放完毕，已停止在: {_rangeEndTime} 秒");
        });
    }

    private void InitVideo(string filePath)
    {
        return;
    }
}