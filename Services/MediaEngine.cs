using Avalonia.Media.Imaging;
using FFmpeg.AutoGen;
using ManagedBass;

public class MediaEngine
{

    // 底层私有成员：各自的 FFmpeg 上下文
    private unsafe AVFormatContext* _audioFormatCtx;
    private unsafe AVFormatContext* _videoFormatCtx;

    // 全量 PCM 数据缓存，使用 short (16bit) 存储 
    private short[] _pcmData;
    private int _sampleRate = 44100;
    private int _channels = 2;
    
    // BASS 播放相关 
    private System.Timers.Timer _feedTimer;
    private long _currentSampleIndex = 0;

    public short[] PcmData => _pcmData;
    public double Duration => _pcmData != null ? (double)_pcmData.Length / (_sampleRate * _channels) : 0;
    
    // 统一的主时钟
    private int _pushStreamHandle; // BASS 推送流

    // 播放变量
    private double _rangeEndTime = 0;
    private bool _isRangePlaying = false;
    
    // 视频帧缓存（给界面渲染用）
    public WriteableBitmap VideoFrameBitmap { get; private set; }

    // 场景 A：用户只导入一个视频文件（音视频同源）
    public void LoadMedia(string filePath)
    {
        // 1. 让音频部分去解这个文件的音频流，存入内存 PCM 并初始化 BASS
        InitAudio(filePath);
        
        // 2. 让视频部分去解这个文件的视频流，记录视频流索引、宽高、帧率
        InitVideo(filePath);
    }

    // 场景 B：用户导入了视频，又单独导入了外部音频（Aegisub 经典操作）
    public void LoadExternalAudio(string audioPath)
    {
        InitAudio(audioPath); // 重新初始化音频源，主时钟以新音频为准
    }

    public unsafe void InitAudio(string filePath)
    {
        // 1. 初始化 FFmpeg 打开文件
        AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();
        ffmpeg.avformat_open_input(&pFormatContext, filePath, null, null);
        ffmpeg.avformat_find_stream_info(pFormatContext, null);

        // 找到音频流索引
        int audioStreamIndex = -1;
        for (int i = 0; i < pFormatContext->nb_streams; i++)
        {
            // 媒体的总指针 ->
            if (pFormatContext -> streams[i] -> codecpar -> codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                audioStreamIndex = i;
                break;
            }
        }

        // 2. 初始化解码器
        AVCodecParameters* pCodecParams = pFormatContext->streams[audioStreamIndex]->codecpar;
        AVCodec* pCodec = ffmpeg.avcodec_find_decoder(pCodecParams->codec_id);
        AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
        ffmpeg.avcodec_parameters_to_context(pCodecContext, pCodecParams);
        ffmpeg.avcodec_open2(pCodecContext, pCodec, null);

        // 3. 配置重采样器 (SwrContext) 
        // 目标格式：AV_SAMPLE_FMT_S16 (16bit Signed Integer) 
        SwrContext* swrContext = ffmpeg.swr_alloc();
        ffmpeg.av_opt_set_chlayout(swrContext, "out_chlayout", &pCodecContext->ch_layout, 0); // 保持通道或强制双声道
        ffmpeg.av_opt_set_int(swrContext, "out_sample_rate", _sampleRate, 0);
        ffmpeg.av_opt_set_sample_fmt(swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
        
        ffmpeg.av_opt_set_chlayout(swrContext, "in_chlayout", &pCodecContext->ch_layout, 0);
        ffmpeg.av_opt_set_int(swrContext, "in_sample_rate", pCodecContext->sample_rate, 0);
        ffmpeg.av_opt_set_sample_fmt(swrContext, "in_sample_fmt", pCodecContext->sample_fmt, 0);
        ffmpeg.swr_init(swrContext);

        // 4. 解码循环
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
        
        // 初始化播放流
        InitBassStream();
    }

    private void InitBassStream()
    {
        Bass.Init(); // 确保音频设备已初始化
        
        // 创建一个空的推送流 
        _pushStreamHandle = Bass.CreateStream(_sampleRate, _channels, BassFlags.Default, StreamProcedureType.Push);
        
        // 开启定时器，每 1ms 检查并喂一次数据
        _feedTimer = new System.Timers.Timer(1);
        _feedTimer.Elapsed += (s, e) => FeedAudioData();
        _feedTimer.Start();
        
        Bass.ChannelPlay(_pushStreamHandle);
    }

    private void FeedAudioData()
    {
        if (_isRangePlaying && GetCurrentTime() >= _rangeEndTime)
        {
            // 必须在主线程或异步安全的上下文执行暂停
            Pause(); 
            return;
        }

        if (_pcmData == null || _pushStreamHandle == 0) return;
        // 检查 BASS 推流缓冲区可用字节数（使用原始常量，避免未定义的 BASSData）
        const int BASS_DATA_AVAILABLE = 0x2000000; // BASS_DATA_AVAILABLE
        int inboundBytes = Bass.ChannelGetData(_pushStreamHandle, IntPtr.Zero, BASS_DATA_AVAILABLE);
        if (inboundBytes < 0) inboundBytes = 0; // 保险处理，避免负值

        // 如果缓冲区数据少于 100ms 的量，赶快推入新数据
        int minBytesNeeded = _sampleRate * _channels * sizeof(short) / 10;
        if (inboundBytes < minBytesNeeded)
        {
            int samplesToFeed = _sampleRate * _channels / 5; // 每次喂 200ms 的量（单位：short 样本数）
            if (_currentSampleIndex + samplesToFeed > _pcmData.Length)
            {
                samplesToFeed = _pcmData.Length - (int)_currentSampleIndex;
            }

            if (samplesToFeed > 0)
            {
                // 从全量内存中截取一段推送给 BASS 
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
        if (endTime <= startTime) return;

        if (_isRangePlaying)
        {
            Pause();
        }

        // 跳转到开始时间
        this.Seek(startTime);

        // 锁定结束时间与状态
        _rangeEndTime = endTime;
        _isRangePlaying = true;

        // 启动回放（确保 BASS 开始播放，定时器开始工作）
        Bass.ChannelPlay(_pushStreamHandle);
        if (!_feedTimer.Enabled)
        {
            _feedTimer.Start();
        }
    }

    private void InitVideo(string filePath)
    {
        return;
    }
}