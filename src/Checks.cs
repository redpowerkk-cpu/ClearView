using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearView
{
    internal static class Checks
    {
        static BitmapSource Pattern(int width, int height)
        {
            byte[] pixels = new byte[width * height * 3]; uint random = 13579;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3; random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                pixels[i] = (byte)((x * 255L / width + (random & 63)) % 256); pixels[i + 1] = (byte)((y * 255L / height + ((random >> 8) & 63)) % 256); pixels[i + 2] = (byte)(((x / 80 + y / 80) % 2) * 140 + ((random >> 16) & 63));
            }
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3); source.Freeze(); return source;
        }
        static void Encode(string path, BitmapSource image, BitmapEncoder encoder, BitmapMetadata metadata)
        {
            encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        public static int Generate(string folder)
        {
            Directory.CreateDirectory(folder);
            try
            {
                BitmapSource large = Pattern(6000, 4000);
                Encode(Path.Combine(folder, "01-large-24mp.jpg"), large, new JpegBitmapEncoder { QualityLevel = 94 }, null);
                Encode(Path.Combine(folder, "02-large-24mp.png"), large, new PngBitmapEncoder(), null);
                Encode(Path.Combine(folder, "03-large-24mp.tif"), large, new TiffBitmapEncoder { Compression = TiffCompressOption.None }, null);
                var small = Pattern(1200, 800); Encode(Path.Combine(folder, "04-normal.jpg"), small, new JpegBitmapEncoder { QualityLevel = 92 }, null);
                var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
                Encode(Path.Combine(folder, "05-oriented.jpg"), small, new JpegBitmapEncoder { QualityLevel = 92 }, metadata);
                Encode(Path.Combine(folder, "06-firstframe.gif"), small, new GifBitmapEncoder(), null);
                string colorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\drivers\color\sRGB Color Space Profile.icm");
                if (File.Exists(colorPath))
                {
                    var colorEncoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    var contexts = new System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>(new List<ColorContext> { new ColorContext(new Uri(colorPath)) });
                    colorEncoder.Frames.Add(BitmapFrame.Create(small, null, null, contexts));
                    using (var stream = File.Create(Path.Combine(folder, "09-icc-profile.jpg"))) colorEncoder.Save(stream);
                }
                File.Copy(Path.Combine(folder, "04-normal.jpg"), Path.Combine(folder, "한글 사진 07.jpg"), true);
                // 96 MP, 288 MB uncompressed file. Stream rows rather than allocating the entire image.
                int width = 12000, height = 8000, stride = width * 3;
                using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "08-large-96mp.bmp"))))
                {
                    writer.Write((ushort)0x4D42); writer.Write(54 + stride * height); writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)24); writer.Write(0); writer.Write(stride * height); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
                    byte[] row = new byte[stride]; for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { row[x * 3] = (byte)(x * 255 / width); row[x * 3 + 1] = (byte)(y * 255 / height); row[x * 3 + 2] = (byte)((x / 100 + y / 100) % 2 * 200); } writer.Write(row); }
                }
                File.WriteAllText(Path.Combine(folder, "broken.jpg"), "deliberately invalid image"); return 0;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, "generation-error.txt"), e.ToString()); return 1; }
        }
        static bool Wait(Func<bool> test, int timeout) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < timeout) { if (test()) return true; Thread.Sleep(15); } return test(); }
        public static int Run(string folder, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))); var results = new List<object>(); int failures = 0;
            Action<string, bool, object> check = delegate(string name, bool pass, object data) { results.Add(new { name = name, passed = pass, data = data }); if (!pass) failures++; };
            try
            {
                foreach (string path in Directory.GetFiles(folder).Where(p => !p.EndsWith("broken.jpg") && !p.EndsWith(".txt")))
                {
                    var info = ImageCodec.ReadInfo(path); var frame = ImageCodec.Preview(info, 2048);
                    check("decode " + Path.GetFileName(path), frame.Pixels.IsFrozen && Math.Max(frame.Pixels.PixelWidth, frame.Pixels.PixelHeight) <= 2048, new { source = info.Width + "x" + info.Height, fileMB = info.FileBytes / 1048576.0, decodedBytes = frame.Bytes, decodeMs = frame.Milliseconds });
                    using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) check("file unlocked " + Path.GetFileName(path), exclusive.Length > 0, exclusive.Length);
                }
                var oriented = ImageCodec.ReadInfo(Path.Combine(folder, "05-oriented.jpg")); var orientedFrame = ImageCodec.Preview(oriented, 1024);
                check("EXIF orientation", oriented.Width == 800 && oriented.Height == 1200 && orientedFrame.Pixels.PixelWidth < orientedFrame.Pixels.PixelHeight, oriented.Orientation);
                string colorFixture = Path.Combine(folder, "09-icc-profile.jpg");
                if (File.Exists(colorFixture)) { var colorInfo = ImageCodec.ReadInfo(colorFixture); var colorFrame = ImageCodec.Preview(colorInfo, 1200); check("embedded ICC conversion", colorInfo.Color != null && !colorInfo.ColorWarning && colorFrame.Pixels.Format == PixelFormats.Bgra32, colorFrame.Pixels.Format.ToString()); }
                var normalInfo = ImageCodec.ReadInfo(Path.Combine(folder, "04-normal.jpg")); var original = ImageCodec.Preview(normalInfo, 2048);
                var tile = ImageCodec.Detail(normalInfo, new Int32Rect(100, 120, 300, 200), 1);
                int stride = 300 * ((original.Pixels.Format.BitsPerPixel + 7) / 8); byte[] expected = new byte[stride * 200], actual = new byte[stride * 200];
                original.Pixels.CopyPixels(new Int32Rect(100, 120, 300, 200), expected, stride, 0); tile.Pixels.CopyPixels(actual, stride, 0);
                check("100% detail matches original pixels", expected.SequenceEqual(actual), tile.Bytes);
                var largeInfo = ImageCodec.ReadInfo(Path.Combine(folder, "08-large-96mp.bmp")); var largeTile = ImageCodec.Detail(largeInfo, new Int32Rect(5000, 3000, 1200, 800), 1);
                check("96MP viewport detail is bounded", largeTile.Pixels.PixelWidth == 1200 && largeTile.Bytes < 5 * 1024 * 1024, new { bytes = largeTile.Bytes, decodeMs = largeTile.Milliseconds });
                var scaledTile = ImageCodec.Detail(normalInfo, new Int32Rect(0, 0, 1200, 800), 0.5);
                check("detail downsample", scaledTile.Pixels.PixelWidth == 600 && scaledTile.Pixels.PixelHeight == 400, scaledTile.Bytes);
                var cache = new FrameCache(original.Bytes + 100); cache.Put("one", original); cache.Put("two", original);
                check("LRU evicts within byte budget", cache.Used <= cache.Budget && cache.Get("one") == null && cache.Get("two") != null, cache.Used);
                using (var loader = new ImageLoader(24 * 1024 * 1024))
                {
                    long received = -1; PhotoFrame latest = null; int errors = 0;
                    loader.Ready += delegate(long id, PhotoFrame frame) { if (frame.Final) { latest = frame; Interlocked.Exchange(ref received, id); } };
                    loader.Failed += delegate { Interlocked.Increment(ref errors); };
                    long request = loader.Load(normalInfo.Path, 1280, new[] { Path.Combine(folder, "01-large-24mp.jpg") });
                    check("async request completes", Wait(delegate { return Interlocked.Read(ref received) == request; }, 10000), request);
                    request = loader.Load(normalInfo.Path, 1280, new string[0]);
                    check("repeat request uses cache", Wait(delegate { return Interlocked.Read(ref received) == request; }, 10000) && latest.Cached, latest == null ? null : (object)latest.Cached);
                    for (int i = 0; i < 12; i++) request = loader.Load(i % 2 == 0 ? largeInfo.Path : normalInfo.Path, 1280, new string[0]);
                    long final = request;
                    check("rapid navigation latest result wins", Wait(delegate { return Interlocked.Read(ref received) == final; }, 15000) && latest.Info.Path == normalInfo.Path, received);
                    loader.Load(Path.Combine(folder, "broken.jpg"), 1280, new string[0]); check("corrupt image reports error", Wait(delegate { return errors > 0; }, 5000), errors);
                    request = loader.Load(normalInfo.Path, 1280, new string[0]); check("recovers after corrupt image", Wait(delegate { return Interlocked.Read(ref received) == request; }, 5000), request);
                    check("prefetch cache remains bounded", loader.CacheBytes <= 24 * 1024 * 1024, loader.CacheBytes);
                }
            }
            catch (Exception e) { check("unhandled", false, e.ToString()); }
            File.WriteAllText(output, new JavaScriptSerializer().Serialize(new { failures = failures, tests = results })); return failures == 0 ? 0 : 1;
        }
    }
}
