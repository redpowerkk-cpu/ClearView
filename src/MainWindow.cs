using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClearView
{
    internal sealed partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string first, string second);
        static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(25, 29, 32)), TextBrush = new SolidColorBrush(Color.FromRgb(236, 243, 240)), MutedBrush = new SolidColorBrush(Color.FromRgb(170, 185, 180)), AccentBrush = new SolidColorBrush(Color.FromRgb(139, 226, 194));
        static readonly string[] Extensions = { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".tif", ".tiff", ".gif", ".ico", ".wdp", ".jxr", ".hdp", ".webp", ".heic", ".heif", ".avif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2" };
        readonly ImageLoader loader = new ImageLoader(128L * 1024 * 1024);
        readonly PhotoSurface surface = new PhotoSurface();
        readonly Grid root = new Grid(), header = new Grid(), footer = new Grid();
        readonly TextBlock filename = new TextBlock(), info = new TextBlock(), counter = new TextBlock(), percent = new TextBlock(), busy = new TextBlock();
        readonly StackPanel empty = new StackPanel();
        readonly TextBlock emptyTitle = new TextBlock(), emptyDescription = new TextBlock();
        readonly DispatcherTimer refine = new DispatcherTimer(), hideChrome = new DispatcherTimer(), slideshow = new DispatcherTimer();
        readonly List<string> files = new List<string>();
        readonly string[] initial;
        int index = -1, folderGeneration;
        long version;
        bool closed, full, loading;
        string requested = "", lastTile = "";
        Rect savedBounds;
        WindowState savedState;
        Stopwatch loadWatch;
        public MainWindow(string[] args)
        {
            initial = args; Title = "ClearView"; Width = 1120; Height = 760; MinWidth = 820; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = PanelBrush; Foreground = TextBrush; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12; AllowDrop = true;
            root.Background = PanelBrush; root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) }); Content = root;
            BuildHeader(); BuildFooter();
            var canvas = new Grid(); Grid.SetRow(canvas, 1); root.Children.Add(canvas); canvas.Children.Add(surface);
            empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.MaxWidth = 540;
            emptyTitle.Text = "ClearView"; emptyTitle.FontFamily = new FontFamily("Segoe UI"); emptyTitle.FontSize = 36; emptyTitle.HorizontalAlignment = HorizontalAlignment.Center; emptyTitle.Margin = new Thickness(0, 0, 0, 18);
            emptyDescription.Text = "사진을 끌어 놓거나 파일을 열어 주세요."; emptyDescription.TextWrapping = TextWrapping.Wrap; emptyDescription.TextAlignment = TextAlignment.Center; emptyDescription.Foreground = MutedBrush; emptyDescription.Margin = new Thickness(0, 0, 0, 28);
            var open = TextButton("사진 열기", OpenFile); open.Background = AccentBrush; open.Foreground = PanelBrush; open.Width = 140; open.Height = 42; open.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(emptyTitle); empty.Children.Add(emptyDescription); empty.Children.Add(open); canvas.Children.Add(empty);
            busy.Foreground = TextBrush; busy.Background = PanelBrush; busy.Padding = new Thickness(16, 8, 16, 8); busy.HorizontalAlignment = HorizontalAlignment.Center; busy.VerticalAlignment = VerticalAlignment.Top; busy.Margin = new Thickness(0, 16, 0, 0); busy.Visibility = Visibility.Collapsed; canvas.Children.Add(busy); busy.IsHitTestVisible = false;
            surface.ViewChanged += delegate { UpdateZoom(); ScheduleDetail(); };
            surface.FullscreenRequested += ToggleFullscreen;
            loader.Ready += delegate(long id, PhotoFrame frame) { Dispatcher.BeginInvoke(new Action(delegate { if (!closed && id == version) Receive(frame); })); };
            loader.Failed += delegate(long id, string message) { Dispatcher.BeginInvoke(new Action(delegate { if (!closed && id == version) Error(message); })); };
            refine.Interval = TimeSpan.FromMilliseconds(160); refine.Tick += delegate { refine.Stop(); RefineDetail(); };
            hideChrome.Interval = TimeSpan.FromSeconds(2); hideChrome.Tick += delegate { if (full && !header.IsMouseOver && !footer.IsMouseOver) { header.Visibility = footer.Visibility = Visibility.Hidden; hideChrome.Stop(); } };
            slideshow.Interval = TimeSpan.FromSeconds(3); slideshow.Tick += delegate { if (!loading && files.Count > 1) Navigate((index + 1) % files.Count); };
            PreviewKeyDown += KeyInput;
            MouseMove += delegate { if (full) { header.Visibility = footer.Visibility = Visibility.Visible; hideChrome.Stop(); hideChrome.Start(); } };
            DragOver += delegate(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop)); };
            Loaded += delegate
            {
                int dark = 1; try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); } catch { }
                if (initial.Length > 0) OpenPaths(initial); surface.Focus();
            };
            Closed += delegate { closed = true; folderGeneration++; loader.Dispose(); refine.Stop(); hideChrome.Stop(); slideshow.Stop(); };
        }
        static Style ButtonStyle()
        {
            var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush)); style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent)); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5))); style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); border.AppendChild(content);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(46, 55, 58)))); style.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush)); style.Triggers.Add(focus);
            return style;
        }
        Button TextButton(string text, Action action)
        {
            var button = new Button { Content = text, Style = ButtonStyle(), Height = 36, Margin = new Thickness(2, 0, 2, 0) }; button.Click += delegate { action(); }; AutomationProperties.SetName(button, text); return button;
        }
        Button IconButton(string name, string geometry, Action action)
        {
            var button = TextButton(name, action); button.Width = 38; button.Padding = new Thickness(7); button.ToolTip = name;
            button.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Stroke = TextBrush, StrokeThickness = 1.8, Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; return button;
        }
        void BuildHeader()
        {
            header.Background = PanelBrush; header.Margin = new Thickness(20, 0, 16, 0); Grid.SetRow(header, 0); root.Children.Add(header);
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; filename.Text = "ClearView"; filename.FontWeight = FontWeights.SemiBold; filename.FontSize = 14; filename.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Text = "가볍게 열고, 선명하게 감상하세요."; info.Foreground = MutedBrush; info.Margin = new Thickness(0, 4, 0, 0); info.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(filename); labels.Children.Add(info); header.Children.Add(labels);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 1); actions.Children.Add(TextButton("파일 열기", OpenFile));
            var menu = IconButton("메뉴", "M 3,10 L 4,10 M 10,10 L 11,10 M 17,10 L 18,10", ShowMenu); actions.Children.Add(menu); header.Children.Add(actions);
        }
        void BuildFooter()
        {
            footer.Background = PanelBrush; footer.Margin = new Thickness(16, 0, 16, 0); Grid.SetRow(footer, 2); root.Children.Add(footer);
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(IconButton("이전 사진 (←)", "M 13,3 L 6,10 L 13,17", delegate { Navigate(index - 1); })); left.Children.Add(IconButton("다음 사진 (→)", "M 7,3 L 14,10 L 7,17", delegate { Navigate(index + 1); }));
            counter.Text = "0 / 0"; counter.Foreground = MutedBrush; counter.VerticalAlignment = VerticalAlignment.Center; counter.Margin = new Thickness(12, 0, 0, 0); left.Children.Add(counter); footer.Children.Add(left);
            var center = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(center, 1);
            center.Children.Add(IconButton("축소 (-)", "M 3,10 L 17,10", delegate { ZoomBy(1 / 1.25); })); percent.Text = "100%"; percent.Width = 58; percent.TextAlignment = TextAlignment.Center; percent.VerticalAlignment = VerticalAlignment.Center; center.Children.Add(percent);
            center.Children.Add(IconButton("확대 (+)", "M 3,10 L 17,10 M 10,3 L 10,17", delegate { ZoomBy(1.25); })); center.Children.Add(TextButton("화면 맞춤", surface.Fit)); center.Children.Add(TextButton("100%", surface.ActualSize));
            center.Children.Add(IconButton("회전 (R)", "M 16,7 A 7,7 0 1 0 17,13 M 16,2 L 16,7 L 11,7", surface.Rotate)); footer.Children.Add(center);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 2);
            right.Children.Add(IconButton("폴더 열기 (Ctrl+Shift+O)", "M 2,17 L 2,4 L 8,4 L 10,7 L 18,7 L 18,17 Z", OpenFolder)); right.Children.Add(IconButton("전체화면 (F11 / 두 번 클릭)", "M 7,2 L 2,2 L 2,7 M 13,2 L 18,2 L 18,7 M 18,13 L 18,18 L 13,18 M 7,18 L 2,18 L 2,13", ToggleFullscreen)); footer.Children.Add(right);
        }
        static bool Supported(string path) { return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        void OpenFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "사진 열기", Multiselect = true, Filter = "사진|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.tif;*.tiff;*.gif;*.ico;*.wdp;*.jxr;*.webp;*.heic;*.heif;*.avif;*.dng;*.cr2;*.cr3;*.nef;*.arw;*.orf;*.rw2|모든 파일|*.*" };
            if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
        }
        void OpenFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "사진 폴더 선택", ShowNewFolderButton = false })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) OpenPaths(new[] { dialog.SelectedPath });
        }
        async void OpenPaths(string[] paths)
        {
            if (paths.Length == 0) return; slideshow.Stop(); int generation = ++folderGeneration;
            string selected = paths.FirstOrDefault(File.Exists); string directory = paths.FirstOrDefault(Directory.Exists);
            if (selected == null && directory == null) { Error("파일을 찾을 수 없습니다. 파일 위치를 확인해 주세요."); return; }
            if (paths.Length > 1 && selected != null)
            {
                files.Clear(); files.AddRange(paths.Where(File.Exists).Select(Path.GetFullPath)); index = -1; Navigate(0); return;
            }
            if (selected != null) { selected = Path.GetFullPath(selected); directory = Path.GetDirectoryName(selected); files.Clear(); files.Add(selected); index = -1; Navigate(0); }
            else { Busy("폴더의 사진을 찾는 중…"); }
            try
            {
                var listing = await Task.Run(delegate { var list = Directory.EnumerateFiles(directory).Where(Supported).Take(30000).ToList(); list.Sort(delegate(string a, string b) { return StrCmpLogicalW(a, b); }); return list; });
                if (closed || generation != folderGeneration) return;
                if (selected != null && !listing.Contains(selected, StringComparer.OrdinalIgnoreCase)) listing.Add(selected);
                files.Clear(); files.AddRange(listing);
                if (files.Count == 0) { index = -1; surface.Clear(); Error("이 폴더에 지원되는 사진이 없습니다."); return; }
                index = selected == null ? -1 : files.FindIndex(p => p.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (selected == null) Navigate(0); else { UpdateCounter(); RequestPreview(); }
            }
            catch (Exception e) { if (generation == folderGeneration && !closed) { if (selected == null) Error(e.Message); else info.Text += " · 폴더 목록을 읽을 수 없음"; } }
        }
        void Navigate(int next)
        {
            if (next < 0 || next >= files.Count) return; index = next; requested = files[index]; loading = true; lastTile = ""; refine.Stop(); loadWatch = Stopwatch.StartNew(); Busy("사진 불러오는 중…"); UpdateCounter(); RequestPreview();
        }
        int PreviewEdge { get { return (int)Math.Max(1024, Math.Min(4096, Math.Ceiling(Math.Max(surface.ActualWidth, surface.ActualHeight) * surface.DpiScale / 256) * 256)); } }
        void RequestPreview()
        {
            if (index < 0 || index >= files.Count) return;
            var neighbors = new List<string>(); if (index + 1 < files.Count) neighbors.Add(files[index + 1]); if (index > 0) neighbors.Add(files[index - 1]);
            version = loader.Load(files[index], PreviewEdge, neighbors.ToArray());
        }
        void Receive(PhotoFrame frame)
        {
            if (!frame.Info.Path.Equals(requested, StringComparison.OrdinalIgnoreCase)) return;
            if (frame.IsDetail) { surface.SetDetail(frame); busy.Visibility = Visibility.Collapsed; return; }
            surface.SetPreview(frame); empty.Visibility = Visibility.Collapsed; filename.Text = Path.GetFileName(frame.Info.Path); Title = filename.Text + " — ClearView";
            info.Text = frame.Info.Width + " × " + frame.Info.Height + "   ·   " + (frame.Info.FileBytes / 1048576.0).ToString("0.##") + " MB   ·   " + frame.Info.Format;
            if (frame.Info.Color != null) info.Text += frame.Info.ColorWarning ? "   ·   색 프로필 변환 미지원" : "   ·   색 프로필 적용";
            if (frame.Info.Frames > 1) info.Text += "   ·   첫 번째 이미지";
            loading = !frame.Final; if (frame.Final) { busy.Visibility = Visibility.Collapsed; ScheduleDetail(); } else Busy("미리보기 표시 중…");
            UpdateZoom();
        }
        void Busy(string text) { busy.Text = text; busy.Visibility = Visibility.Visible; }
        void Error(string message)
        {
            loading = false; busy.Visibility = Visibility.Collapsed; slideshow.Stop();
            if (surface.Preview == null || !surface.Preview.Info.Path.Equals(requested, StringComparison.OrdinalIgnoreCase))
            {
                surface.Clear(); emptyTitle.Text = "사진을 열 수 없습니다"; emptyDescription.Text = "파일이 손상되었거나 해당 형식의 Windows 코덱이 없을 수 있습니다.\n다른 사진을 열어 주세요."; empty.Visibility = Visibility.Visible;
            }
            info.Text = message; info.ToolTip = message;
        }
        void UpdateCounter() { counter.Text = files.Count == 0 ? "0 / 0" : (index + 1) + " / " + files.Count; }
        void UpdateZoom() { percent.Text = (surface.Zoom * surface.DpiScale * 100).ToString("0") + "%"; }
        void ZoomBy(double factor) { surface.ZoomAt(surface.Zoom * factor, new Point(surface.ActualWidth / 2, surface.ActualHeight / 2)); }
        void ScheduleDetail() { refine.Stop(); if (surface.NeedsDetail && !loading) refine.Start(); }
        void RefineDetail()
        {
            if (!surface.NeedsDetail || loading || surface.Preview.Info.Path != requested) return;
            Int32Rect region = surface.VisibleRegion(); if (region.Width == 0 || region.Height == 0) return;
            double scale = Math.Min(1, surface.Zoom * surface.DpiScale);
            string tile = surface.Preview.Info.Key + "|" + region + "|" + scale.ToString("0.000", CultureInfo.InvariantCulture); if (tile == lastTile) return; lastTile = tile;
            version = loader.LoadDetail(requested, region, scale); Busy("확대 영역을 선명하게 표시하는 중…");
        }
        void ToggleFullscreen()
        {
            if (!full)
            {
                savedState = WindowState; savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; full = true;
                // Overlay controls instead of reducing the photograph's fullscreen viewport.
                Grid.SetRowSpan(surface.Parent as UIElement, 3); Grid.SetRow(surface.Parent as UIElement, 0); Panel.SetZIndex(header, 2); Panel.SetZIndex(footer, 2); hideChrome.Start();
            }
            else
            {
                full = false; hideChrome.Stop(); header.Visibility = footer.Visibility = Visibility.Visible; Grid.SetRowSpan(surface.Parent as UIElement, 1); Grid.SetRow(surface.Parent as UIElement, 1);
                WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; Left = savedBounds.Left; Top = savedBounds.Top; Width = savedBounds.Width; Height = savedBounds.Height; WindowState = savedState;
            }
            surface.Focus();
        }
        void ShowMenu()
        {
            var menu = new ContextMenu { Background = PanelBrush, Foreground = TextBrush, FontFamily = FontFamily, FontSize = 13 };
            AddItem(menu, "폴더 열기", OpenFolder); AddItem(menu, slideshow.IsEnabled ? "슬라이드쇼 중지" : "슬라이드쇼 시작 (3초)", delegate { if (slideshow.IsEnabled) slideshow.Stop(); else if (files.Count > 1) slideshow.Start(); });
            AddItem(menu, "파일 위치 열기", delegate { if (index >= 0) Process.Start("explorer.exe", "/select,\"" + files[index] + "\""); });
            AddItem(menu, "사진 정보", ShowInfo); AddItem(menu, "도움말", Help); menu.IsOpen = true;
        }
        static void AddItem(ContextMenu menu, string title, Action action) { var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        void ShowInfo()
        {
            if (surface.Preview == null) return; var p = surface.Preview.Info;
            MessageBox.Show(this, p.Path + "\n\n" + p.Width + " × " + p.Height + " pixels\n" + (p.FileBytes / 1048576.0).ToString("0.00") + " MB\nEXIF 방향: " + p.Orientation + "\n이미지 수: " + p.Frames + "\n내장 색 프로필: " + (p.Color != null ? (p.ColorWarning ? "변환 미지원 · 기본 색상으로 표시" : "sRGB로 변환하여 표시") : "없음") + "\n\n미리보기 캐시: " + (loader.CacheBytes / 1048576.0).ToString("0.0") + " / 128 MiB\n회전·확대는 원본 파일을 변경하지 않습니다.", "사진 정보");
        }
        void Help() { MessageBox.Show(this, "ClearView 0.1.0\n\n← / →   이전 / 다음 사진\nHome / End   처음 / 마지막 사진\n휠, + / -   마우스 위치 중심 확대 / 축소\n드래그   확대 사진 이동\n0   화면 맞춤     1   원본 100%     R   회전\nF11 / F / 두 번 클릭   전체화면     Esc   해제\nCtrl+O   파일 열기     Ctrl+Shift+O   폴더 열기\nSpace   슬라이드쇼 시작 / 중지\n\n큰 사진은 미리보기 후 확대 영역을 별도로 읽습니다.\nGIF·다중 TIFF는 첫 프레임을 표시합니다.\nHEIC·WebP·RAW 등은 설치된 Windows 코덱에 따라 달라집니다.\n고용량 파일의 실제 속도는 디스크와 이미지 형식에 따라 다릅니다.", "ClearView 도움말"); }
        void KeyInput(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.O) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) OpenFolder(); else OpenFile(); e.Handled = true; return; }
            if (Keyboard.Modifiers != ModifierKeys.None && e.Key != Key.OemPlus && e.Key != Key.Add) return;
            switch (e.Key)
            {
                case Key.Left: case Key.PageUp: Navigate(index - 1); break;
                case Key.Right: case Key.PageDown: Navigate(index + 1); break;
                case Key.Home: Navigate(0); break; case Key.End: Navigate(files.Count - 1); break;
                case Key.Add: case Key.OemPlus: ZoomBy(1.25); break; case Key.Subtract: case Key.OemMinus: ZoomBy(1 / 1.25); break;
                case Key.D0: case Key.NumPad0: surface.Fit(); break; case Key.D1: case Key.NumPad1: surface.ActualSize(); break;
                case Key.R: surface.Rotate(); break; case Key.F11: case Key.F: ToggleFullscreen(); break;
                case Key.Escape: if (full) ToggleFullscreen(); else slideshow.Stop(); break;
                case Key.Space: if (slideshow.IsEnabled) slideshow.Stop(); else if (files.Count > 1) slideshow.Start(); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
