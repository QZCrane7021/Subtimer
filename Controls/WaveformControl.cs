using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using System;

namespace Subtimer.Controls
{
    public class WaveformControl : Control
    {
        // MediaEngine 绑定属性
        public static readonly StyledProperty<MediaEngine> MediaEngineProperty =
            AvaloniaProperty.Register<WaveformControl, MediaEngine>(nameof(MediaEngine));

        public MediaEngine MediaEngine
        {
            get => GetValue(MediaEngineProperty);
            set => SetValue(MediaEngineProperty, value);
        }

        // 每秒像素数，用于控制时间轴的横向缩放比例
        public static readonly StyledProperty<double> PixelsPerSecondProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(PixelsPerSecond), 100.0);

        public double PixelsPerSecond
        {
            get => GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }

        // 视图滚动偏移量，配合 ScrollBar 使用，决定当前屏幕展示哪一秒
        public static readonly StyledProperty<double> HorizontalOffsetProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(HorizontalOffset), 0.0);
        public double HorizontalOffset
        {
            get => GetValue(HorizontalOffsetProperty);
            set => SetValue(HorizontalOffsetProperty, value);
        }

        // 标记鼠标是否正在拖拽 Seek
        private bool _isDragging = false; 

        static WaveformControl()
        {
            // 当这些属性发生变化时，触发 Render 重绘
            AffectsRender<WaveformControl>(MediaEngineProperty, PixelsPerSecondProperty, HorizontalOffsetProperty);
        }

        public WaveformControl()
        {
            // 限制绘制区域不超出控件边界
            ClipToBounds = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            
            var pointerPoint = e.GetCurrentPoint(this);
            if (pointerPoint.Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                e.Pointer.Capture(this); // 捕获鼠标，即便移出控件范围也能继续拖拽
                
                HandleMouseSeek(e.GetPosition(this).X);
            }
        }


        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            
            if (_isDragging)
            {
                HandleMouseSeek(e.GetPosition(this).X);
            }
        }


        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null); // 释放鼠标捕获
            }
        }


        private void HandleMouseSeek(double mouseX)
        {
            if (MediaEngine == null || PixelsPerSecond <= 0) return;

            // 核心公式：时间(秒) = (鼠标当前X坐标 + 滚动条隐藏的像素) / 每秒像素数
            double targetSeconds = (mouseX + HorizontalOffset) / PixelsPerSecond;

            // 边界限幅
            if (targetSeconds < 0) targetSeconds = 0;
            if (targetSeconds > MediaEngine.Duration) targetSeconds = MediaEngine.Duration;

            // 调用你的二合一引擎跳转（此时音频、视频都会响应这个 Seek）
            MediaEngine.Seek(targetSeconds);
            
            // 局部强制重绘（更新播放头红线）
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double width = Bounds.Width;
            double height = Bounds.Height;

            if (width <= 0 || height <= 0) return;

            // 如果没有加载引擎或者没有音频裸数据，绘制空白占位提示
            if (MediaEngine == null || MediaEngine.PcmData == null)
            {
                RenderEmptyHint(context, width, height);
                return;
            }

            // 绘制波形图/频谱图（你之前写的离屏 WriteableBitmap 绘制部分）
            Avalonia.Media.Imaging.WriteableBitmap bitmap = GetWaveformBitmap((int)width, (int)height);

            if (bitmap != null)
            {
                var srcRect = new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height);
                var destRect = new Rect(0, 0, width, height);
                
                context.DrawImage(bitmap, srcRect, destRect);
            }

            DrawPlaybackCursor(context, height);
        }

        // 绘制播放指示红线
        private void DrawPlaybackCursor(DrawingContext context, double height)
        {
            if (MediaEngine == null) return;

            // 获取当前回放时间戳
            double currentTime = MediaEngine.GetCurrentTime();
            
            // 计算红线在屏幕上的 X 坐标
            double cursorX = (currentTime * PixelsPerSecond) - HorizontalOffset;

            // 只有当红线在屏幕可见视野内时才绘制
            if (cursorX >= 0 && cursorX <= Bounds.Width)
            {
                var pen = new Pen(Brushes.Red, 1.5); // 1.5 像素粗细的红线
                context.DrawLine(pen, new Point(cursorX, 0), new Point(cursorX, height));
            }
        }

        // 未加载文件时的友好提示
        private void RenderEmptyHint(DrawingContext context, double width, double height)
        {
            var text = new FormattedText(
                "请在软件中导入音视频媒体文件...",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                14,
                Brushes.Gray);

            // 居中绘制文本
            var origin = new Point((width - text.Width) / 2, (height - text.Height) / 2);
            context.DrawText(text, origin);
        }

        // 占位方法：需要将通过 PCM/FFT 填充 WriteableBitmap 的生成逻辑塞进这里
        private Avalonia.Media.Imaging.WriteableBitmap GetWaveformBitmap(int width, int height)
        {
            // TODO: 返回你绘制好波形/频谱的 WriteableBitmap 实例
            return null; 
        }
    }
}