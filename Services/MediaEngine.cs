using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ManagedBass;

public unsafe class MediaEngine : IDisposable
{
    // ==========================================
    // 1. FFmpeg & BASS 音频部分 (保留你原有的设计)
    // ==========================================
    private AVFormatContext* _audioFormatCtx; 
    private short[] _pcmData;
    private int _sampleRate = 44100;        
    private int _channels = 2;              
    private long _currentSampleIndex = 0;   
    public short[] PcmData => _pcmData;     
    public double Duration => _pcmData != null ? (double)_pcmData.Length / (_sampleRate * _channels) : 0;   
    
    private int _pushStreamHandle;          
    private System.Timers.Timer _feedTimer; 

    private double _rangeStartTime = 0;                     
    private double _rangeEndTime = 0;                       
    private bool _isRangePlaying = false;                   
    private int _playEndSyncHandle = 0;                     
    private readonly SyncProcedure _playEndSyncProcedure;

    // ==========================================
    // 2. 新增：视频部分变量与属性
    // ==========================================
    private AVFormatContext* _videoFormatCtx; // 视频上下文
    private AVCodecContext* _videoCodecCtx;   // 视频解码器上下文
    private AVFrame* _videoFrame;             // 解码用的 YUV 原始帧
    private AVPacket* _videoPacket;           // 视频数据包
    private SwsContext* _swsCtx;              // 像素格式转换上下文（YUV -> BGRA）
    
    private int _videoStreamIndex = -1;
    private double _videoTimeBase = 0.0;
    private double _lastRenderedTime = -1.0;  // 记录上一帧渲染的时间，用于区分“顺序播放”还是“跳转Seek”

    // 暴露给 Avalonia UI 绑定的属性
    public WriteableBitmap? VideoSource { get; private set; }
    public int VideoWidth => _videoCodecCtx != null ? _videoCodecCtx->width : 0;
    public int VideoHeight => _videoCodecCtx != null ? _videoCodecCtx->height : 0;
    public double VideoDuration { get; private set; }

    // 音视频同步核心：UI 线程定时器，播放时驱动视频画面更新
    private DispatcherTimer? _videoSyncTimer;

    public event Action? VideoFrameUpdated; // 通知 UI 视频帧刷新的事件

    public MediaEngine()
    {
        // 初始化 BASS
        Bass.Init();
        
        // 绑定音频停止回调
        _playEndSyncProcedure = OnAudioPlayEndReached;

        // 初始化音视频同步定时器 (15毫秒间隔，约 60 帧)
        _videoSyncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _videoSyncTimer.Tick += OnVideoSyncTick;
    }

