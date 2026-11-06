using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearView
{
    internal sealed class PhotoInfo
    {
        public string Path, Key, Format;
        public int Width, Height, Orientation, Frames;
        public long FileBytes;
        public ColorContext Color;
        public bool ColorWarning;
    }
    internal sealed class PhotoFrame
    {
        public PhotoInfo Info;
        public BitmapSource Pixels;
        public Rect Region;
        public bool IsDetail, Cached, Final;
        public double Milliseconds;
        public long Bytes { get { return (long)Pixels.PixelWidth * Pixels.PixelHeight * Math.Max(4, (Pixels.Format.BitsPerPixel + 7) / 8); } }
    }
    internal static class ImageCodec
    {
        public static PhotoInfo ReadInfo(string path)
        {
            FileInfo file = new FileInfo(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                var frame = decoder.Frames[0]; int orientation = 1;
                var metadata = frame.Metadata as BitmapMetadata;
                if (metadata != null)
                {
                    foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                    {
                        try { object v = metadata.GetQuery(query); if (v != null) { orientation = Convert.ToInt32(v); break; } } catch (NotSupportedException) { } catch (ArgumentException) { }
                    }
                }
                if (orientation < 1 || orientation > 8) orientation = 1;
                int width = frame.PixelWidth, height = frame.PixelHeight;
                if (width < 1 || height < 1 || (long)width * height > 600000000) throw new NotSupportedException("이미지 크기가 안전한 처리 범위를 벗어났습니다.");
                return new PhotoInfo { Path = path, Key = path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks, FileBytes = file.Length,
                    Width = orientation >= 5 ? height : width, Height = orientation >= 5 ? width : height, Orientation = orientation,
                    Frames = decoder.Frames.Count, Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
                    Color = frame.ColorContexts != null && frame.ColorContexts.Count > 0 ? frame.ColorContexts[0] : null };
            }
        }
        static BitmapSource Orient(BitmapSource pixels, int orientation)
        {
            var group = new TransformGroup();
            switch (orientation)
            {
                case 2: group.Children.Add(new ScaleTransform(-1, 1)); break;
                case 3: group.Children.Add(new RotateTransform(180)); break;
                case 4: group.Children.Add(new ScaleTransform(1, -1)); break;
                case 5: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(270)); break;
                case 6: group.Children.Add(new RotateTransform(90)); break;
                case 7: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(90)); break;
                case 8: group.Children.Add(new RotateTransform(270)); break;
            }
            return group.Children.Count == 0 ? pixels : new TransformedBitmap(pixels, group);
        }
        static BitmapSource Colorize(BitmapSource pixels, PhotoInfo info)
        {
            if (info.Color != null)
            {
                try { return new ColorConvertedBitmap(pixels, info.Color, new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32); }
                catch (NotSupportedException) { info.ColorWarning = true; } catch (ArgumentException) { info.ColorWarning = true; }
            }
            return pixels;
        }
        public static PhotoFrame Preview(PhotoInfo info, int edge)
        {
            var watch = Stopwatch.StartNew();
            using (var stream = new FileStream(info.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // StreamSource creates a fresh decode; IgnoreImageCache expects a URI on .NET Framework.
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = stream;
                int rawWidth = info.Orientation >= 5 ? info.Height : info.Width, rawHeight = info.Orientation >= 5 ? info.Width : info.Height;
                if (Math.Max(rawWidth, rawHeight) > edge)
                {
                    if (rawWidth >= rawHeight) bitmap.DecodePixelWidth = edge; else bitmap.DecodePixelHeight = edge;
                }
                bitmap.EndInit();
                BitmapSource source = Orient(Colorize(bitmap, info), info.Orientation);
                // Materialize before closing the stream; workers only publish frozen pixels.
                var pixels = new WriteableBitmap(source); pixels.Freeze();
                return new PhotoFrame { Info = info, Pixels = pixels, Region = new Rect(0, 0, info.Width, info.Height), Final = true, Milliseconds = watch.Elapsed.TotalMilliseconds };
            }
        }
        public static PhotoFrame Detail(PhotoInfo info, Int32Rect region, double scale)
        {
            var watch = Stopwatch.StartNew();
            scale = Math.Max(0.001, Math.Min(1, scale));
            if (region.Width < 1 || region.Height < 1 || (double)region.Width * region.Height * scale * scale > 16000000) throw new ArgumentException("확대 영역이 너무 큽니다.");
            using (var stream = new FileStream(info.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                BitmapSource source = Orient(Colorize(decoder.Frames[0], info), info.Orientation);
                source = new CroppedBitmap(source, region);
                if (scale < 0.99) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                var pixels = new WriteableBitmap(source); pixels.Freeze();
                return new PhotoFrame { Info = info, Pixels = pixels, Region = new Rect(region.X, region.Y, region.Width, region.Height), IsDetail = true, Final = true, Milliseconds = watch.Elapsed.TotalMilliseconds };
            }
        }
    }
    internal sealed class FrameCache
    {
        sealed class Entry { public string Key; public PhotoFrame Frame; }
        readonly object gate = new object();
        readonly LinkedList<Entry> lru = new LinkedList<Entry>();
        readonly Dictionary<string, LinkedListNode<Entry>> index = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.OrdinalIgnoreCase);
        public readonly long Budget;
        long used;
        public long Used { get { lock (gate) return used; } }
        public FrameCache(long budget) { Budget = budget; }
        public PhotoFrame Get(string key)
        {
            lock (gate) { LinkedListNode<Entry> node; if (!index.TryGetValue(key, out node)) return null; lru.Remove(node); lru.AddFirst(node); return node.Value.Frame; }
        }
        public void Put(string key, PhotoFrame frame)
        {
            lock (gate)
            {
                LinkedListNode<Entry> existing; if (index.TryGetValue(key, out existing)) { used -= existing.Value.Frame.Bytes; lru.Remove(existing); index.Remove(key); }
                if (frame.Bytes > Budget) return;
                while (used + frame.Bytes > Budget && lru.Last != null) { var last = lru.Last; used -= last.Value.Frame.Bytes; index.Remove(last.Value.Key); lru.RemoveLast(); }
                var node = lru.AddFirst(new Entry { Key = key, Frame = frame }); index[key] = node; used += frame.Bytes;
            }
        }
        public void Clear() { lock (gate) { lru.Clear(); index.Clear(); used = 0; } }
    }
    internal sealed class ImageLoader : IDisposable
    {
        sealed class Request { public long Version; public string Path; public int Edge; public string[] Neighbors; public Int32Rect? Detail; public double Scale; }
        readonly object gate = new object();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Thread worker;
        readonly FrameCache cache;
        Request pending;
        long version;
        volatile bool stopped;
        public event Action<long, PhotoFrame> Ready;
        public event Action<long, string> Failed;
        public long CacheBytes { get { return cache.Used; } }
        public ImageLoader(long cacheBudget) { cache = new FrameCache(cacheBudget); worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "ClearView image decode" }; worker.SetApartmentState(ApartmentState.STA); worker.Start(); }
        public long Load(string path, int edge, string[] neighbors)
        {
            lock (gate) { long id = ++version; pending = new Request { Version = id, Path = path, Edge = Math.Max(256, Math.Min(4096, edge)), Neighbors = neighbors ?? new string[0] }; signal.Set(); return id; }
        }
        public long LoadDetail(string path, Int32Rect region, double scale)
        {
            lock (gate) { long id = ++version; pending = new Request { Version = id, Path = path, Detail = region, Scale = scale, Neighbors = new string[0] }; signal.Set(); return id; }
        }
        bool Current(long id) { lock (gate) return !stopped && version == id; }
        void Publish(Request request, PhotoFrame frame) { if (Current(request.Version) && Ready != null) Ready(request.Version, frame); }
        string PreviewKey(PhotoInfo info, int edge) { return info.Key + "|" + Math.Min(edge, Math.Max(info.Width, info.Height)); }
        void Work()
        {
            while (!stopped)
            {
                signal.WaitOne(); if (stopped) break;
                Request request; lock (gate) { request = pending; pending = null; } if (request == null) continue;
                try
                {
                    PhotoInfo info = ImageCodec.ReadInfo(request.Path); if (!Current(request.Version)) continue;
                    if (request.Detail.HasValue) { Publish(request, ImageCodec.Detail(info, request.Detail.Value, request.Scale)); continue; }
                    string key = PreviewKey(info, request.Edge); PhotoFrame frame = cache.Get(key);
                    if (frame != null) { Publish(request, Copy(frame, true, true)); }
                    else
                    {
                        if (request.Edge > 1024 && Math.Max(info.Width, info.Height) > 2048)
                        {
                            string smallKey = PreviewKey(info, 768); var small = cache.Get(smallKey);
                            if (small == null) { small = ImageCodec.Preview(info, 768); cache.Put(smallKey, small); }
                            Publish(request, Copy(small, false, false)); if (!Current(request.Version)) continue;
                        }
                        frame = ImageCodec.Preview(info, request.Edge); cache.Put(key, frame); Publish(request, frame);
                    }
                    foreach (string neighbor in request.Neighbors)
                    {
                        if (!Current(request.Version)) break;
                        try { PhotoInfo nearby = ImageCodec.ReadInfo(neighbor); string nearKey = PreviewKey(nearby, request.Edge); if (cache.Get(nearKey) == null) { var next = ImageCodec.Preview(nearby, request.Edge); if (Current(request.Version)) cache.Put(nearKey, next); } } catch (Exception ex) { if (ex is OutOfMemoryException) cache.Clear(); }
                    }
                }
                catch (Exception ex) { if (ex is OutOfMemoryException) cache.Clear(); if (Current(request.Version) && Failed != null) Failed(request.Version, ex.Message); }
            }
        }
        static PhotoFrame Copy(PhotoFrame f, bool cached, bool final) { return new PhotoFrame { Info = f.Info, Pixels = f.Pixels, Region = f.Region, Milliseconds = cached ? 0 : f.Milliseconds, Cached = cached, Final = final }; }
        public void Dispose() { stopped = true; lock (gate) { pending = null; version++; } signal.Set(); cache.Clear(); /* Native decoders cannot be forcibly cancelled; the background thread exits after its current decode. */ }
    }
}
