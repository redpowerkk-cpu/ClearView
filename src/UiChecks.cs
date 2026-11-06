using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearView
{
    internal sealed partial class MainWindow
    {
        async Task<bool> Until(Func<bool> test, int timeout) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < timeout) { if (test()) return true; await Task.Delay(25); } return test(); }
        void Capture(string path)
        {
            UpdateLayout(); var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        public async Task RunUiChecks(string folder, string output)
        {
            Directory.CreateDirectory(output); var results = new List<object>(); int failures = 0; int heartbeats = 0;
            Action<string, bool, object> check = delegate(string name, bool pass, object data) { results.Add(new { name = name, passed = pass, data = data }); if (!pass) failures++; };
            var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; heartbeat.Tick += delegate { heartbeats++; }; heartbeat.Start();
            try
            {
                await Task.Delay(100); Capture(Path.Combine(output, "01-empty.png"));
                using (var p = Process.GetCurrentProcess()) { p.Refresh(); check("idle memory observation", true, p.WorkingSet64 / 1048576.0); }
                files.Clear(); files.AddRange(new[] { Path.Combine(folder, "01-large-24mp.jpg"), Path.Combine(folder, "02-large-24mp.png"), Path.Combine(folder, "08-large-96mp.bmp") });
                var watch = Stopwatch.StartNew(); Navigate(0);
                check("24MP JPEG first pixels", await Until(delegate { return surface.Preview != null; }, 15000), watch.Elapsed.TotalMilliseconds);
                check("screen preview completes", await Until(delegate { return !loading; }, 15000), watch.Elapsed.TotalMilliseconds); Capture(Path.Combine(output, "02-photo.png"));
                surface.ActualSize(); check("100% detail resolves", await Until(delegate { return surface.Detail != null; }, 15000), surface.Detail == null ? 0 : surface.Detail.Bytes);
                check("100% physical pixel scale", Math.Abs(surface.Zoom * surface.DpiScale - 1) < 0.001, surface.Zoom);
                surface.Rotate(); check("view-only rotation", surface.Rotation == 90, surface.Rotation); surface.Fit();
                ToggleFullscreen(); check("fullscreen", full && WindowStyle == WindowStyle.None, surface.ActualWidth); ToggleFullscreen(); check("restore window", !full && WindowStyle == WindowStyle.SingleBorderWindow, Width);
                Width = 820; Height = 500; await Task.Delay(100); Capture(Path.Combine(output, "03-compact.png")); Width = 1120; Height = 760;
                int before = heartbeats; watch.Restart(); Navigate(2); check("96MP BMP displays", await Until(delegate { return surface.Preview != null && surface.Preview.Info.Path == requested && !loading; }, 20000), watch.Elapsed.TotalMilliseconds);
                check("UI pumps during large decode", heartbeats > before, heartbeats - before);
                surface.ActualSize(); check("96MP viewport detail resolves", await Until(delegate { return surface.Detail != null && surface.Detail.Info.Path == requested; }, 15000), surface.Detail == null ? 0 : surface.Detail.Bytes);
                Capture(Path.Combine(output, "04-detail.png"));
                for (int i = 0; i < 10; i++) Navigate(i % 3); Navigate(1);
                check("rapid navigation final photo", await Until(delegate { return surface.Preview != null && surface.Preview.Info.Path == files[1] && !loading; }, 20000), filename.Text);
                check("preview cache budget", loader.CacheBytes <= 128L * 1024 * 1024, loader.CacheBytes);
                using (var p = Process.GetCurrentProcess()) { p.Refresh(); check("post decode memory observation", true, p.WorkingSet64 / 1048576.0); }
            }
            catch (Exception e) { check("unhandled", false, e.ToString()); }
            heartbeat.Stop(); File.WriteAllText(Path.Combine(output, "results.json"), new JavaScriptSerializer().Serialize(new { failures = failures, tests = results })); Close();
        }
    }
}
