using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClearView
{
    internal sealed class PhotoSurface : FrameworkElement
    {
        public PhotoFrame Preview, Detail;
        public bool FitMode = true;
        public double Zoom = 1, PanX, PanY;
        public int Rotation;
        Point last;
        bool dragging;
        public event Action ViewChanged;
        public event Action FullscreenRequested;
        public PhotoSurface() { Focusable = true; ClipToBounds = true; SnapsToDevicePixels = true; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }
        public double DpiScale { get { var source = PresentationSource.FromVisual(this); return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11; } }
        public void SetPreview(PhotoFrame frame)
        {
            bool changed = Preview == null || Preview.Info.Key != frame.Info.Key;
            Preview = frame;
            if (changed) { Detail = null; Rotation = 0; PanX = PanY = 0; FitMode = true; }
            if (FitMode) Refit(); InvalidateVisual();
        }
        public void SetDetail(PhotoFrame frame) { if (Preview != null && Preview.Info.Key == frame.Info.Key) { Detail = frame; InvalidateVisual(); } }
        public void Clear() { Preview = Detail = null; InvalidateVisual(); }
        public void Fit() { FitMode = true; PanX = PanY = 0; Refit(); Changed(); }
        void Refit()
        {
            if (Preview == null || ActualWidth < 1 || ActualHeight < 1) return;
            double width = Rotation % 180 == 0 ? Preview.Info.Width : Preview.Info.Height;
            double height = Rotation % 180 == 0 ? Preview.Info.Height : Preview.Info.Width;
            Zoom = Math.Min(1 / DpiScale, Math.Min(ActualWidth / width, ActualHeight / height)); PanX = PanY = 0;
        }
        public Matrix Mapping()
        {
            Matrix m = Matrix.Identity; if (Preview == null) return m;
            m.Translate(-Preview.Info.Width / 2.0, -Preview.Info.Height / 2.0); m.Rotate(Rotation); m.Scale(Zoom, Zoom); m.Translate(ActualWidth / 2 + PanX, ActualHeight / 2 + PanY); return m;
        }
        public void ZoomAt(double target, Point anchor)
        {
            if (Preview == null) return;
            Matrix inverse = Mapping(); inverse.Invert(); Point sourcePoint = inverse.Transform(anchor);
            FitMode = false; Zoom = Math.Max(0.001, Math.Min(16 / DpiScale, target));
            var relative = new Point(sourcePoint.X - Preview.Info.Width / 2.0, sourcePoint.Y - Preview.Info.Height / 2.0);
            Matrix transform = Matrix.Identity; transform.Rotate(Rotation); transform.Scale(Zoom, Zoom); relative = transform.Transform(relative);
            PanX = anchor.X - ActualWidth / 2 - relative.X; PanY = anchor.Y - ActualHeight / 2 - relative.Y;
            Constrain(); Changed();
        }
        public void ActualSize() { ZoomAt(1 / DpiScale, new Point(ActualWidth / 2, ActualHeight / 2)); }
        public void Rotate() { Rotation = (Rotation + 90) % 360; if (FitMode) Refit(); else Constrain(); Changed(); }
        void Constrain()
        {
            if (Preview == null) return;
            double width = Rotation % 180 == 0 ? Preview.Info.Width : Preview.Info.Height, height = Rotation % 180 == 0 ? Preview.Info.Height : Preview.Info.Width;
            double x = Math.Max(0, (width * Zoom - ActualWidth) / 2), y = Math.Max(0, (height * Zoom - ActualHeight) / 2);
            PanX = Math.Max(-x, Math.Min(x, PanX)); PanY = Math.Max(-y, Math.Min(y, PanY));
        }
        public Int32Rect VisibleRegion()
        {
            if (Preview == null) return Int32Rect.Empty;
            Matrix inverse = Mapping(); inverse.Invert();
            Point[] points = { inverse.Transform(new Point(0, 0)), inverse.Transform(new Point(ActualWidth, 0)), inverse.Transform(new Point(0, ActualHeight)), inverse.Transform(new Point(ActualWidth, ActualHeight)) };
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (Point p in points) { left = Math.Min(left, p.X); top = Math.Min(top, p.Y); right = Math.Max(right, p.X); bottom = Math.Max(bottom, p.Y); }
            int x = Math.Max(0, (int)Math.Floor(left) - 24), y = Math.Max(0, (int)Math.Floor(top) - 24);
            int r = Math.Min(Preview.Info.Width, (int)Math.Ceiling(right) + 24), b = Math.Min(Preview.Info.Height, (int)Math.Ceiling(bottom) + 24);
            return new Int32Rect(x, y, Math.Max(0, r - x), Math.Max(0, b - y));
        }
        public bool NeedsDetail { get { return Preview != null && Preview.Final && Zoom * DpiScale > (double)Preview.Pixels.PixelWidth / Preview.Info.Width * 1.12; } }
        void Changed() { InvalidateVisual(); if (ViewChanged != null) ViewChanged(); }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 16, 18)), null, new Rect(RenderSize));
            if (Preview == null) return;
            dc.PushTransform(new MatrixTransform(Mapping()));
            var bounds = new Rect(0, 0, Preview.Info.Width, Preview.Info.Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(36, 40, 42)), null, bounds);
            dc.DrawImage(Preview.Pixels, bounds);
            if (Detail != null && Detail.Info.Key == Preview.Info.Key) dc.DrawImage(Detail.Pixels, Detail.Region);
            dc.Pop();
        }
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); if (FitMode) Refit(); else Constrain(); Changed(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e) { Focus(); ZoomAt(Zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), e.GetPosition(this)); e.Handled = true; }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus(); if (e.ClickCount == 2) { if (FullscreenRequested != null) FullscreenRequested(); return; }
            if (Preview == null) return; dragging = true; last = e.GetPosition(this); CaptureMouse(); Cursor = Cursors.SizeAll; e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return; Point now = e.GetPosition(this); PanX += now.X - last.X; PanY += now.Y - last.Y; last = now; Constrain(); Changed();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { dragging = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow; Changed(); }
    }
}