    // ==========================================
    // 3. 新增：视频导入与初始化
    // ==========================================
    public void InitVideo(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("视频文件未找到", filePath);

        // 清理旧视频
        DisposeVideo();

        fixed (AVFormatContext** pFormatCtx = &_videoFormatCtx)
        {
            if (ffmpeg.avformat_open_input(pFormatCtx, filePath, null, null) != 0)
                throw new Exception("无法打开视频文件");
        }

        if (ffmpeg.avformat_find_stream_info(_videoFormatCtx, null) < 0)
            throw new Exception("无法获取视频流信息");

        // 寻找视频流
        for (int i = 0; i < _videoFormatCtx->nb_streams; i++)
        {
            if (_videoFormatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                _videoStreamIndex = i;
                break;
            }
        }

        if (_videoStreamIndex == -1) throw new Exception("未找到视频流");

        var stream = _videoFormatCtx->streams[_videoStreamIndex];
        _videoTimeBase = ffmpeg.av_q2d(stream->time_base);
        VideoDuration = stream->duration * _videoTimeBase;

        // 配置解码器
        var pCodec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
        if (pCodec == null) throw new Exception("未找到视频解码器");

        _videoCodecCtx = ffmpeg.avcodec_alloc_context3(pCodec);
        ffmpeg.avcodec_parameters_to_context(_videoCodecCtx, stream->codecpar);

        if (ffmpeg.avcodec_open2(_videoCodecCtx, pCodec, null) < 0)
            throw new Exception("无法打开视频解码器");

        // 分配帧和包
        _videoFrame = ffmpeg.av_frame_alloc();
        _videoPacket = ffmpeg.av_packet_alloc();

        // 🔥 关键：在 UI 线程创建对应尺寸的 WriteableBitmap 传给 UI
        Dispatcher.UIThread.Invoke(() =>
        {
            VideoSource = new WriteableBitmap(
                new PixelSize(_videoCodecCtx->width, _videoCodecCtx->height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        });

        // 默认定位到 0 秒渲染首帧
        UpdateVideoFrame(0.0);
    }

    // ==========================================
    // 4. 新增：高精度 Seek 轴与实时渲染核心
    // ==========================================
    public void UpdateVideoFrame(double seconds)
    {
        if (_videoFormatCtx == null || _videoCodecCtx == null || VideoSource == null) return;

        // 【优化算法】：判断是“连续播放”还是“手动跳跃改轴”
        double diff = seconds - _lastRenderedTime;
        if (diff > 0 && diff < 1.0)
        {
            // A. 连续播放状态：直接向后解码下一帧，性能极佳
            DecodeNextFrame(seconds);
        }
        else
        {
            // B. 拖拽跳转状态：执行精准 Seek
            SeekVideoFrame(seconds);
        }

        _lastRenderedTime = seconds;
    }

    /// <summary>
    /// 精准定位视频帧（Seek 关键帧并向后推演）
    /// </summary>
    private bool SeekVideoFrame(double seconds)
    {
        long targetPts = (long)(seconds / _videoTimeBase);

        // 向前寻找最近的 I 帧（关键帧）
        if (ffmpeg.av_seek_frame(_videoFormatCtx, _videoStreamIndex, targetPts, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0)
            return false;

        ffmpeg.avcodec_flush_buffers(_videoCodecCtx);

        bool frameFound = false;
        int maxFramesToDecode = 300; // 防止脏包死循环的阻断计数器

        while (maxFramesToDecode-- > 0 && ffmpeg.av_read_frame(_videoFormatCtx, _videoPacket) >= 0)
        {
            if (_videoPacket->stream_index == _videoStreamIndex)
            {
                if (ffmpeg.avcodec_send_packet(_videoCodecCtx, _videoPacket) >= 0)
                {
                    while (ffmpeg.avcodec_receive_frame(_videoCodecCtx, _videoFrame) >= 0)
                    {
                        // 追赶到了目标时间戳
                        if (_videoFrame->pts >= targetPts)
                        {
                            WriteFrameToBitmap(_videoFrame);
                            frameFound = true;
                            break;
                        }
                    }
                }
            }
            ffmpeg.av_packet_unref(_videoPacket);
            if (frameFound) break;
        }
        return frameFound;
    }

    /// <summary>
    /// 连续向后解码单帧（用于流畅播放）
    /// </summary>
    private bool DecodeNextFrame(double seconds)
    {
        long targetPts = (long)(seconds / _videoTimeBase);
        bool frameFound = false;

        while (ffmpeg.av_read_frame(_videoFormatCtx, _videoPacket) >= 0)
        {
            if (_videoPacket->stream_index == _videoStreamIndex)
            {
                if (ffmpeg.avcodec_send_packet(_videoCodecCtx, _videoPacket) >= 0)
                {
                    if (ffmpeg.avcodec_receive_frame(_videoCodecCtx, _videoFrame) >= 0)
                    {
                        if (_videoFrame->pts >= targetPts)
                        {
                            WriteFrameToBitmap(_videoFrame);
                            frameFound = true;
                        }
                    }
                }
            }
            ffmpeg.av_packet_unref(_videoPacket);
            if (frameFound) break;
        }
        return frameFound;
    }

    /// <summary>
    /// 零拷贝：直接从 YUV 原始帧写入 Avalonia WriteableBitmap 显存
    /// </summary>
    private void WriteFrameToBitmap(AVFrame* srcFrame)
    {
        if (VideoSource == null) return;

        using (var lockedBitmap = VideoSource.Lock())
        {
            _swsCtx = ffmpeg.sws_getCachedContext(
                _swsCtx,
                srcFrame->width, srcFrame->height, (AVPixelFormat)srcFrame->format,
                lockedBitmap.Size.Width, lockedBitmap.Size.Height, AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)SwsFlags.SWS_FAST_BILINEAR, null, null, null);

            byte* dstData = (byte*)lockedBitmap.Address;
            int dstRowBytes = lockedBitmap.RowBytes;

            byte*[] dstDataArray = { dstData, null, null, null };
            int[] dstStrideArray = { dstRowBytes, 0, 0, 0 };

            ffmpeg.sws_scale(
                _swsCtx,
                srcFrame->data, srcFrame->linesize, 0, srcFrame->height,
                dstDataArray, dstStrideArray);
        }

        // 通知绑定的控件进行重绘
        VideoFrameUpdated?.Invoke();
    }

    // ==========================================
    // 5. 联动：音视频同步（AV Sync）
    // ==========================================
    private void OnVideoSyncTick(object? sender, EventArgs e)
    {
        if (!_isRangePlaying) return;

        // 以音频的主播放时钟（BASS 播放出的字节进度）为标准
        long currentBytePos = Bass.ChannelGetPosition(_pushStreamHandle);
        double playedSeconds = Bass.ChannelBytes2Seconds(_pushStreamHandle, currentBytePos);

        // 最终的真实时间 = 当前时间片的起点 + 已经播放的时长
        double absoluteTime = _rangeStartTime + playedSeconds;

        // 让视频帧立刻追上该时间点
        UpdateVideoFrame(absoluteTime);
    }

    // 劫持你原有的 PlayRange，加入视频定时器的启动
    public void PlayRange(double startTime, double endTime)
    {
        _isRangePlaying = true;
        _rangeStartTime = startTime;
        _rangeEndTime = endTime;

        if (_playEndSyncHandle != 0)
        {
            Bass.ChannelRemoveSync(_pushStreamHandle, _playEndSyncHandle);
            _playEndSyncHandle = 0;
        }

        Bass.ChannelStop(_pushStreamHandle); 
        Bass.ChannelSetPosition(_pushStreamHandle, 0);

        _currentSampleIndex = (long)(startTime * _sampleRate * _channels);

        double duration = endTime - startTime;
        long durationBytes = Bass.ChannelSeconds2Bytes(_pushStreamHandle, duration);

        _playEndSyncHandle = Bass.ChannelSetSync(
            _pushStreamHandle, 
            SyncFlags.Position, 
            durationBytes, 
            _playEndSyncProcedure, 
            IntPtr.Zero
        );

        _feedTimer?.Start();
        
        // 🔥 开启音视频实时同步
        _videoSyncTimer?.Start(); 
        
        Bass.ChannelPlay(_pushStreamHandle, false);
    }

    private void OnAudioPlayEndReached(int handle, int channel, int data, IntPtr user)
    {
        Bass.ChannelPause(channel);
        _feedTimer?.Stop();

        // 🔥 暂停视频同步，并让视频停在终点时间
        _videoSyncTimer?.Stop();
        Dispatcher.UIThread.Post(() => {
            UpdateVideoFrame(_rangeEndTime);
        });

        _isRangePlaying = false;
    }

    // ==========================================
    // 6. 销毁与垃圾回收
    // ==========================================
    public void DisposeVideo()
    {
        _videoSyncTimer?.Stop();
        if (_swsCtx != null) ffmpeg.sws_freeContext(_swsCtx);
        
        fixed (AVFrame** p = &_videoFrame) ffmpeg.av_frame_free(p);
        fixed (AVPacket** p = &_videoPacket) ffmpeg.av_packet_free(p);
        fixed (AVCodecContext** p = &_videoCodecCtx) ffmpeg.avcodec_free_context(p);
        fixed (AVFormatContext** p = &_videoFormatCtx)
        {
            var ctx = _videoFormatCtx;
            ffmpeg.avformat_close_input(&ctx);
            _videoFormatCtx = null;
        }
    }

    public void Dispose()
    {
        DisposeVideo();
        _feedTimer?.Dispose();
        Bass.Free();
    }

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
}