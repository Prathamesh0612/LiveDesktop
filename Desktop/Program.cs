using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WinFormsHost = System.Windows.Forms.Integration.WindowsFormsHost;
using Draw = System.Drawing;
using Microsoft.Win32;

namespace LivelyMock
{
    class Item : INotifyPropertyChanged
    {
        public string Path;
        public string ContentHash;
        bool chk, inl = true, playing, queued, isDragging, isDragOver;
        TimeSpan dur;
        long sizeBytes;
        BitmapSource thumb;
        public event PropertyChangedEventHandler PropertyChanged;
        void N(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }

        public static bool IsStaticPhotoPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p.StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase)) return false;
            string e = System.IO.Path.GetExtension(p).ToLowerInvariant();
            return e == ".jpg" || e == ".jpeg" || e == ".png" || e == ".bmp" || e == ".webp";
        }
        public static bool IsGifPath(string p)
        {
            return !string.IsNullOrEmpty(p) && !p.StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase)
                && System.IO.Path.GetExtension(p).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        }
        public bool IsStaticPhoto { get { return IsStaticPhotoPath(Path); } }
        public bool IsGif { get { return IsGifPath(Path); } }
        public bool IsPhotoOrGif { get { return IsStaticPhoto || IsGif; } }

        public bool IsYouTube { get { return !string.IsNullOrEmpty(Path) && Path.StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase); } }
        public string YtVideoId { get { return IsYouTube ? YouTubeHelper.ParseStreamField(Path, "id") : ""; } }
        public int YtMaxHeight
        {
            get
            {
                int q;
                return (IsYouTube && int.TryParse(YouTubeHelper.ParseStreamField(Path, "q"), out q)) ? q : 2160;
            }
        }
        public string YtQualityLabel { get { return YouTubeHelper.HeightToLabel(YtMaxHeight); } }

        public string Name
        {
            get
            {
                if (IsYouTube)
                {
                    string t = YouTubeHelper.ParseStreamField(Path, "title");
                    return string.IsNullOrEmpty(t) ? ("YouTube " + YtVideoId) : t;
                }
                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }
        public string Ext { get { return IsYouTube ? ("YT · " + YtQualityLabel) : (System.IO.Path.GetExtension(Path) ?? "").TrimStart('.').ToUpperInvariant(); } }
        public bool Checked
        {
            get { return chk; }
            set
            {
                if (chk != value)
                {
                    chk = value;
                    N("Checked");
                    N("CardBorderBrush");
                }
            }
        }
        public bool InLoop
        {
            get { return inl; }
            set
            {
                if (inl != value)
                {
                    inl = value;
                    N("InLoop"); N("LoopText"); N("LoopBadgeBg"); N("LoopBadgeFg");
                }
            }
        }
        public bool IsPlaying
        {
            get { return playing; }
            set
            {
                if (playing != value)
                {
                    playing = value;
                    N("IsPlaying"); N("CardBorderBrush"); N("CardBorderThickness"); N("PlayingVisibility"); N("QueuedVisibility");
                }
            }
        }
        public bool IsQueued
        {
            get { return queued; }
            set
            {
                if (queued != value)
                {
                    queued = value;
                    N("IsQueued"); N("CardBorderBrush"); N("CardBorderThickness"); N("QueuedVisibility");
                }
            }
        }
        public bool IsDragging
        {
            get { return isDragging; }
            set
            {
                if (isDragging != value)
                {
                    isDragging = value;
                    N("IsDragging"); N("CardOpacity"); N("CardScale"); N("CardBorderBrush"); N("CardBorderThickness");
                }
            }
        }
        public bool IsDragOver
        {
            get { return isDragOver; }
            set
            {
                if (isDragOver != value)
                {
                    isDragOver = value;
                    N("IsDragOver"); N("CardScale"); N("CardBorderBrush"); N("CardBorderThickness"); N("DropTargetVisibility");
                }
            }
        }
        public string LoopText { get { return inl ? "● Loop" : "○ Off"; } }
        public Brush LoopBadgeBg { get { return inl ? Theme.BadgeLoopBg : Theme.BadgeOutBg; } }
        public Brush LoopBadgeFg { get { return inl ? Theme.BadgeLoopFg : Theme.TextMuted; } }
        public Brush CardBorderBrush { get { return isDragOver ? Brushes.White : (playing ? Theme.Accent : (queued ? Theme.B(215, 215, 226) : (chk ? Theme.B(200, 200, 200) : Theme.BorderSubtle))); } }
        public Thickness CardBorderThickness { get { return new Thickness(isDragOver ? 2.5 : ((playing || queued) ? 2.0 : 1.5)); } }
        public double CardOpacity { get { return isDragging ? 0.30 : 1.0; } }
        public double CardScale { get { return isDragOver ? 1.045 : (isDragging ? 0.92 : 1.0); } }
        public Visibility PlayingVisibility { get { return playing ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility QueuedVisibility { get { return (queued && !playing) ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility DropTargetVisibility { get { return isDragOver ? Visibility.Visible : Visibility.Collapsed; } }

        public TimeSpan Dur { get { return dur; } set { dur = value; N("Length"); N("MetaLine"); } }
        public string Length
        {
            get
            {
                if (IsStaticPhoto) return "PHOTO";
                if (IsGif) return "GIF";
                return dur.TotalSeconds <= 0 ? "--:--" : ((int)dur.TotalMinutes) + ":" + dur.Seconds.ToString("00");
            }
        }
        public long SizeBytes { get { return sizeBytes; } set { sizeBytes = value; N("SizeText"); N("MetaLine"); } }
        public string SizeText { get { return IsYouTube ? "Live Stream" : (sizeBytes <= 0 ? "" : (sizeBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB"); } }
        public string MetaLine { get { return Ext + (IsYouTube ? "  ·  Direct Stream" : (IsStaticPhoto ? "  ·  Photo" : "") + (sizeBytes > 0 ? "  ·  " + SizeText : "")); } }
        public BitmapSource Thumb { get { return thumb; } set { thumb = value; N("Thumb"); } }
        int orderIndex = 1;
        public int OrderIndex
        {
            get { return orderIndex; }
            set { if (orderIndex != value) { orderIndex = value; N("OrderIndex"); N("OrderBadge"); } }
        }
        public string OrderBadge { get { return "#" + orderIndex; } }
        public string EnsureHash()
        {
            if (IsYouTube || string.IsNullOrEmpty(Path) || !File.Exists(Path)) return null;
            if (sizeBytes <= 0) { try { sizeBytes = new FileInfo(Path).Length; } catch { } }
            if (string.IsNullOrEmpty(ContentHash)) ContentHash = FileHashHelper.GetSha256(Path);
            return ContentHash;
        }
    }

    static class FileHashHelper
    {
        public static string GetSha256(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }
    }

    class Playlist : INotifyPropertyChanged
    {
        string name;
        bool active, isDragging, isDragOver, inMultiLoop;
        public ObservableCollection<Item> Items = new ObservableCollection<Item>();
        public event PropertyChangedEventHandler PropertyChanged;
        void N(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }
        public string Name { get { return name; } set { name = value; N("Name"); N("Title"); N("CountText"); } }
        public string Title { get { return name + " (" + Items.Count + ")"; } }
        public string CountText { get { return Items.Count + " items  ·  " + Items.Count(i => i.InLoop) + " in loop"; } }
        public bool InMultiLoop
        {
            get { return inMultiLoop; }
            set { if (inMultiLoop != value) { inMultiLoop = value; N("InMultiLoop"); } }
        }
        public bool IsActive
        {
            get { return active; }
            set { if (active != value) { active = value; N("IsActive"); N("CardBg"); N("CardBorder"); } }
        }
        public bool IsDragging
        {
            get { return isDragging; }
            set { if (isDragging != value) { isDragging = value; N("IsDragging"); N("PlOpacity"); } }
        }
        public bool IsDragOver
        {
            get { return isDragOver; }
            set { if (isDragOver != value) { isDragOver = value; N("IsDragOver"); N("CardBorder"); N("CardBg"); } }
        }
        public double PlOpacity { get { return isDragging ? 0.38 : 1.0; } }
        public Brush CardBg { get { return isDragOver ? Theme.B(38, 38, 44) : (active ? Theme.B(28, 28, 30) : Theme.BgCard); } }
        public Brush CardBorder { get { return isDragOver ? Brushes.White : (active ? Theme.Accent : Theme.BorderSubtle); } }
        public void RefreshCounts() { N("Title"); N("CountText"); }
        public override string ToString() { return Title; }
    }

    class AnimCardBorder : Border
    {
        public static readonly DependencyProperty CardScaleProperty = DependencyProperty.Register(
            "CardScale", typeof(double), typeof(AnimCardBorder),
            new PropertyMetadata(1.0, OnCardScaleChanged));

        readonly ScaleTransform st = new ScaleTransform(1.0, 1.0);
        public AnimCardBorder()
        {
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = st;
        }
        public double CardScale { get { return (double)GetValue(CardScaleProperty); } set { SetValue(CardScaleProperty, value); } }
        static void OnCardScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = (AnimCardBorder)d;
            double target = (double)e.NewValue;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var ax = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            var ay = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            b.st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            b.st.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }
    }

    static class AppPaths
    {
        public static string DataDir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string liveDir = System.IO.Path.Combine(appData, "LiveDesk");
                string oldDir = System.IO.Path.Combine(appData, "LivelyMock");
                if (!Directory.Exists(liveDir) && Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.CreateDirectory(liveDir);
                        foreach (var f in Directory.GetFiles(oldDir))
                            File.Copy(f, System.IO.Path.Combine(liveDir, System.IO.Path.GetFileName(f)), false);
                    }
                    catch { }
                }
                return liveDir;
            }
        }
    }

    static class Theme
    {
        public static SolidColorBrush B(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }
        public static SolidColorBrush BA(byte a, byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromArgb(a, r, g, b)); br.Freeze(); return br; }

        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // Monochrome Pure Black & White Aesthetic Palette
        public static readonly Brush BgApp = B(8, 8, 9);
        public static readonly Brush BgSidebar = B(11, 11, 13);
        public static readonly Brush BgHeader = B(11, 11, 13);
        public static readonly Brush BgCard = B(18, 18, 20);
        public static readonly Brush BgCardInner = B(13, 13, 15);
        public static readonly Brush BgCardHover = B(30, 30, 34);
        public static readonly Brush BgInput = B(15, 15, 17);
        public static readonly Brush BgTransport = B(10, 10, 12);
        public static readonly Brush BorderSubtle = B(38, 38, 42);

        public static readonly Brush Accent = B(245, 245, 247);
        public static readonly Brush AccentHover = B(255, 255, 255);
        public static readonly Brush AccentPressed = B(212, 212, 216);
        public static readonly Brush AccentFg = B(10, 10, 12);
        public static readonly Brush AccentCyan = B(230, 230, 235);
        public static readonly Brush AccentGreen = B(245, 245, 247);
        public static readonly Brush AccentAmber = B(190, 190, 198);

        public static readonly Brush TextPrimary = B(245, 245, 247);
        public static readonly Brush TextSecondary = B(160, 160, 168);
        public static readonly Brush TextMuted = B(104, 104, 112);

        public static readonly Brush BadgeLoopBg = BA(225, 24, 24, 27);
        public static readonly Brush BadgeLoopFg = B(245, 245, 247);
        public static readonly Brush BadgeOutBg = BA(210, 14, 14, 16);
        public static readonly Brush BadgeDarkOverlay = BA(215, 8, 8, 10);
    }

    static class ThumbCache
    {
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObj);

        public static BitmapSource Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            BitmapSource bs;
            lock (cache) { if (cache.TryGetValue(path, out bs)) return bs; }
            try
            {
                Guid g = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                IShellItemImageFactory f;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref g, out f) == 0 && f != null)
                {
                    IntPtr hbm;
                    int hr = f.GetImage(new SIZE { cx = 384, cy = 216 }, 0x08, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero)
                        hr = f.GetImage(new SIZE { cx = 384, cy = 216 }, 0x00, out hbm);
                    if (hr == 0 && hbm != IntPtr.Zero)
                    {
                        try
                        {
                            bs = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bs.Freeze();
                            lock (cache) { cache[path] = bs; }
                            return bs;
                        }
                        finally { DeleteObject(hbm); }
                    }
                }
            }
            catch { }
            try
            {
                if ((Item.IsStaticPhotoPath(path) || Item.IsGifPath(path)) && File.Exists(path))
                {
                    bs = FromFileBytes(File.ReadAllBytes(path));
                    if (bs != null) { lock (cache) { cache[path] = bs; } return bs; }
                }
            }
            catch { }
            return null;
        }

        public static BitmapSource FromFileBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 384;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        static readonly Queue<Action> staQueue = new Queue<Action>();
        static Thread staWorker;

        static void EnsureStaWorker()
        {
            if (staWorker != null) return;
            staWorker = new Thread(() =>
            {
                while (true)
                {
                    Action job = null;
                    lock (staQueue)
                    {
                        while (staQueue.Count == 0) Monitor.Wait(staQueue);
                        job = staQueue.Dequeue();
                    }
                    try { if (job != null) job(); } catch { }
                }
            }) { IsBackground = true };
            staWorker.SetApartmentState(ApartmentState.STA);
            staWorker.Start();
        }

        public static void GetAsync(string path, Action<BitmapSource> onLoaded)
        {
            if (string.IsNullOrEmpty(path)) return;
            BitmapSource cached;
            lock (cache) { if (cache.TryGetValue(path, out cached)) { if (onLoaded != null) onLoaded(cached); return; } }
            EnsureStaWorker();
            lock (staQueue)
            {
                staQueue.Enqueue(() =>
                {
                    var bs = Get(path);
                    if (bs != null && Application.Current != null && Application.Current.Dispatcher != null)
                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (onLoaded != null) onLoaded(bs); }));
                });
                Monitor.Pulse(staQueue);
            }
        }

        public static void GetYouTubeAsync(string videoId, Action<BitmapSource> onLoaded)
        {
            if (string.IsNullOrEmpty(videoId)) return;
            string key = "yt:" + videoId;
            BitmapSource cached;
            lock (cache) { if (cache.TryGetValue(key, out cached)) { if (onLoaded != null) onLoaded(cached); return; } }

            string thumbDir = System.IO.Path.Combine(AppPaths.DataDir, "thumbs");
            string localJpg = System.IO.Path.Combine(thumbDir, videoId + ".jpg");
            if (File.Exists(localJpg))
            {
                try
                {
                    var bs = FromFileBytes(File.ReadAllBytes(localJpg));
                    if (bs != null) { cache[key] = bs; if (onLoaded != null) onLoaded(bs); return; }
                }
                catch { }
            }

            Task.Run(() =>
            {
                try
                {
                    YouTubeHelper.EnsureTls();
                    Directory.CreateDirectory(thumbDir);
                    byte[] data = null;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "Mozilla/5.0");
                        try { data = wc.DownloadData("https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg"); }
                        catch { }
                    }
                    if (data != null && data.Length > 500)
                    {
                        try { File.WriteAllBytes(localJpg, data); } catch { }
                        var bs = FromFileBytes(data);
                        if (bs != null)
                        {
                            cache[key] = bs;
                            if (Application.Current != null && Application.Current.Dispatcher != null)
                                Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (onLoaded != null) onLoaded(bs); }));
                        }
                    }
                }
                catch { }
            });
        }
    }

    static class YouTubeHelper
    {
        public class VideoInfo
        {
            public string Id;
            public string Title;
            public double DurationSec;
            public int MaxHeight;
            public List<int> AvailableHeights = new List<int>();
        }

        public class StreamUrls
        {
            public string VideoUrl;
            public string AudioUrl;
            public DateTime CachedAtUtc;
        }

        static readonly Dictionary<string, StreamUrls> streamCache = new Dictionary<string, StreamUrls>(StringComparer.OrdinalIgnoreCase);

        public static string ToolsDir
        {
            get { return System.IO.Path.Combine(AppPaths.DataDir, "tools"); }
        }

        public static string CustomDownloadsDir;

        public static string DefaultDownloadsDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LiveDesk"); }
        }

        public static string DownloadsDir
        {
            get { return !string.IsNullOrWhiteSpace(CustomDownloadsDir) ? CustomDownloadsDir : DefaultDownloadsDir; }
        }

        public static void EnsureTls()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | SecurityProtocolType.Tls12; }
            catch { }
        }

        public static string HeightToLabel(int h)
        {
            if (h >= 4320) return "8K";
            if (h >= 2160) return "4K";
            if (h >= 1440) return "2K";
            if (h >= 1080) return "1080p";
            if (h >= 720) return "720p";
            return h + "p";
        }

        public static string ExtractVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (Regex.IsMatch(url, @"^[A-Za-z0-9_-]{11}$")) return url;
            var m = Regex.Match(url, @"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static bool IsYouTubeUrl(string text)
        {
            return !string.IsNullOrEmpty(ExtractVideoId(text));
        }

        public static string BuildStreamPath(string videoId, int maxHeight, string title, double durSec)
        {
            string safeTitle = (title ?? ("YouTube " + videoId)).Replace("|", "-").Trim();
            return "ytstream://" + videoId + "?q=" + maxHeight + "&title=" + Uri.EscapeDataString(safeTitle) + "&dur=" + ((int)Math.Max(0, durSec));
        }

        public static string ParseStreamField(string streamPath, string key)
        {
            if (string.IsNullOrEmpty(streamPath) || !streamPath.StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase)) return "";
            string rest = streamPath.Substring("ytstream://".Length);
            int qIdx = rest.IndexOf('?');
            string id = qIdx >= 0 ? rest.Substring(0, qIdx) : rest;
            if (key == "id") return id;
            if (qIdx < 0) return "";
            string qs = rest.Substring(qIdx + 1);
            foreach (var part in qs.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(part.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return "";
        }

        static string FindTool(string exeName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] bundled =
                {
                    System.IO.Path.Combine(baseDir, "tools", exeName),
                    System.IO.Path.Combine(baseDir, exeName)
                };
                foreach (var b in bundled)
                    if (File.Exists(b) && new FileInfo(b).Length > 100000) return b;
            }
            catch { }

            string local = System.IO.Path.Combine(ToolsDir, exeName);
            if (File.Exists(local) && new FileInfo(local).Length > 100000) return local;

            // Legacy LivelyMock check + auto-copy to LiveDesk tools
            try
            {
                string legacy = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LivelyMock", "tools", exeName);
                if (File.Exists(legacy) && new FileInfo(legacy).Length > 100000)
                {
                    try
                    {
                        Directory.CreateDirectory(ToolsDir);
                        if (!File.Exists(local)) File.Copy(legacy, local, true);
                    }
                    catch { }
                    return File.Exists(local) ? local : legacy;
                }
            }
            catch { }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    string candidate = System.IO.Path.Combine(p.Trim(), exeName);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length > 100000) return candidate;
                }
                catch { }
            }
            return null;
        }

        public static string GetYtDlpPath() { return FindTool("yt-dlp.exe"); }
        public static string GetFfmpegPath() { return FindTool("ffmpeg.exe"); }

        public static string GetFfmpegDir()
        {
            string f = GetFfmpegPath();
            if (!string.IsNullOrEmpty(f) && File.Exists(f))
            {
                return System.IO.Path.GetDirectoryName(f);
            }
            return ToolsDir;
        }

        public static bool MergeMediaStreams(string videoPath, string audioPath, string outputPath)
        {
            try
            {
                string ffmpeg = GetFfmpegPath();
                if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) return false;
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)) return false;
                if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)) return false;

                string outDir = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                string tempOut = outputPath + ".merging.mp4";
                if (File.Exists(tempOut)) try { File.Delete(tempOut); } catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-y -i \"" + videoPath + "\" -i \"" + audioPath + "\" -c copy -movflags +faststart \"" + tempOut + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(90000);
                    if (!p.HasExited) { try { p.Kill(); } catch { } return false; }
                    if (p.ExitCode != 0) return false;
                }

                if (File.Exists(tempOut) && new FileInfo(tempOut).Length > 1000)
                {
                    if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
                    File.Move(tempOut, outputPath);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static async Task<bool> EnsureToolsAsync(bool needFfmpeg, Action<string, double> onProgress, CancellationToken ct)
        {
            EnsureTls();
            Directory.CreateDirectory(ToolsDir);

            if (GetYtDlpPath() == null)
            {
                if (onProgress != null) onProgress("Downloading yt-dlp...", 5);
                string dst = System.IO.Path.Combine(ToolsDir, "yt-dlp.exe");
                string tmp = dst + ".tmp";
                await DownloadFileWithProgressAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", tmp, (pct) =>
                {
                    if (onProgress != null) onProgress("Downloading yt-dlp... " + pct.ToString("0") + "%", needFfmpeg ? pct * 0.3 : pct);
                }, ct);
                if (ct.IsCancellationRequested) return false;
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
            }

            if (needFfmpeg && GetFfmpegPath() == null)
            {
                if (onProgress != null) onProgress("Downloading ffmpeg...", 35);
                string zipPath = System.IO.Path.Combine(ToolsDir, "ffmpeg_dl.zip");
                await DownloadFileWithProgressAsync("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", zipPath, (pct) =>
                {
                    if (onProgress != null) onProgress("Downloading ffmpeg... " + pct.ToString("0") + "%", 35 + pct * 0.55);
                }, ct);
                if (ct.IsCancellationRequested) { try { File.Delete(zipPath); } catch { } return false; }

                if (onProgress != null) onProgress("Extracting ffmpeg.exe...", 94);
                await Task.Run(() =>
                {
                    string dst = System.IO.Path.Combine(ToolsDir, "ffmpeg.exe");
                    using (var arc = ZipFile.OpenRead(zipPath))
                    {
                        var entry = arc.Entries.FirstOrDefault(e => e.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                        if (entry != null) entry.ExtractToFile(dst, true);
                    }
                    try { File.Delete(zipPath); } catch { }
                });
            }

            return GetYtDlpPath() != null && (!needFfmpeg || GetFfmpegPath() != null);
        }

        static async Task DownloadFileWithProgressAsync(string url, string dstPath, Action<double> onPct, CancellationToken ct)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0");
                wc.DownloadProgressChanged += (s, e) =>
                {
                    if (onPct != null)
                    {
                        double pct = e.TotalBytesToReceive > 0 ? (e.BytesReceived * 100.0 / e.TotalBytesToReceive) : 0;
                        onPct(pct);
                    }
                };
                using (ct.Register(() => { try { wc.CancelAsync(); } catch { } }))
                {
                    await wc.DownloadFileTaskAsync(new Uri(url), dstPath);
                }
            }
        }

        static Task<string> RunYtDlpCaptureAsync(string args, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                string exe = GetYtDlpPath();
                if (string.IsNullOrEmpty(exe)) return "";
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                    {
                        p.BeginErrorReadLine();
                        string outStr = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(45000);
                        return outStr;
                    }
                }
            });
        }

        public static async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct)
        {
            string vid = ExtractVideoId(url);
            if (string.IsNullOrEmpty(vid)) return null;
            string cleanUrl = "https://www.youtube.com/watch?v=" + vid;
            string json = await RunYtDlpCaptureAsync("--no-playlist --no-warnings -J \"" + cleanUrl + "\"", ct);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return null;

            var info = new VideoInfo
            {
                Id = vid,
                Title = root.ContainsKey("title") ? Convert.ToString(root["title"]) : ("YouTube " + vid),
                DurationSec = root.ContainsKey("duration") && root["duration"] != null ? Convert.ToDouble(root["duration"]) : 0
            };

            var heights = new HashSet<int>();
            if (root.ContainsKey("formats") && root["formats"] is System.Collections.IEnumerable)
            {
                foreach (var fo in (System.Collections.IEnumerable)root["formats"])
                {
                    var f = fo as Dictionary<string, object>;
                    if (f == null || !f.ContainsKey("height") || f["height"] == null) continue;
                    int h;
                    if (int.TryParse(Convert.ToString(f["height"]), out h) && h >= 360)
                    {
                        heights.Add(h);
                        if (h > info.MaxHeight) info.MaxHeight = h;
                    }
                }
            }
            info.AvailableHeights = heights.OrderByDescending(x => x).ToList();
            return info;
        }

        public static async Task<StreamUrls> ResolveStreamUrlsAsync(string videoIdOrUrl, int maxHeight, CancellationToken ct)
        {
            string vid = ExtractVideoId(videoIdOrUrl) ?? videoIdOrUrl;
            string key = vid + "@" + maxHeight;
            StreamUrls cached;
            lock (streamCache)
            {
                if (streamCache.TryGetValue(key, out cached) && (DateTime.UtcNow - cached.CachedAtUtc).TotalMinutes < 90)
                    return cached;
            }

            if (!await EnsureToolsAsync(false, null, ct)) return null;
            string cleanUrl = "https://www.youtube.com/watch?v=" + vid;
            string fmt = "bv*[height<=" + maxHeight + "]+ba/b[height<=" + maxHeight + "]/b";
            string outStr = await RunYtDlpCaptureAsync("--no-playlist --no-warnings -g -f \"" + fmt + "\" \"" + cleanUrl + "\"", ct);
            var lines = (outStr ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
            if (lines.Count == 0) return null;

            var res = new StreamUrls
            {
                VideoUrl = lines[0],
                AudioUrl = lines.Count > 1 ? lines[1] : null,
                CachedAtUtc = DateTime.UtcNow
            };
            lock (streamCache) { streamCache[key] = res; }
            return res;
        }

        public static async Task<string> DownloadVideoAsync(string url, int maxHeight, Action<string, double> onProgress, CancellationToken ct)
        {
            if (!await EnsureToolsAsync(true, onProgress, ct)) return null;

            string vid = ExtractVideoId(url);
            string cleanUrl = !string.IsNullOrEmpty(vid) ? ("https://www.youtube.com/watch?v=" + vid) : url.Trim();
            Directory.CreateDirectory(DownloadsDir);

            string exe = GetYtDlpPath();
            string ffmpegDir = GetFfmpegDir();
            string fmt = "bv*[height<=" + maxHeight + "][ext=mp4]+ba[ext=m4a]/bv*[height<=" + maxHeight + "]+ba[ext=m4a]/bv*[height<=" + maxHeight + "]+ba/b[height<=" + maxHeight + "]";
            string outDir = DownloadsDir;
            Directory.CreateDirectory(outDir);
            string outTmpl = System.IO.Path.Combine(outDir, "%(title).110B [%(height)sp-%(id)s].%(ext)s");
            string args = "--encoding utf-8 --no-playlist --no-simulate --progress --newline --no-warnings --windows-filenames --ffmpeg-location \"" + ffmpegDir + "\" -f \"" + fmt + "\" --merge-output-format mp4 -o \"" + outTmpl + "\" --print after_move:filepath \"" + cleanUrl + "\"";

            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                DateTime startedUtc = DateTime.UtcNow.AddSeconds(-3);
                string finalPath = null;
                int streamPass = 0;
                using (var p = Process.Start(psi))
                {
                    p.BeginErrorReadLine();
                    using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                    {
                        var rxPct = new Regex(@"\[download\]\s+([\d\.]+)%.*?at\s+([^\s]+).*?ETA\s+([^\s]+)");
                        var rxPctSimple = new Regex(@"\[download\]\s+([\d\.]+)%");
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            if (ct.IsCancellationRequested) break;
                            string tr = line.Trim();
                            if (tr.StartsWith("[download] Destination:", StringComparison.OrdinalIgnoreCase))
                            {
                                streamPass++;
                                continue;
                            }
                            if (tr.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) || tr.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase))
                            {
                                if (onProgress != null) onProgress("Merging " + HeightToLabel(maxHeight) + " video & audio into MP4...", 96);
                                continue;
                            }
                            var m = rxPct.Match(tr);
                            if (m.Success)
                            {
                                double pct;
                                double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct);
                                string phase = streamPass <= 1 ? ("Downloading " + HeightToLabel(maxHeight) + " video... ") : "Downloading audio track... ";
                                if (onProgress != null) onProgress(phase + pct.ToString("0.0") + "% (" + m.Groups[2].Value + ", ETA " + m.Groups[3].Value + ")", streamPass <= 1 ? pct * 0.85 : 85 + pct * 0.10);
                                continue;
                            }
                            var m2 = rxPctSimple.Match(tr);
                            if (m2.Success)
                            {
                                double pct;
                                double.TryParse(m2.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct);
                                if (onProgress != null) onProgress("Downloading... " + pct.ToString("0.0") + "%", pct * 0.9);
                                continue;
                            }
                            if ((tr.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || tr.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || tr.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)) && File.Exists(tr))
                            {
                                finalPath = tr;
                            }
                        }
                        p.WaitForExit();
                    }
                }

                // Fallback lookup by video ID in case Windows console ANSI/emoji filename mangling prevented exact stdout match
                if (!ct.IsCancellationRequested && (string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath)))
                {
                    try
                    {
                        var vidExts = new[] { ".mp4", ".mkv", ".webm" };
                        var candidates = Directory.GetFiles(outDir, !string.IsNullOrEmpty(vid) ? ("*" + vid + "*.*") : "*.*")
                            .Where(f => vidExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                            .ToList();
                        if (candidates.Count > 0) finalPath = candidates[0];
                        else
                        {
                            var recent = Directory.GetFiles(outDir, "*.*")
                                .Where(f => vidExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()) && File.GetLastWriteTimeUtc(f) >= startedUtc)
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .FirstOrDefault();
                            if (!string.IsNullOrEmpty(recent)) finalPath = recent;
                        }
                    }
                    catch { }
                }

                // Lossless merge of any separate video and audio streams (e.g. 4K DASH streams like .f401.mp4 + .f140.m4a)
                try
                {
                    string targetVid = !string.IsNullOrEmpty(vid) ? vid : "";
                    var allOutFiles = Directory.GetFiles(outDir, "*.*");

                    // Check for video fragments matching .f<number>
                    var vFrags = allOutFiles.Where(f =>
                    {
                        string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".mp4" && ext != ".webm" && ext != ".mkv") return false;
                        string fn = System.IO.Path.GetFileNameWithoutExtension(f);
                        return Regex.IsMatch(fn, @"\.f\d+$");
                    }).ToList();

                    if (!string.IsNullOrEmpty(finalPath) && Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(finalPath), @"\.f\d+$") && !vFrags.Contains(finalPath))
                    {
                        vFrags.Add(finalPath);
                    }

                    foreach (var vf in vFrags)
                    {
                        string fn = System.IO.Path.GetFileNameWithoutExtension(vf);
                        string baseClean = Regex.Replace(fn, @"\.f\d+$", "").Trim();
                        string targetMerged = System.IO.Path.Combine(outDir, baseClean + ".mp4");

                        // Find corresponding audio fragment
                        var af = allOutFiles.FirstOrDefault(a =>
                        {
                            if (string.Equals(a, vf, StringComparison.OrdinalIgnoreCase)) return false;
                            string aExt = System.IO.Path.GetExtension(a).ToLowerInvariant();
                            if (aExt != ".m4a" && aExt != ".opus" && aExt != ".webm" && aExt != ".aac" && aExt != ".mp3") return false;
                            string aFn = System.IO.Path.GetFileNameWithoutExtension(a);
                            return aFn.StartsWith(baseClean, StringComparison.OrdinalIgnoreCase);
                        });

                        if (af != null && File.Exists(af))
                        {
                            if (onProgress != null) onProgress("Merging 4K video & audio tracks (lossless)...", 97);
                            if (MergeMediaStreams(vf, af, targetMerged))
                            {
                                try { File.Delete(vf); } catch { }
                                try { File.Delete(af); } catch { }
                                if (string.IsNullOrEmpty(finalPath) || string.Equals(finalPath, vf, StringComparison.OrdinalIgnoreCase) || !File.Exists(finalPath))
                                {
                                    finalPath = targetMerged;
                                }
                            }
                        }
                        else if (!File.Exists(targetMerged) && File.Exists(vf))
                        {
                            try { File.Move(vf, targetMerged); finalPath = targetMerged; } catch { }
                        }
                        else if (File.Exists(targetMerged))
                        {
                            finalPath = targetMerged;
                        }
                    }
                }
                catch { }

                // Clean up any remaining temporary files (.part, .ytdl, orphaned .f140 etc.)
                try
                {
                    if (!string.IsNullOrEmpty(vid))
                    {
                        var leftovers = Directory.GetFiles(outDir, "*" + vid + "*.*");
                        foreach (var lf in leftovers)
                        {
                            string lExt = System.IO.Path.GetExtension(lf).ToLowerInvariant();
                            string lName = System.IO.Path.GetFileNameWithoutExtension(lf);
                            if (lExt == ".part" || lExt == ".ytdl" || Regex.IsMatch(lName, @"\.f\d+$"))
                            {
                                try { File.Delete(lf); } catch { }
                            }
                        }
                    }
                }
                catch { }

                if (ct.IsCancellationRequested || string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath))
                    return null;

                // playlists.txt uses '|' delimiter, so sanitize any pipe from filename
                string dirName = System.IO.Path.GetDirectoryName(finalPath);
                string fileName = System.IO.Path.GetFileName(finalPath);
                if (fileName.Contains("|"))
                {
                    string safePath = System.IO.Path.Combine(dirName, fileName.Replace("|", "-"));
                    try { File.Move(finalPath, safePath); finalPath = safePath; } catch { }
                }
                return finalPath;
            });
        }
    }

    static class Mp4Info
    {
        static uint U32(byte[] b, int o) { return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]); }
        static ulong U64(byte[] b, int o) { return ((ulong)U32(b, o) << 32) | U32(b, o + 4); }
        public static TimeSpan GetDuration(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    byte[] h = new byte[16];
                    long len = fs.Length, pos = 0;
                    while (pos + 8 <= len)
                    {
                        fs.Position = pos;
                        if (fs.Read(h, 0, 8) < 8) break;
                        ulong sz = U32(h, 0);
                        string tp = "" + (char)h[4] + (char)h[5] + (char)h[6] + (char)h[7];
                        int hdr = 8;
                        if (sz == 1) { if (fs.Read(h, 8, 8) < 8) break; sz = U64(h, 8); hdr = 16; }
                        else if (sz == 0) sz = (ulong)(len - pos);
                        if (tp == "moov")
                        {
                            int moovLen = (int)Math.Min((ulong)4 * 1024 * 1024, sz - (ulong)hdr);
                            byte[] mb = new byte[moovLen];
                            fs.Read(mb, 0, moovLen);
                            for (int i = 0; i + 32 < moovLen; i++)
                            {
                                if (mb[i] == 'm' && mb[i + 1] == 'v' && mb[i + 2] == 'h' && mb[i + 3] == 'd')
                                {
                                    int v = mb[i + 4];
                                    if (v == 0)
                                    {
                                        uint ts = U32(mb, i + 16), d = U32(mb, i + 20);
                                        if (ts > 0) return TimeSpan.FromSeconds((double)d / ts);
                                    }
                                    else if (v == 1 && i + 36 <= moovLen)
                                    {
                                        uint ts = U32(mb, i + 24); ulong d = U64(mb, i + 28);
                                        if (ts > 0) return TimeSpan.FromSeconds((double)d / ts);
                                    }
                                }
                            }
                            break;
                        }
                        if (sz < (ulong)hdr) break;
                        pos += (long)sz;
                    }
                }
            }
            catch { }
            return TimeSpan.Zero;
        }
    }

    static class Native
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetDllDirectory(string lpPathName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr OpenEventW(uint dwDesiredAccess, bool bInheritHandle, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetEvent(IntPtr hEvent);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ResetEvent(IntPtr hEvent);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("Windows.UI.Xaml.dll", CharSet = CharSet.Unicode)] public static extern int InitializeXamlDiagnosticsEx(string endPointName, uint pid, string wszDllXamlDiagnostics, string wszTAPDllName, Guid tapClsid, string wszInitializationData);

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttribData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr c, IntPtr p);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint k, byte a, uint f);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int sz);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT val, int sz);

        // libvlc native hardware D3D11VA playback
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr libvlc_new(int argc, [In] string[] argv);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr libvlc_media_new_path(IntPtr inst, byte[] pathUtf8);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr libvlc_media_new_location(IntPtr inst, byte[] urlUtf8);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_add_option(IntPtr media, byte[] optionsUtf8);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_release(IntPtr media);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr libvlc_media_player_new(IntPtr inst);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_release(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_set_media(IntPtr mp, IntPtr media);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_set_hwnd(IntPtr mp, IntPtr hwnd);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int libvlc_media_player_play(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_set_pause(IntPtr mp, int do_pause);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_stop(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_media_player_set_time(IntPtr mp, long time);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern long libvlc_media_player_get_time(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern long libvlc_media_player_get_length(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int libvlc_media_player_get_state(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int libvlc_audio_get_mute(IntPtr mp);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int libvlc_audio_set_mute(IntPtr mp, int status);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int libvlc_audio_set_volume(IntPtr mp, int volume);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_video_set_aspect_ratio(IntPtr mp, byte[] aspect);
        [DllImport("libvlc.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void libvlc_video_set_crop_geometry(IntPtr mp, byte[] geom);
    }

    static class StartupHelper
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppVal = "LiveDesk";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    var v = k.GetValue(AppVal) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enable)
                    {
                        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        k.SetValue(AppVal, "\"" + exe + "\" --tray");
                    }
                    else
                    {
                        k.DeleteValue(AppVal, false);
                    }
                    k.DeleteValue("LivelyStudio", false);
                    k.DeleteValue("LivelyMock", false);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Inbuilt Native Windows 11 Taskbar Transparency Engine (Zero TranslucentTB Dependency).
    /// Uses our native TaskbarTap.dll (InitializeXamlDiagnosticsEx) to set Opacity=0 on explorer.exe's
    /// XAML BackgroundFill & BackgroundStroke + DWM SetWindowCompositionAttribute on Shell_TrayWnd.
    /// </summary>
    static class TaskbarHelper
    {
        static readonly Guid TapClsid = new Guid("8F9D4A62-3E1B-4C7A-9D2E-5B6A8C1F0E34");
        static IntPtr clearEvent;
        static bool enabled;
        static uint lastInjectedPid;
        static bool injecting;

        static void EnsureEvent()
        {
            if (clearEvent == IntPtr.Zero)
                clearEvent = Native.CreateEventW(IntPtr.Zero, true, false, "Local\\LivelyMockTaskbarClear");
        }

        public static bool IsRunning()
        {
            return enabled;
        }

        static void StopLegacyTranslucentTB()
        {
            try
            {
                var procs = Process.GetProcessesByName("TranslucentTB");
                if (procs.Length == 0) return;
                var pids = new HashSet<uint>(procs.Select(p => (uint)p.Id));
                Native.EnumWindows((h, l) =>
                {
                    uint pid; Native.GetWindowThreadProcessId(h, out pid);
                    if (pids.Contains(pid)) Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
                IntPtr msgChild = IntPtr.Zero;
                while ((msgChild = Native.FindWindowEx((IntPtr)(-3), msgChild, null, null)) != IntPtr.Zero)
                {
                    uint pid; Native.GetWindowThreadProcessId(msgChild, out pid);
                    if (pids.Contains(pid)) Native.PostMessage(msgChild, 0x0010, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }

        static void ApplyDwmAccent(IntPtr hwnd, bool makeClear)
        {
            if (hwnd == IntPtr.Zero) return;
            var accent = new Native.AccentPolicy
            {
                AccentState = makeClear ? 2 : 0, // 2 = ACCENT_ENABLE_TRANSPARENTGRADIENT, 0 = ACCENT_DISABLED
                AccentFlags = 2,
                GradientColor = 0x00000000,
                AnimationId = 0
            };
            int sz = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new Native.WindowCompositionAttribData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = sz
                };
                Native.SetWindowCompositionAttribute(hwnd, ref data);
                if (!makeClear)
                {
                    Native.PostMessage(hwnd, 0x031A /*WM_THEMECHANGED*/, IntPtr.Zero, IntPtr.Zero);
                    Native.PostMessage(hwnd, 0x031E /*WM_DWMCOMPOSITIONCHANGED*/, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        static void ApplyAllTrayWindows(bool makeClear)
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) ApplyDwmAccent(tray, makeClear);

            IntPtr sec = IntPtr.Zero;
            while ((sec = Native.FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                ApplyDwmAccent(sec, makeClear);
            }
        }

        static void EnsureTapInjectedAsync()
        {
            if (injecting) return;
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            uint pid;
            Native.GetWindowThreadProcessId(tray, out pid);
            if (pid == 0) return;

            IntPtr hActive = Native.OpenEventW(0x00100000 /*SYNCHRONIZE*/, false, "Local\\LivelyMockTapActive_" + pid);
            if (hActive != IntPtr.Zero)
            {
                Native.CloseHandle(hActive);
                lastInjectedPid = pid;
                return;
            }
            if (lastInjectedPid == pid) return;

            string dllPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TaskbarTap.dll");
            if (!File.Exists(dllPath)) return;

            injecting = true;
            lastInjectedPid = pid;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        int hr = Native.InitializeXamlDiagnosticsEx("VisualDiagConnection" + i, pid, "Windows.UI.Xaml.dll", dllPath, TapClsid, "");
                        if (hr >= 0) break;
                    }
                }
                catch { }
                finally { injecting = false; }
            });
        }

        public static void SetTransparent(bool enable)
        {
            try
            {
                EnsureEvent();
                StopLegacyTranslucentTB();
                enabled = enable;
                if (enable)
                {
                    Native.SetEvent(clearEvent);
                    EnsureTapInjectedAsync();
                    ApplyAllTrayWindows(true);
                }
                else
                {
                    Native.ResetEvent(clearEvent);
                    ApplyAllTrayWindows(false);
                }
            }
            catch { }
        }

        public static void Tick()
        {
            if (!enabled) return;
            try
            {
                EnsureTapInjectedAsync();
                ApplyAllTrayWindows(true);
            }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                if (clearEvent != IntPtr.Zero)
                {
                    Native.ResetEvent(clearEvent);
                    ApplyAllTrayWindows(false);
                    Native.CloseHandle(clearEvent);
                    clearEvent = IntPtr.Zero;
                }
            }
            catch { }
        }
    }

    class Canvas : Forms.Form
    {
        public Draw.Image PhotoImage;
        public int FitMode;
        public Canvas()
        {
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Draw.Color.Black;
            StartPosition = Forms.FormStartPosition.Manual;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            if (PhotoImage == null) return;
            int cw = ClientSize.Width, ch = ClientSize.Height;
            int iw = PhotoImage.Width, ih = PhotoImage.Height;
            if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return;
            e.Graphics.Clear(Draw.Color.Black);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            Draw.Rectangle dest;
            if (FitMode == 2)
            {
                dest = new Draw.Rectangle(0, 0, cw, ch);
            }
            else
            {
                double sx = (double)cw / iw, sy = (double)ch / ih;
                double s = (FitMode == 0) ? Math.Max(sx, sy) : Math.Min(sx, sy);
                int dw = (int)Math.Round(iw * s), dh = (int)Math.Round(ih * s);
                dest = new Draw.Rectangle((cw - dw) / 2, (ch - dh) / 2, dw, dh);
            }
            e.Graphics.DrawImage(PhotoImage, dest);
        }
        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    class WallHost
    {
        static IntPtr vlcInst;
        Canvas F;
        IntPtr mp;
        string curPath;
        int W, H, vol = 70, fit;
        bool userMute, userPaused, streamConfigured, endFired, isPhotoMode;
        int resourceState = 0; // 0 = Desktop/Start Focused, 1 = Other App Focused (BG Visible), 2 = Desktop Covered
        public int RuleOtherApp = 0; // 0 = Video ON + Mute Audio (default), 1 = Video + Audio ON, 2 = Pause Both
        public int RuleCovered = 0;  // 0 = Pause Both (default), 1 = Keep Playing
        public int OpenAppsCount;
        public double ScreenCoveredPercent;
        DispatcherTimer fastTimer, watchTimer;
        IntPtr defViewHandle, childWorkerHandle;
        public IntPtr MainWinHandle, DeskHudHandle;
        public Action Ended, Failed, Opened;

        public int ResourceState { get { return resourceState; } }
        public bool IsEcoPaused { get { return ShouldPauseVideo; } }

        bool ShouldPauseVideo
        {
            get
            {
                if (isPhotoMode) return false;
                if (userPaused) return true;
                if (resourceState == 2 && RuleCovered == 0) return true;
                if (resourceState == 1 && RuleOtherApp == 2) return true;
                return false;
            }
        }

        bool ShouldMuteAudio
        {
            get
            {
                if (isPhotoMode || userMute || ShouldPauseVideo) return true;
                if (resourceState == 1 && RuleOtherApp != 1) return true;
                if (resourceState == 2 && RuleOtherApp != 1) return true;
                return false;
            }
        }

        public string StateLabel
        {
            get
            {
                if (isPhotoMode) return "Photo";
                if (userPaused) return "Paused";
                if (ShouldPauseVideo) return "Paused";
                if (ShouldMuteAudio) return "Muted";
                return "Playing";
            }
        }

        public Brush StateBadgeBrush
        {
            get
            {
                if (isPhotoMode) return Theme.AccentGreen;
                if (userPaused) return Theme.TextMuted;
                if (ShouldPauseVideo) return Theme.Accent;
                if (ShouldMuteAudio) return Theme.AccentAmber;
                return Theme.AccentGreen;
            }
        }

        static readonly object vlcLock = new object();
        static Task<IntPtr> vlcInitTask;

        static void Log(string m) { try { File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDir, "log.txt"), DateTime.Now + " " + m + "\r\n"); } catch { } }

        public static bool IsVlcReady { get { return vlcInst != IntPtr.Zero; } }

        public static Task<IntPtr> EnsureVlcAsync()
        {
            if (vlcInst != IntPtr.Zero) return Task.FromResult(vlcInst);
            lock (vlcLock)
            {
                if (vlcInitTask == null) vlcInitTask = Task.Run(() => EnsureVlc());
                return vlcInitTask;
            }
        }

        public static IntPtr EnsureVlc()
        {
            lock (vlcLock)
            {
                if (vlcInst == IntPtr.Zero)
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string[] candidates =
                    {
                        System.IO.Path.Combine(baseDir, "vlc"),
                        System.IO.Path.Combine(baseDir, "libvlc"),
                        @"C:\Program Files\VideoLAN\VLC",
                        @"C:\Program Files (x86)\VideoLAN\VLC"
                    };
                    string vlcDir = candidates.FirstOrDefault(d => Directory.Exists(d) && File.Exists(System.IO.Path.Combine(d, "libvlc.dll")));
                    if (string.IsNullOrEmpty(vlcDir))
                    {
                        if (Application.Current != null && Application.Current.Dispatcher != null)
                            Application.Current.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show("Video playback engine (vlc\\libvlc.dll) not found.\nKeep the 'vlc' folder next to LiveDesk.exe or install 64-bit VLC Media Player.", "LiveDesk")));
                        return IntPtr.Zero;
                    }
                    string plugDir = System.IO.Path.Combine(vlcDir, "plugins");
                    if (Directory.Exists(plugDir)) Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugDir);
                    Native.SetDllDirectory(vlcDir);
                    string[] args = { "--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--no-stats", "--no-sub-autodetect-file" };
                    vlcInst = Native.libvlc_new(args.Length, args);
                }
                return vlcInst;
            }
        }

        public WallHost()
        {
            EnsureVlc();
            var b = Forms.Screen.PrimaryScreen.Bounds; W = b.Width; H = b.Height;
            F = new Canvas { Bounds = new Draw.Rectangle(0, 0, W, H) };
            F.Show();
            Attach(F.Handle);

            mp = Native.libvlc_media_player_new(vlcInst);
            Native.libvlc_media_player_set_hwnd(mp, F.Handle);

            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            fastTimer.Tick += (s, e) => TickPlayer();
            fastTimer.Start();

            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            watchTimer.Tick += (s, e) => Watch();
            watchTimer.Start();
        }

        public void Play(string path) { PlayMedia(path, null); }

        static Draw.Image LoadGdiImage(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                try
                {
                    var ms = new MemoryStream(bytes);
                    return Draw.Image.FromStream(ms);
                }
                catch
                {
                    using (var msIn = new MemoryStream(bytes))
                    {
                        var dec = BitmapDecoder.Create(msIn, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(dec.Frames[0]);
                        var msPng = new MemoryStream();
                        enc.Save(msPng);
                        msPng.Position = 0;
                        return Draw.Image.FromStream(msPng);
                    }
                }
            }
            catch { return null; }
        }

        public void PlayMedia(string pathOrUrl, string audioSlaveUrl = null)
        {
            if (mp == IntPtr.Zero || string.IsNullOrEmpty(pathOrUrl)) return;
            bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            if (!isUrl && Item.IsStaticPhotoPath(pathOrUrl))
            {
                Draw.Image img = LoadGdiImage(pathOrUrl);
                if (img == null) { endFired = true; if (Failed != null) Failed(); return; }
                try { Native.libvlc_media_player_stop(mp); } catch { }
                isPhotoMode = true;
                curPath = pathOrUrl;
                endFired = false;
                streamConfigured = true;
                if (F.PhotoImage != null) { try { F.PhotoImage.Dispose(); } catch { } }
                F.PhotoImage = img;
                F.FitMode = fit;
                F.Invalidate();
                if (Opened != null) Opened();
                return;
            }

            isPhotoMode = false;
            if (F.PhotoImage != null)
            {
                try { F.PhotoImage.Dispose(); } catch { }
                F.PhotoImage = null;
                F.Invalidate();
            }

            int st = Native.libvlc_media_player_get_state(mp);
            if (curPath == pathOrUrl && string.IsNullOrEmpty(audioSlaveUrl) && (st == 3 || st == 4))
            {
                endFired = false;
                Native.libvlc_media_player_set_time(mp, 0);
                ApplyPlayState();
                return;
            }
            curPath = pathOrUrl;
            endFired = false;
            streamConfigured = false;
            byte[] utf8 = Encoding.UTF8.GetBytes(pathOrUrl + "\0");
            IntPtr m = isUrl ? Native.libvlc_media_new_location(vlcInst, utf8) : Native.libvlc_media_new_path(vlcInst, utf8);
            if (!string.IsNullOrEmpty(audioSlaveUrl))
                Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":input-slave=" + audioSlaveUrl + "\0"));
            if (isUrl)
                Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":network-caching=1500\0"));
            if (!isUrl && Item.IsGifPath(pathOrUrl))
                Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":input-repeat=65535\0"));
            Native.libvlc_media_player_set_media(mp, m);
            Native.libvlc_media_release(m);
            Native.libvlc_media_player_play(mp);
            ApplyAudioNow();
            ApplyFitNow();
            if (ShouldPauseVideo) Native.libvlc_media_player_set_pause(mp, 1);
        }

        public void Preload(string path) { }

        public void Seek(double seconds)
        {
            if (isPhotoMode || mp == IntPtr.Zero) return;
            long len = Native.libvlc_media_player_get_length(mp);
            long target = (long)(seconds * 1000);
            if (len > 0 && target >= len - 250) target = Math.Max(0, len - 300);
            Native.libvlc_media_player_set_time(mp, Math.Max(0, target));
        }

        public void Restart()
        {
            if (isPhotoMode || mp == IntPtr.Zero || curPath == null) return;
            endFired = false;
            int st = Native.libvlc_media_player_get_state(mp);
            if (st == 3 || st == 4)
            {
                Native.libvlc_media_player_set_time(mp, 0);
                ApplyPlayState();
            }
            else Play(curPath);
        }

        public void Pause() { userPaused = true; ApplyPlayState(); }
        public void Resume() { userPaused = false; ApplyPlayState(); }

        public void ApplyPlayState()
        {
            if (isPhotoMode || mp == IntPtr.Zero) return;
            int st = Native.libvlc_media_player_get_state(mp);
            if (ShouldPauseVideo)
            {
                if (st == 3) Native.libvlc_media_player_set_pause(mp, 1);
            }
            else
            {
                if (st == 4) Native.libvlc_media_player_set_pause(mp, 0);
                else if (st != 3 && curPath != null) Native.libvlc_media_player_play(mp);
            }
            ApplyAudioNow();
        }

        public void SetAudio(bool mute, int v) { userMute = mute; vol = v; ApplyAudioNow(); }

        void ApplyAudioNow()
        {
            if (mp == IntPtr.Zero) return;
            bool m = ShouldMuteAudio;
            Native.libvlc_audio_set_mute(mp, m ? 1 : 0);
            Native.libvlc_audio_set_volume(mp, m ? 0 : vol);
        }

        public void SetFit(int f) { fit = f; F.FitMode = f; if (isPhotoMode) F.Invalidate(); else ApplyFitNow(); }

        void ApplyFitNow()
        {
            if (isPhotoMode || mp == IntPtr.Zero) return;
            byte[] ratio = Encoding.ASCII.GetBytes(W + ":" + H + "\0");
            if (fit == 0)
            {
                Native.libvlc_video_set_aspect_ratio(mp, null);
                Native.libvlc_video_set_crop_geometry(mp, ratio);
            }
            else if (fit == 1)
            {
                Native.libvlc_video_set_crop_geometry(mp, null);
                Native.libvlc_video_set_aspect_ratio(mp, null);
            }
            else
            {
                Native.libvlc_video_set_crop_geometry(mp, null);
                Native.libvlc_video_set_aspect_ratio(mp, ratio);
            }
        }

        public double Pos
        {
            get
            {
                if (isPhotoMode || mp == IntPtr.Zero) return 0;
                long t = Native.libvlc_media_player_get_time(mp);
                return t > 0 ? t / 1000.0 : 0;
            }
        }

        public double Dur
        {
            get
            {
                if (isPhotoMode || mp == IntPtr.Zero) return 0;
                long d = Native.libvlc_media_player_get_length(mp);
                return d > 0 ? d / 1000.0 : 0;
            }
        }

        void TickPlayer()
        {
            if (isPhotoMode || mp == IntPtr.Zero || curPath == null) return;
            int st = Native.libvlc_media_player_get_state(mp);
            if (st == 7)
            {
                Log("vlc-failed " + curPath);
                if (!endFired) { endFired = true; if (Failed != null) Failed(); }
                return;
            }
            long t = Native.libvlc_media_player_get_time(mp);
            long len = Native.libvlc_media_player_get_length(mp);
            if (st == 3 && !streamConfigured)
            {
                streamConfigured = true;
                ApplyAudioNow();
                ApplyFitNow();
                if (ShouldPauseVideo) Native.libvlc_media_player_set_pause(mp, 1);
                if (Opened != null) Opened();
            }
            else if (st == 3 && ShouldPauseVideo)
            {
                Native.libvlc_media_player_set_pause(mp, 1);
            }
            if (!endFired && ((len > 600 && t >= len - 200) || st == 6))
            {
                endFired = true;
                if (Ended != null) Ended();
            }
        }

        static bool IsShellOrStartProcess(uint pid, out bool isStartOrSearch)
        {
            isStartOrSearch = false;
            try
            {
                string n = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                if (n == "startmenuexperiencehost" || n == "searchhost" || n == "searchui" || n == "shellexperiencehost")
                {
                    isStartOrSearch = true;
                    return true;
                }
                return n == "textinputhost" || n == "translucenttb" || n == "widgets" || n == "lockapp";
            }
            catch { return false; }
        }

        static bool IsIgnoredShellClass(string c)
        {
            return c == "Progman" || c == "WorkerW" || c == "SHELLDLL_DefView"
                || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
                || c == "XamlExplorerHostIslandWindow" || c == "Windows.UI.Core.CoreWindow"
                || c == "EdgeUiInputTopWndClass" || c == "ApplicationManager_ImmersiveShellWindow"
                || c == "ForegroundStaging" || c == "DummyDWMListenerWindow"
                || c == "MultitaskingViewFrame";
        }

        static bool GetAccurateWindowRect(IntPtr h, out Native.RECT rc)
        {
            if (Native.DwmGetWindowAttribute(h, 9, out rc, Marshal.SizeOf(typeof(Native.RECT))) == 0)
                return true;
            return Native.GetWindowRect(h, out rc);
        }

        public void WatchNow() { Watch(); }

        void Watch()
        {
            int newState = 0;
            try
            {
                if (defViewHandle != IntPtr.Zero)
                {
                    Native.SetWindowPos(F.Handle, defViewHandle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    if (childWorkerHandle != IntPtr.Zero)
                        Native.SetWindowPos(childWorkerHandle, F.Handle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                }

                uint myPid = (uint)Process.GetCurrentProcess().Id;
                IntPtr fg = Native.GetForegroundWindow();
                bool fgIsTrueDesktop = false, fgIsStartMenu = false, fgIsTaskbar = false;
                if (fg == IntPtr.Zero || fg == F.Handle || (DeskHudHandle != IntPtr.Zero && fg == DeskHudHandle))
                {
                    fgIsTrueDesktop = true;
                }
                else
                {
                    var sb = new StringBuilder(64); Native.GetClassName(fg, sb, 64); string fc = sb.ToString();
                    uint fgPid; Native.GetWindowThreadProcessId(fg, out fgPid);
                    bool fgStartProc;
                    bool fgShellProc = IsShellOrStartProcess(fgPid, out fgStartProc);
                    if (fc == "Progman" || fc == "WorkerW" || fc == "SHELLDLL_DefView")
                        fgIsTrueDesktop = true;
                    else if (fgStartProc || fc == "XamlExplorerHostIslandWindow")
                        fgIsStartMenu = true;
                    else if (fc == "Shell_TrayWnd" || fc == "Shell_SecondaryTrayWnd" || fgShellProc)
                        fgIsTaskbar = true;
                }

                var scr = Forms.Screen.FromHandle(F.Handle);
                var wa = scr.WorkingArea;
                var mb = scr.Bounds;
                const int GX = 16, GY = 10;
                bool[,] grid = new bool[GX, GY];
                int openApps = 0;
                bool anyMaximized = false, anyExclusiveFullscreen = false;

                Native.EnumWindows((h, l) =>
                {
                    if (h == F.Handle || (DeskHudHandle != IntPtr.Zero && h == DeskHudHandle)) return true;
                    if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                    int cloaked = 0;
                    if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                    int style = Native.GetWindowLong(h, -16);
                    if ((style & 0x40000000) != 0 || (style & 0x08000000) != 0) return true; // WS_CHILD or WS_DISABLED
                    int ex = Native.GetWindowLong(h, -20);
                    if ((ex & 0x00000080) != 0 || (ex & 0x08000000) != 0 || (ex & 0x00000020) != 0) return true;
                    if ((ex & 0x00080000) != 0)
                    {
                        uint ck, fl; byte al;
                        if (Native.GetLayeredWindowAttributes(h, out ck, out al, out fl) && (fl & 0x02) != 0 && al == 0)
                            return true;
                    }

                    var sb = new StringBuilder(64); Native.GetClassName(h, sb, 64); string c = sb.ToString();
                    if (IsIgnoredShellClass(c)) return true;
                    uint pid; Native.GetWindowThreadProcessId(h, out pid);
                    bool dummyStart;
                    if ((pid == myPid && h != MainWinHandle) || IsShellOrStartProcess(pid, out dummyStart)) return true;

                    Native.RECT rc;
                    if (!GetAccurateWindowRect(h, out rc)) return true;
                    int flft = Math.Max(rc.L, mb.Left), ftop = Math.Max(rc.T, mb.Top);
                    int frgt = Math.Min(rc.R, mb.Right), fbot = Math.Min(rc.B, mb.Bottom);
                    int wFull = frgt - flft, hFull = fbot - ftop;

                    int lft = Math.Max(rc.L, wa.Left), top = Math.Max(rc.T, wa.Top);
                    int rgt = Math.Min(rc.R, wa.Right), bot = Math.Min(rc.B, wa.Bottom);
                    int w = rgt - lft, hh = bot - top;
                    if (w < 160 || hh < 120) return true;

                    bool coversMon = wFull >= mb.Width - 16 && hFull >= mb.Height - 16;
                    bool isMaxOrFull = Native.IsZoomed(h) || coversMon || (w >= wa.Width - 28 && hh >= wa.Height - 28);
                    if (!isMaxOrFull && Native.GetWindowTextLength(h) == 0) return true;

                    openApps++;
                    if (coversMon && (style & 0x00C00000) == 0) anyExclusiveFullscreen = true;
                    if (isMaxOrFull) anyMaximized = true;

                    int x0 = Math.Max(0, (lft - wa.Left) * GX / wa.Width);
                    int x1 = Math.Min(GX - 1, (rgt - wa.Left - 1) * GX / wa.Width);
                    int y0 = Math.Max(0, (top - wa.Top) * GY / wa.Height);
                    int y1 = Math.Min(GY - 1, (bot - wa.Top - 1) * GY / wa.Height);
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                            grid[gx, gy] = true;
                    return true;
                }, IntPtr.Zero);

                int covered = 0;
                for (int gx = 0; gx < GX; gx++)
                    for (int gy = 0; gy < GY; gy++)
                        if (grid[gx, gy]) covered++;
                double covRatio = covered / (double)(GX * GY);
                OpenAppsCount = openApps;
                ScreenCoveredPercent = Math.Round(covRatio * 100.0);

                bool desktopCovered = anyExclusiveFullscreen || anyMaximized || covRatio >= 0.72 || (openApps >= 3 && covRatio >= 0.62);

                if (anyExclusiveFullscreen)
                {
                    newState = 2;
                }
                else if (fgIsStartMenu)
                {
                    // Start menu / Search opened: only play audio if NO background app is running
                    if (desktopCovered)
                        newState = 2;
                    else if (openApps > 0)
                        newState = 1; // App open in background (fullscreen or windowed) -> keep Other App state (muted unless user explicitly chose Keep Audio)
                    else
                        newState = 0; // Pure clean desktop with no background apps
                }
                else if (fgIsTrueDesktop)
                {
                    if (desktopCovered)
                        newState = 2;
                    else
                        newState = 0;
                }
                else if (fgIsTaskbar)
                {
                    if (desktopCovered)
                        newState = 2;
                    else if (openApps > 0)
                        newState = 1;
                    else
                        newState = 0;
                }
                else if (desktopCovered)
                {
                    newState = 2;
                }
                else if (openApps > 0 || !fgIsTaskbar)
                {
                    newState = 1;
                }
                else
                {
                    newState = 0;
                }
            }
            catch { }

            bool stateChanged = newState != resourceState;
            resourceState = newState;

            bool needSync = stateChanged;
            if (!needSync && !isPhotoMode && mp != IntPtr.Zero)
            {
                int st = Native.libvlc_media_player_get_state(mp);
                if ((ShouldPauseVideo && st == 3) || (!ShouldPauseVideo && st == 4)) needSync = true;
                else if (st == 3 && Native.libvlc_audio_get_mute(mp) != (ShouldMuteAudio ? 1 : 0)) needSync = true;
            }
            if (needSync) ApplyPlayState();
        }

        void Attach(IntPtr hwnd)
        {
            IntPtr prog = Native.FindWindow("Progman", null), res;
            Native.SendMessageTimeout(prog, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 250, out res);
            Native.SendMessageTimeout(prog, 0x052C, (IntPtr)0xD, (IntPtr)1, 0, 250, out res);
            IntPtr defView = Native.FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero) { Thread.Sleep(45); defView = Native.FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null); }
            IntPtr childWorker = Native.FindWindowEx(prog, IntPtr.Zero, "WorkerW", null);
            IntPtr worker = IntPtr.Zero, parent, after;
            if (defView != IntPtr.Zero)
            {
                parent = prog;
                after = defView;
                defViewHandle = defView;
                childWorkerHandle = childWorker;
            }
            else
            {
                Native.EnumWindows((h, l) =>
                {
                    if (Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                        worker = Native.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                    return true;
                }, IntPtr.Zero);
                parent = worker != IntPtr.Zero ? worker : prog;
                after = (IntPtr)1;
            }

            int style = Native.GetWindowLong(hwnd, -16);
            style = (style & ~unchecked((int)0x80000000) & ~0x00C00000) | 0x40000000 | 0x10000000 | 0x04000000 | 0x02000000;
            Native.SetWindowLong(hwnd, -16, style);

            int ex = Native.GetWindowLong(hwnd, -20);
            ex = (ex & ~0x00200000) | 0x00080000 | 0x00000080 | 0x08000000;
            Native.SetWindowLong(hwnd, -20, ex);
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, 0x02);

            IntPtr r = Native.SetParent(hwnd, parent);

            Native.SetWindowLong(hwnd, -20, ex);
            bool lwa = Native.SetLayeredWindowAttributes(hwnd, 0, 255, 0x02);
            int exAfter = Native.GetWindowLong(hwnd, -20);

            var b = Forms.Screen.PrimaryScreen.Bounds; var v = Forms.SystemInformation.VirtualScreen;
            int x = defView != IntPtr.Zero ? 0 : (b.X - v.X);
            int y = defView != IntPtr.Zero ? 0 : (b.Y - v.Y);
            bool ok = Native.SetWindowPos(hwnd, after, x, y, b.Width, b.Height, 0x0010 | 0x0040 | 0x0020);
            if (defView != IntPtr.Zero && childWorker != IntPtr.Zero)
                Native.SetWindowPos(childWorker, hwnd, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

            Log("attach-vlc prog=" + prog + " defView=" + defView + " childWorker=" + childWorker + " parent=" + parent + " setparent=" + r + " exAfter=" + exAfter.ToString("X") + " lwa=" + lwa + " setpos=" + ok);
        }

        public void Close()
        {
            try
            {
                fastTimer.Stop();
                watchTimer.Stop();
                if (mp != IntPtr.Zero) { Native.libvlc_media_player_stop(mp); Native.libvlc_media_player_release(mp); mp = IntPtr.Zero; }
                F.Close();
            }
            catch { }
        }
    }

    class MainWin : Window
    {
        Playlist collectionPl = new Playlist { Name = "Collection" };
        List<Playlist> playlists = new List<Playlist>();
        Playlist activePl;
        Playlist currentPl { get { return activePl ?? collectionPl; } }
        ObservableCollection<Item> items { get { return currentPl != null ? currentPl.Items : null; } }
        ObservableCollection<Item> filteredItems = new ObservableCollection<Item>();

        // Main shell columns & panels
        ColumnDefinition colInspector;
        Border inspBorder;

        // Clean Header & Navigation Controls
        TextBlock txtHeaderTitle = new TextBlock(), txtHeaderSub = new TextBlock(), txtNoPlaylistsHint = new TextBlock();
        ListBox lbSidebarPlaylists = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ListBox lbCards = new ListBox();
        Grid viewLibrary, viewSettings;
        Button navBtnLib, navBtnSet, btnPlayMultiLoop;
        TextBlock txtNavColLabel;
        Button filterBtnAll, filterBtnLoop, filterBtnOut, btnSelectAllHeader;
        TextBox tbSearch = new TextBox { Width = 155, VerticalAlignment = VerticalAlignment.Center };
        string filterMode = "all";

        // Cycle Transition Mode (Video End vs Timer HH:MM:SS)
        Button btnCycleEndHeader, btnCycleTimerHeader;
        StackPanel timerPanelHeader;
        TextBox tbHrHeader, tbMinHeader, tbSecHeader;
        bool cycleByTimer = false;
        int timerHours = 0, timerMinutes = 5, timerSeconds = 0;
        double timerRemainingSec = 300;
        DateTime lastTimerTick = DateTime.UtcNow;

        // Contextual Floating Selection Bar
        Border selectionBar;
        TextBlock txtSelCount = new TextBlock();

        // Clean Right Inspector & Live Video Preview Panel
        Item inspectedItem;
        Image imgPreview = new Image { Stretch = Stretch.UniformToFill };
        Border previewThumbBox;
        WinFormsHost previewHost;
        Forms.Panel previewVideoPanel;
        IntPtr previewMp;
        string previewPlayingPath;
        bool previewUserPaused = false;
        TextBlock txtPreviewStatus = new TextBlock(), txtInspTitle = new TextBlock(), txtInspMeta = new TextBlock(), txtInspLoop = new TextBlock(), txtInspOrder = new TextBlock();
        Button btnPreviewToggle, btnInspLoopToggle, btnInspOpen;
        Point cardDragStart, plDragStart;
        Item draggedCardItem, activeDragSrcItem, activeDragHoverItem;
        Playlist draggedPlItem, activeDragSrcPl, activeDragHoverPl;
        Window dragGhostWin;
        Button setFitBtn0, setFitBtn1, setFitBtn2;
        Button ruleOtherBtn0, ruleOtherBtn1, ruleOtherBtn2;
        Button ruleCovBtn0, ruleCovBtn1;
        int fitMode = 0, ruleOtherApp = 0, ruleCovered = 0;

        // Settings controls
        TextBlock txtDlDirSettings = new TextBlock();
        CheckBox swTtbSettings, swStartupSettings, swDeskHudSettings;

        // Desktop Mini Controls HUD (On-Desktop Prev / Play / Next / Seekbar)
        bool deskHudEnabled = true, isDraggingHudSeek = false;
        Window deskHudWin;
        IntPtr deskHudHwnd;
        Button hudBtnPlayPause;
        Slider hudSeekSlider;
        TextBlock hudTxtTitle, hudTxtTime;

        // Bottom Transport & Settings controls
        Image imgNowThumb = new Image { Stretch = Stretch.UniformToFill };
        TextBlock txtNowTitle = new TextBlock(), txtNowSub = new TextBlock(), txtTimeCur = new TextBlock(), txtTimeTot = new TextBlock();
        TextBlock txtVolPct = new TextBlock();
        Slider seekSlider = new Slider { Minimum = 0, Maximum = 100, Value = 0, VerticalAlignment = VerticalAlignment.Center };
        bool isDraggingSeek = false;
        Button btnPlayPause, btnMuteIcon, btnShuffleToggle;
        bool shuffleEnabled = false, muteEnabled = false, ttbEnabled = false, startupEnabled = false;
        Slider vol;

        // Engine state
        WallHost host; List<Item> queue = new List<Item>(); int qi = -1, fails, playReqId; string mode = "loop"; Item cur;
        bool ytResolving = false;
        Forms.NotifyIcon ni; Forms.ToolStripMenuItem trayPlMenu, trayTtbItem, trayStartupItem;
        DispatcherTimer tick; Random rnd = new Random();
        string dir = AppPaths.DataDir;
        string libFile, colFile, plFile, setFile;
        bool loading = true, switchingPl = false, paused = false;

        public MainWin(bool tray)
        {
            Directory.CreateDirectory(dir);
            libFile = System.IO.Path.Combine(dir, "library.txt");
            colFile = System.IO.Path.Combine(dir, "collection.txt");
            plFile = System.IO.Path.Combine(dir, "playlists.txt");
            setFile = System.IO.Path.Combine(dir, "settings.txt");

            Title = "LiveDesk";
            try
            {
                var exeIco = Draw.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (exeIco != null) Icon = Imaging.CreateBitmapSourceFromHIcon(exeIco.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            Width = 1300; Height = 820; MinWidth = 1000; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.BgApp; Foreground = Theme.TextPrimary;
            FontFamily = new FontFamily("Segoe UI");
            AllowDrop = true;
            Drop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) AddPaths(f); };

            BuildRootLayout();
            BuildTray();

            Closing += (s, e) => { e.Cancel = true; StopPreview(); Hide(); };
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized) StopPreview();
                else if (IsVisible && viewLibrary != null && viewLibrary.Visibility == Visibility.Visible && inspectedItem != null && !previewUserPaused)
                    StartPreview(inspectedItem);
            };
            IsVisibleChanged += (s, e) =>
            {
                if (!IsVisible) StopPreview();
                else if (WindowState != WindowState.Minimized && viewLibrary != null && viewLibrary.Visibility == Visibility.Visible && inspectedItem != null && !previewUserPaused)
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        if (IsVisible && WindowState != WindowState.Minimized && viewLibrary.Visibility == Visibility.Visible && inspectedItem != null && !previewUserPaused)
                            StartPreview(inspectedItem);
                    }));
            };
            Loaded += (s, e) =>
            {
                WallHost.EnsureVlcAsync();
                if (IsVisible && WindowState != WindowState.Minimized && viewLibrary != null && viewLibrary.Visibility == Visibility.Visible && inspectedItem != null && !previewUserPaused)
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        if (IsVisible && WindowState != WindowState.Minimized && viewLibrary.Visibility == Visibility.Visible && inspectedItem != null && !previewUserPaused)
                            StartPreview(inspectedItem);
                    }));
            };
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            tick.Tick += (s, e) => UpdateNow();
            tick.Start();

            LoadAll();
            loading = false;
            RefreshPlaylistUi();
            RebuildFilteredItems();
            UpdateSelectionBar();
            if (tray) PlaySet("loop", true);
        }

        // ==================== WINUI 3 FLUENT CONTROLS ====================
        Style MakeBtnStyle(Brush bg, Brush bgHover, Brush bgPress, Brush fg, Brush border, int radius = 7, int padX = 14, int padY = 7)
        {
            var st = new Style(typeof(Button));
            st.Setters.Add(new Setter(Control.BackgroundProperty, bg));
            st.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            st.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(padX, padY, padX, padY)));
            st.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            st.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            st.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var trHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trHover.Setters.Add(new Setter(Control.BackgroundProperty, bgHover, "bd"));
            tpl.Triggers.Add(trHover);

            var trPress = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            trPress.Setters.Add(new Setter(Control.BackgroundProperty, bgPress, "bd"));
            tpl.Triggers.Add(trPress);

            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return st;
        }

        /// <summary>Monochrome Black & White pill toggle switch.</summary>
        CheckBox MakePillSwitch()
        {
            var cb = new CheckBox { Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var tpl = new ControlTemplate(typeof(CheckBox));
            var track = new FrameworkElementFactory(typeof(Border), "track");
            track.SetValue(FrameworkElement.WidthProperty, 44.0);
            track.SetValue(FrameworkElement.HeightProperty, 22.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            track.SetValue(Border.BackgroundProperty, Theme.B(24, 24, 28));
            track.SetValue(Border.BorderBrushProperty, Theme.B(52, 52, 58));
            track.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var thumb = new FrameworkElementFactory(typeof(Border), "thumb");
            thumb.SetValue(FrameworkElement.WidthProperty, 16.0);
            thumb.SetValue(FrameworkElement.HeightProperty, 16.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            thumb.SetValue(Border.BackgroundProperty, Theme.B(130, 130, 138));
            thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            thumb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
            track.AppendChild(thumb);
            tpl.VisualTree = track;

            var trChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            trChecked.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Accent, "track"));
            trChecked.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.White, "track"));
            trChecked.Setters.Add(new Setter(Border.BackgroundProperty, Theme.AccentFg, "thumb"));
            trChecked.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "thumb"));
            trChecked.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0), "thumb"));
            tpl.Triggers.Add(trChecked);

            cb.Template = tpl;
            return cb;
        }

        /// <summary>Sleek Monochrome Black & White Slider template.</summary>
        void StyleModernSlider(Slider sl)
        {
            sl.Height = 22;
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Slider'>
    <Grid VerticalAlignment='Center'>
        <Border Height='4' CornerRadius='2' Background='#26262A' VerticalAlignment='Center'/>
        <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton>
                <RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Height='4' CornerRadius='2' Background='#F5F5F7' VerticalAlignment='Center'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
                <RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>
                    <RepeatButton.Template>
                        <ControlTemplate TargetType='RepeatButton'>
                            <Border Height='18' Background='Transparent' VerticalAlignment='Center'/>
                        </ControlTemplate>
                    </RepeatButton.Template>
                </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
                <Thumb Focusable='False'>
                    <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                            <Border Width='13' Height='13' CornerRadius='6.5' Background='#FFFFFF' BorderBrush='#0A0A0C' BorderThickness='2'/>
                        </ControlTemplate>
                    </Thumb.Template>
                </Thumb>
            </Track.Thumb>
        </Track>
    </Grid>
</ControlTemplate>";
            sl.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        /// <summary>Monochrome circular checkmark badge for wallpaper cards (White circle + Black tick).</summary>
        ControlTemplate MakeCardCheckTemplate()
        {
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='CheckBox'>
    <Border x:Name='circle' Width='24' Height='24' CornerRadius='12'
            Background='#AA0A0A0C' BorderBrush='#77FFFFFF' BorderThickness='1.5'
            Cursor='Hand'>
        <TextBlock x:Name='checkIcon' Text='&#xE73E;'
                   FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets'
                   FontSize='12' FontWeight='Bold' Foreground='#09090B'
                   HorizontalAlignment='Center' VerticalAlignment='Center'
                   Visibility='Collapsed'/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='circle' Property='BorderBrush' Value='#FFFFFFFF'/>
            <Setter TargetName='circle' Property='Background' Value='#CC18181B'/>
        </Trigger>
        <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='circle' Property='Background' Value='#F5F5F7'/>
            <Setter TargetName='circle' Property='BorderBrush' Value='#FFFFFF'/>
            <Setter TargetName='checkIcon' Property='Visibility' Value='Visible'/>
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        Border MakeTimeInputBox(out TextBox tb, string initVal, Action onChanged)
        {
            var bd = new Border
            {
                Background = Theme.B(10, 10, 12), BorderBrush = Theme.B(54, 54, 60), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center
            };
            tb = new TextBox
            {
                Text = initVal, Width = 24, MaxLength = 2, TextAlignment = TextAlignment.Center,
                Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            tb.TextChanged += (s, e) => { if (!loading && onChanged != null) onChanged(); };
            bd.Child = tb;
            return bd;
        }

        Style SegBtnStyle(bool active, int radius = 6, int padX = 11, int padY = 5)
        {
            if (active)
                return MakeBtnStyle(Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Theme.AccentFg, Theme.Accent, radius, padX, padY);
            return MakeBtnStyle(Brushes.Transparent, Theme.BgCardHover, Theme.BgCard, Theme.TextSecondary, Brushes.Transparent, radius, padX, padY);
        }

        Button StyledBtn(string text, Action onClick, bool accent = false, bool danger = false, int padX = 13, int padY = 7, int radius = 7)
        {
            Style st;
            if (accent) st = MakeBtnStyle(Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Theme.AccentFg, Theme.Accent, radius, padX, padY);
            else if (danger) st = MakeBtnStyle(Theme.B(28, 28, 32), Theme.B(44, 44, 50), Theme.B(58, 58, 66), Theme.TextPrimary, Theme.B(68, 68, 76), radius, padX, padY);
            else st = MakeBtnStyle(Theme.BgCard, Theme.BgCardHover, Theme.B(38, 38, 44), Theme.TextPrimary, Theme.BorderSubtle, radius, padX, padY);

            var b = new Button { Content = text, Style = st, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        Button FluentIconBtn(string glyph, string tooltip, Action onClick, bool accent = false, int size = 34, int radius = 8)
        {
            var st = accent
                ? MakeBtnStyle(Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Theme.AccentFg, Theme.Accent, radius, 0, 0)
                : MakeBtnStyle(Theme.BgCard, Theme.BgCardHover, Theme.B(38, 38, 44), Theme.TextPrimary, Theme.BorderSubtle, radius, 0, 0);
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = size, Height = size, Style = st, ToolTip = tooltip, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center
            };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        void BuildRootLayout()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
            Content = root;

            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colInspector = new ColumnDefinition { Width = new GridLength(294) };
            shell.ColumnDefinitions.Add(colInspector);
            Grid.SetRow(shell, 0);
            root.Children.Add(shell);

            BuildSidebar(shell);
            BuildCenterWorkspace(shell);
            BuildInspector(shell);
            BuildBottomTransport(root);
            SwitchTab(0);
        }

        // ==================== MINIMAL MONOCHROME LEFT SIDEBAR ====================
        void BuildSidebar(Grid shell)
        {
            var side = new Border { Background = Theme.BgSidebar, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(0, 0, 1, 0) };
            Grid.SetColumn(side, 0);
            shell.Children.Add(side);

            var dp = new DockPanel { Margin = new Thickness(14, 18, 14, 14) };
            side.Child = dp;

            // Minimal Monochrome Brand
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 22) };
            var logo = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Theme.Accent, Margin = new Thickness(0, 0, 10, 0) };
            logo.Child = new TextBlock { Text = "\uE7F4", FontFamily = Theme.IconFont, FontSize = 15, Foreground = Theme.AccentFg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(logo);
            var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bt.Children.Add(new TextBlock { Text = "LiveDesk", FontSize = 15.0, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary });
            brand.Children.Add(bt);
            DockPanel.SetDock(brand, Dock.Top);
            dp.Children.Add(brand);

            // Navigation: Collection (all wallpapers) & Settings
            var nav = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            navBtnLib = MakeNavBtn("\uE8A9", "Collection (0)", () => { SelectCollection(false); SwitchTab(0); }, out txtNavColLabel);
            TextBlock dummySetLbl;
            navBtnSet = MakeNavBtn("\uE713", "Settings", () => SwitchTab(1), out dummySetLbl);
            nav.Children.Add(navBtnLib);
            nav.Children.Add(navBtnSet);
            DockPanel.SetDock(nav, Dock.Top);
            dp.Children.Add(nav);

            // Playlists Header + "+ New" button
            var plHeader = new DockPanel { Margin = new Thickness(4, 0, 0, 8) };
            var btnAddPl = StyledBtn("＋ New", CreatePlaylistDialog, false, false, 8, 3, 5);
            btnAddPl.FontSize = 11; btnAddPl.Margin = new Thickness(0);
            DockPanel.SetDock(btnAddPl, Dock.Right);
            plHeader.Children.Add(btnAddPl);
            plHeader.Children.Add(new TextBlock { Text = "PLAYLISTS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(plHeader, Dock.Top);
            dp.Children.Add(plHeader);

            txtNoPlaylistsHint.Text = "No playlists yet — click ＋ New";
            txtNoPlaylistsHint.FontSize = 11;
            txtNoPlaylistsHint.Foreground = Theme.TextMuted;
            txtNoPlaylistsHint.Margin = new Thickness(6, 2, 4, 8);
            txtNoPlaylistsHint.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(txtNoPlaylistsHint, Dock.Top);
            dp.Children.Add(txtNoPlaylistsHint);

            lbSidebarPlaylists.Cursor = Cursors.Hand;
            var plDt = new DataTemplate(typeof(Playlist));
            var plBd = new FrameworkElementFactory(typeof(Border));
            plBd.SetBinding(Border.BackgroundProperty, new Binding("CardBg"));
            plBd.SetBinding(Border.BorderBrushProperty, new Binding("CardBorder"));
            plBd.SetBinding(UIElement.OpacityProperty, new Binding("PlOpacity"));
            plBd.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            plBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            plBd.SetValue(Border.PaddingProperty, new Thickness(11, 8, 9, 8));
            plBd.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));

            var plRow = new FrameworkElementFactory(typeof(DockPanel));
            var plMultiChk = new FrameworkElementFactory(typeof(CheckBox));
            plMultiChk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("InMultiLoop") { Mode = BindingMode.TwoWay });
            plMultiChk.SetValue(DockPanel.DockProperty, Dock.Right);
            plMultiChk.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            plMultiChk.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            plMultiChk.SetValue(FrameworkElement.ToolTipProperty, "Include in Multi-Playlist Loop");
            plRow.AppendChild(plMultiChk);

            var plSp = new FrameworkElementFactory(typeof(StackPanel));
            var plNameTxt = new FrameworkElementFactory(typeof(TextBlock));
            plNameTxt.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            plNameTxt.SetValue(TextBlock.ForegroundProperty, Theme.TextPrimary);
            plNameTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            plNameTxt.SetValue(TextBlock.FontSizeProperty, 12.5);
            var plCountTxt = new FrameworkElementFactory(typeof(TextBlock));
            plCountTxt.SetBinding(TextBlock.TextProperty, new Binding("CountText"));
            plCountTxt.SetValue(TextBlock.ForegroundProperty, Theme.TextMuted);
            plCountTxt.SetValue(TextBlock.FontSizeProperty, 10.5);
            plSp.AppendChild(plNameTxt);
            plSp.AppendChild(plCountTxt);
            plRow.AppendChild(plSp);
            plBd.AppendChild(plRow);
            plDt.VisualTree = plBd;
            lbSidebarPlaylists.ItemTemplate = plDt;

            var plItemStyle = new Style(typeof(ListBoxItem));
            plItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            plItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            plItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            var plItemTpl = new ControlTemplate(typeof(ListBoxItem));
            plItemTpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            plItemStyle.Setters.Add(new Setter(Control.TemplateProperty, plItemTpl));
            lbSidebarPlaylists.ItemContainerStyle = plItemStyle;

            var plCm = new ContextMenu();
            var mPlPlay = new MenuItem { Header = "▶ Play Playlist Loop" };
            mPlPlay.Click += (s, e) => PlaySet("loop");
            var mPlMulti = new MenuItem { Header = "▶ Multi-Playlist Loop..." };
            mPlMulti.Click += (s, e) => ShowMultiPlaylistLoopDialog();
            var mPlUp = new MenuItem { Header = "▲ Move Playlist Up" };
            mPlUp.Click += (s, e) => MovePlaylist(activePl, -1);
            var mPlDown = new MenuItem { Header = "▼ Move Playlist Down" };
            mPlDown.Click += (s, e) => MovePlaylist(activePl, 1);
            var mPlRen = new MenuItem { Header = "✎ Rename Playlist..." };
            mPlRen.Click += (s, e) => RenamePlaylistDialog();
            var mPlDel = new MenuItem { Header = "🗑 Delete Playlist" };
            mPlDel.Click += (s, e) => DeleteCurrentPlaylist();
            plCm.Items.Add(mPlPlay); plCm.Items.Add(mPlMulti); plCm.Items.Add(new Separator());
            plCm.Items.Add(mPlUp); plCm.Items.Add(mPlDown); plCm.Items.Add(new Separator());
            plCm.Items.Add(mPlRen); plCm.Items.Add(mPlDel);
            lbSidebarPlaylists.ContextMenu = plCm;

            lbSidebarPlaylists.AllowDrop = true;
            lbSidebarPlaylists.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) { draggedPlItem = null; return; }
                plDragStart = e.GetPosition(null);
                draggedPlItem = FindDataContext<Playlist>(e.OriginalSource as DependencyObject);
            };
            lbSidebarPlaylists.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && draggedPlItem != null)
                {
                    var pos = e.GetPosition(null);
                    if (Math.Abs(pos.X - plDragStart.X) > 6 || Math.Abs(pos.Y - plDragStart.Y) > 6)
                    {
                        var plToDrag = draggedPlItem;
                        draggedPlItem = null;
                        activeDragSrcPl = plToDrag;
                        plToDrag.IsDragging = true;
                        try { DragDrop.DoDragDrop(lbSidebarPlaylists, plToDrag, DragDropEffects.Move); }
                        finally { ClearDragVisuals(); }
                    }
                }
            };
            lbSidebarPlaylists.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(Playlist)))
                {
                    e.Effects = DragDropEffects.Move;
                    var hover = FindDataContext<Playlist>(e.OriginalSource as DependencyObject);
                    if (hover == activeDragSrcPl) hover = null;
                    if (activeDragHoverPl != hover)
                    {
                        if (activeDragHoverPl != null) activeDragHoverPl.IsDragOver = false;
                        activeDragHoverPl = hover;
                        if (activeDragHoverPl != null) activeDragHoverPl.IsDragOver = true;
                    }
                    e.Handled = true;
                }
                else if (e.Data.GetDataPresent(typeof(Item)))
                {
                    var hover = FindDataContext<Playlist>(e.OriginalSource as DependencyObject);
                    e.Effects = hover != null ? DragDropEffects.Move : DragDropEffects.None;
                    if (activeDragHoverPl != hover)
                    {
                        if (activeDragHoverPl != null) activeDragHoverPl.IsDragOver = false;
                        activeDragHoverPl = hover;
                        if (activeDragHoverPl != null) activeDragHoverPl.IsDragOver = true;
                    }
                    e.Handled = true;
                }
            };
            lbSidebarPlaylists.DragLeave += (s, e) =>
            {
                if (activeDragHoverPl != null) { activeDragHoverPl.IsDragOver = false; activeDragHoverPl = null; }
            };
            lbSidebarPlaylists.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(Playlist)))
                {
                    var srcPl = e.Data.GetData(typeof(Playlist)) as Playlist;
                    var dstPl = FindDataContext<Playlist>(e.OriginalSource as DependencyObject);
                    ClearDragVisuals();
                    if (srcPl != null && dstPl != null && srcPl != dstPl)
                    {
                        int oldIdx = playlists.IndexOf(srcPl);
                        int newIdx = playlists.IndexOf(dstPl);
                        if (oldIdx >= 0 && newIdx >= 0)
                        {
                            playlists.RemoveAt(oldIdx);
                            playlists.Insert(newIdx, srcPl);
                            SaveLib();
                            RefreshPlaylistUi();
                        }
                    }
                    e.Handled = true;
                }
                else if (e.Data.GetDataPresent(typeof(Item)))
                {
                    var srcIt = e.Data.GetData(typeof(Item)) as Item;
                    var dstPl = FindDataContext<Playlist>(e.OriginalSource as DependencyObject);
                    ClearDragVisuals();
                    if (srcIt != null && dstPl != null)
                    {
                        if (FindDuplicateByHash(dstPl, srcIt.Path, srcIt.IsYouTube ? srcIt.YtVideoId : null) == null)
                        {
                            AddItemToPlaylist(dstPl, srcIt.Path, true);
                            SaveLib();
                            RefreshPlaylistUi();
                        }
                    }
                    e.Handled = true;
                }
            };

            lbSidebarPlaylists.SelectionChanged += (s, e) =>
            {
                if (switchingPl) return;
                var pl = lbSidebarPlaylists.SelectedItem as Playlist;
                if (pl != null) { SwitchTab(0); SelectPlaylist(pl, !loading); }
            };
            btnPlayMultiLoop = StyledBtn("▶  Multi-Playlist Loop", StartMultiLoop, false, false, 12, 8, 6);
            btnPlayMultiLoop.Margin = new Thickness(0, 8, 0, 0);
            btnPlayMultiLoop.ToolTip = "Tick playlists above and click to loop across multiple playlists";
            DockPanel.SetDock(btnPlayMultiLoop, Dock.Bottom);
            dp.Children.Add(btnPlayMultiLoop);
            dp.Children.Add(lbSidebarPlaylists);
        }

        Button MakeNavBtn(string iconGlyph, string label, Action onClick, out TextBlock lblBlock)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = iconGlyph, FontFamily = Theme.IconFont, FontSize = 14, Width = 24, VerticalAlignment = VerticalAlignment.Center });
            lblBlock = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(lblBlock);
            var b = new Button
            {
                Content = sp, HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 5),
                Style = SegBtnStyle(false, 8, 12, 9)
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        void SwitchTab(int idx)
        {
            viewLibrary.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            viewSettings.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            navBtnLib.Style = SegBtnStyle(idx == 0 && activePl == null, 8, 12, 9);
            navBtnSet.Style = SegBtnStyle(idx == 1, 8, 12, 9);
            if (colInspector != null && inspBorder != null)
            {
                colInspector.Width = idx == 0 ? new GridLength(294) : new GridLength(0);
                inspBorder.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (idx == 0)
            {
                if (inspectedItem != null && !previewUserPaused && IsVisible && WindowState != WindowState.Minimized)
                    StartPreview(inspectedItem);
            }
            else StopPreview();
        }

        // ==================== CENTER WORKSPACE ====================
        void BuildCenterWorkspace(Grid shell)
        {
            var centerHost = new Grid();
            Grid.SetColumn(centerHost, 1);
            shell.Children.Add(centerHost);

            viewLibrary = new Grid();
            viewSettings = new Grid { Visibility = Visibility.Collapsed };
            centerHost.Children.Add(viewLibrary);
            centerHost.Children.Add(viewSettings);

            BuildLibraryView();
            BuildSettingsView();
        }

        void ToggleSelectAll()
        {
            if (filteredItems == null || filteredItems.Count == 0) return;
            bool allChecked = filteredItems.All(i => i.Checked);
            foreach (var i in filteredItems) i.Checked = !allChecked;
            UpdateSelectionBar();
        }

        void BuildLibraryView()
        {
            viewLibrary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            viewLibrary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            viewLibrary.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border
            {
                Background = Theme.BgHeader, BorderBrush = Theme.BorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 13, 20, 12)
            };
            var headerStack = new StackPanel();

            // ==================== TIER 1: PLAYLIST TITLE + SEARCH & PRIMARY ACTIONS ====================
            var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 11) };

            var topLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            txtHeaderTitle.Text = "Default";
            txtHeaderTitle.FontSize = 19; txtHeaderTitle.FontWeight = FontWeights.Bold; txtHeaderTitle.Foreground = Theme.TextPrimary;
            txtHeaderTitle.VerticalAlignment = VerticalAlignment.Center;
            topLeft.Children.Add(txtHeaderTitle);

            var subPill = new Border
            {
                Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            txtHeaderSub.Text = "14 wallpapers";
            txtHeaderSub.FontSize = 11.5; txtHeaderSub.Foreground = Theme.TextSecondary;
            subPill.Child = txtHeaderSub;
            topLeft.Children.Add(subPill);

            var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(topRight, Dock.Right);

            var searchBorder = new Border
            {
                Background = Theme.BgInput, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var searchSp = new StackPanel { Orientation = Orientation.Horizontal };
            searchSp.Children.Add(new TextBlock { Text = "\uE721  ", FontFamily = Theme.IconFont, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            tbSearch.Width = 175;
            tbSearch.Background = Brushes.Transparent; tbSearch.Foreground = Theme.TextPrimary; tbSearch.BorderThickness = new Thickness(0); tbSearch.CaretBrush = Brushes.White;
            tbSearch.TextChanged += (s, e) => RebuildFilteredItems();
            searchSp.Children.Add(tbSearch);
            searchBorder.Child = searchSp;
            topRight.Children.Add(searchBorder);

            var btnYt = StyledBtn("YouTube", ShowYouTubeDialog, false, false, 12, 7);
            btnYt.ToolTip = "Add wallpaper from a YouTube link";
            topRight.Children.Add(btnYt);

            var btnAdd = StyledBtn("＋ Add ▾", null, false, false, 12, 7);
            btnAdd.Click += (s, e) =>
            {
                var cm = new ContextMenu();
                if (activePl != null)
                {
                    var mFromCol = new MenuItem { Header = "Add from Collection..." };
                    mFromCol.Click += (s2, e2) => ShowAddFromCollectionDialog(activePl);
                    cm.Items.Add(mFromCol);
                    cm.Items.Add(new Separator());
                }
                var mFiles = new MenuItem { Header = "Add Videos or Photos..." };
                mFiles.Click += (s2, e2) => AddFiles();
                var mFolder = new MenuItem { Header = "Add Folder..." };
                mFolder.Click += (s2, e2) => AddFolder();
                var mYt = new MenuItem { Header = "From YouTube URL..." };
                mYt.Click += (s2, e2) => ShowYouTubeDialog();
                cm.Items.Add(mFiles); cm.Items.Add(mFolder); cm.Items.Add(new Separator()); cm.Items.Add(mYt);
                cm.PlacementTarget = btnAdd;
                cm.Placement = PlacementMode.Bottom;
                cm.IsOpen = true;
            };
            topRight.Children.Add(btnAdd);

            var btnPlayLoop = StyledBtn("▶  Play Loop", () => PlaySet("loop"), true, false, 15, 7);
            btnPlayLoop.Margin = new Thickness(0);
            topRight.Children.Add(btnPlayLoop);

            topDock.Children.Add(topRight);
            topDock.Children.Add(topLeft);
            headerStack.Children.Add(topDock);

            // ==================== TIER 2: FILTERS & SELECT ALL (LEFT) + CYCLE TIMER CAPSULE (RIGHT) ====================
            var barDock = new DockPanel();

            var barLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var segBorder = new Border
            {
                Background = Theme.BgInput, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            filterBtnAll = StyledBtn("All", () => SetFilter("all"), true, false, 11, 5, 6);
            filterBtnLoop = StyledBtn("Included", () => SetFilter("loop"), false, false, 11, 5, 6);
            filterBtnOut = StyledBtn("Excluded", () => SetFilter("out"), false, false, 11, 5, 6);
            filterBtnAll.Margin = new Thickness(0, 0, 2, 0);
            filterBtnLoop.Margin = new Thickness(0, 0, 2, 0);
            filterBtnOut.Margin = new Thickness(0);
            segRow.Children.Add(filterBtnAll);
            segRow.Children.Add(filterBtnLoop);
            segRow.Children.Add(filterBtnOut);
            segBorder.Child = segRow;
            barLeft.Children.Add(segBorder);

            btnSelectAllHeader = StyledBtn("✓ Select All", ToggleSelectAll, false, false, 12, 6, 7);
            btnSelectAllHeader.Margin = new Thickness(0, 0, 10, 0);
            barLeft.Children.Add(btnSelectAllHeader);

            var barRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(barRight, Dock.Right);

            barRight.Children.Add(new TextBlock
            {
                Text = "NEXT VIDEO:", FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });

            // Cycle Transition Capsule: [ ↻ Video End | ⏱ Timer  00 : 05 : 00 ▾ ]
            var cycleCapsule = new Border
            {
                Background = Theme.BgInput, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center
            };
            var cycleSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            btnCycleEndHeader = StyledBtn("↻ Video End", () => SetCycleMode(false), true, false, 10, 5, 6);
            btnCycleTimerHeader = StyledBtn("⏱ Timer", () => SetCycleMode(true), false, false, 10, 5, 6);
            btnCycleEndHeader.Margin = new Thickness(0, 0, 2, 0);
            btnCycleTimerHeader.Margin = new Thickness(0);
            cycleSp.Children.Add(btnCycleEndHeader);
            cycleSp.Children.Add(btnCycleTimerHeader);

            timerPanelHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 3, 0), Visibility = Visibility.Collapsed };
            timerPanelHeader.Children.Add(MakeTimeInputBox(out tbHrHeader, "00", () => ParseTimerInputs(true)));
            timerPanelHeader.Children.Add(new TextBlock { Text = "h :", Foreground = Theme.TextMuted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            timerPanelHeader.Children.Add(MakeTimeInputBox(out tbMinHeader, "05", () => ParseTimerInputs(true)));
            timerPanelHeader.Children.Add(new TextBlock { Text = "m :", Foreground = Theme.TextMuted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            timerPanelHeader.Children.Add(MakeTimeInputBox(out tbSecHeader, "00", () => ParseTimerInputs(true)));
            timerPanelHeader.Children.Add(new TextBlock { Text = "s", Foreground = Theme.TextMuted, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });

            var btnPresets = StyledBtn("▾ Presets", null, false, false, 8, 4, 5);
            btnPresets.FontSize = 11;
            btnPresets.Margin = new Thickness(2, 0, 0, 0);
            btnPresets.ToolTip = "Quick Timer Presets (Hr:Min:Sec)";
            btnPresets.Click += (s, e) =>
            {
                var cm = new ContextMenu();
                Action<string, int, int, int> addPre = (lbl, h, m, sec) =>
                {
                    var mi = new MenuItem { Header = lbl };
                    mi.Click += (s2, e2) => SetTimerValues(h, m, sec, true);
                    cm.Items.Add(mi);
                };
                addPre("⏱  15 Seconds (00:00:15)", 0, 0, 15);
                addPre("⏱  30 Seconds (00:00:30)", 0, 0, 30);
                addPre("⏱  1 Minute (00:01:00)", 0, 1, 0);
                addPre("⏱  3 Minutes (00:03:00)", 0, 3, 0);
                addPre("⏱  5 Minutes (00:05:00)", 0, 5, 0);
                addPre("⏱  15 Minutes (00:15:00)", 0, 15, 0);
                addPre("⏱  30 Minutes (00:30:00)", 0, 30, 0);
                addPre("⏱  1 Hour (01:00:00)", 1, 0, 0);
                cm.PlacementTarget = btnPresets;
                cm.Placement = PlacementMode.Bottom;
                cm.IsOpen = true;
            };
            timerPanelHeader.Children.Add(btnPresets);
            cycleSp.Children.Add(timerPanelHeader);
            cycleCapsule.Child = cycleSp;
            barRight.Children.Add(cycleCapsule);

            barDock.Children.Add(barRight);
            barDock.Children.Add(barLeft);
            headerStack.Children.Add(barDock);

            header.Child = headerStack;
            Grid.SetRow(header, 0);
            viewLibrary.Children.Add(header);

            // Minimal Monochrome Floating Selection Dock (Zero duplicate Select All button)
            selectionBar = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = Theme.B(18, 18, 22),
                BorderBrush = Theme.B(60, 60, 68),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(20, 10, 20, 0),
                Padding = new Thickness(12, 7, 12, 7)
            };
            var selDock = new DockPanel();

            var selLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var countPill = new Border { Background = Theme.Accent, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            txtSelCount.Foreground = Theme.AccentFg; txtSelCount.FontSize = 12; txtSelCount.FontWeight = FontWeights.Bold; txtSelCount.VerticalAlignment = VerticalAlignment.Center;
            countPill.Child = txtSelCount;
            selLeft.Children.Add(countPill);

            selLeft.Children.Add(StyledBtn("✕ Clear", ClearSelection, false, false, 10, 5, 6));

            var selActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(selActions, Dock.Right);
            selActions.Children.Add(StyledBtn("▶ Play Selected", () => { PlaySet("sel"); ClearSelection(); }, true, false, 11, 5, 6));
            selActions.Children.Add(StyledBtn("☰ Add to Queue", () => { AddToQueue(Sel(), false); ClearSelection(); }, false, false, 10, 5, 6));
            selActions.Children.Add(StyledBtn("● Include", () => { SetLoop(Sel(), true); ClearSelection(); }, false, false, 10, 5, 6));
            selActions.Children.Add(StyledBtn("○ Exclude", () => { SetLoop(Sel(), false); ClearSelection(); }, false, false, 10, 5, 6));
            selActions.Children.Add(StyledBtn("Add to Playlist...", () => AddItemsToPlaylistDialog(Sel()), false, false, 10, 5, 6));
            var btnRemSel = StyledBtn("Remove", DeleteSel, false, true, 10, 5, 6);
            btnRemSel.Margin = new Thickness(0);
            selActions.Children.Add(btnRemSel);

            selDock.Children.Add(selActions);
            selDock.Children.Add(selLeft);
            selectionBar.Child = selDock;
            Grid.SetRow(selectionBar, 1);
            viewLibrary.Children.Add(selectionBar);

            var contentGrid = new Grid { Margin = new Thickness(14, 10, 14, 6) };
            Grid.SetRow(contentGrid, 2);
            viewLibrary.Children.Add(contentGrid);

            BuildCardsListBox();
            contentGrid.Children.Add(lbCards);
        }

        void SetFilter(string mode)
        {
            filterMode = mode;
            filterBtnAll.Style = SegBtnStyle(mode == "all", 6, 11, 5);
            filterBtnLoop.Style = SegBtnStyle(mode == "loop", 6, 11, 5);
            filterBtnOut.Style = SegBtnStyle(mode == "out", 6, 11, 5);
            RebuildFilteredItems();
        }

        void ClearSelection()
        {
            if (items != null)
            {
                bool prev = loading;
                loading = true;
                foreach (var i in items) i.Checked = false;
                loading = prev;
            }
            UpdateSelectionBar();
        }

        void UpdateSelectionBar()
        {
            if (selectionBar == null || items == null) return;
            int c = items.Count(i => i.Checked);
            bool allChecked = filteredItems != null && filteredItems.Count > 0 && filteredItems.All(i => i.Checked);
            if (btnSelectAllHeader != null)
            {
                btnSelectAllHeader.Content = allChecked ? "✕ Deselect All" : "✓ Select All";
                btnSelectAllHeader.Style = allChecked
                    ? SegBtnStyle(true, 7, 12, 6)
                    : MakeBtnStyle(Theme.BgCard, Theme.BgCardHover, Theme.B(38, 38, 44), Theme.TextPrimary, Theme.BorderSubtle, 7, 12, 6);
            }

            if (c > 0)
            {
                txtSelCount.Text = c + " Selected";
                selectionBar.Visibility = Visibility.Visible;
            }
            else selectionBar.Visibility = Visibility.Collapsed;
        }

        void BuildCardsListBox()
        {
            lbCards.ItemsSource = filteredItems;
            lbCards.Background = Brushes.Transparent;
            lbCards.BorderThickness = new Thickness(0);
            lbCards.SelectionMode = SelectionMode.Single;
            ScrollViewer.SetHorizontalScrollBarVisibility(lbCards, ScrollBarVisibility.Disabled);

            var ipf = new FrameworkElementFactory(typeof(WrapPanel));
            ipf.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
            lbCards.ItemsPanel = new ItemsPanelTemplate { VisualTree = ipf };

            var dt = new DataTemplate(typeof(Item));
            var card = new FrameworkElementFactory(typeof(AnimCardBorder));
            card.SetValue(FrameworkElement.WidthProperty, 254.0);
            card.SetValue(FrameworkElement.MarginProperty, new Thickness(7));
            card.SetValue(Border.BackgroundProperty, Theme.BgCard);
            card.SetBinding(Border.BorderBrushProperty, new Binding("CardBorderBrush"));
            card.SetBinding(Border.BorderThicknessProperty, new Binding("CardBorderThickness"));
            card.SetBinding(UIElement.OpacityProperty, new Binding("CardOpacity"));
            card.SetBinding(AnimCardBorder.CardScaleProperty, new Binding("CardScale"));
            card.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            card.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

            var stack = new FrameworkElementFactory(typeof(StackPanel));

            var thumbGrid = new FrameworkElementFactory(typeof(Grid));
            thumbGrid.SetValue(FrameworkElement.HeightProperty, 142.0);
            thumbGrid.SetValue(FrameworkElement.ClipToBoundsProperty, true);

            var thumbBg = new FrameworkElementFactory(typeof(Border));
            thumbBg.SetValue(Border.BackgroundProperty, Theme.B(14, 14, 16));
            thumbBg.SetValue(Border.CornerRadiusProperty, new CornerRadius(9, 9, 0, 0));
            thumbGrid.AppendChild(thumbBg);

            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("Thumb"));
            img.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            thumbGrid.AppendChild(img);

            // Monochrome circular checkmark badge (White circle + Black tick)
            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetValue(Control.TemplateProperty, MakeCardCheckTemplate());
            chk.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Checked") { Mode = BindingMode.TwoWay });
            chk.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            chk.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            chk.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
            thumbGrid.AppendChild(chk);

            var durBorder = new FrameworkElementFactory(typeof(Border));
            durBorder.SetValue(Border.BackgroundProperty, Theme.BadgeDarkOverlay);
            durBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            durBorder.SetValue(Border.PaddingProperty, new Thickness(7, 2, 7, 2));
            durBorder.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            durBorder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            durBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
            var durTxt = new FrameworkElementFactory(typeof(TextBlock));
            durTxt.SetBinding(TextBlock.TextProperty, new Binding("Length"));
            durTxt.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            durTxt.SetValue(TextBlock.FontSizeProperty, 11.0);
            durTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            durBorder.AppendChild(durTxt);
            thumbGrid.AppendChild(durBorder);

            var playBadge = new FrameworkElementFactory(typeof(Border));
            playBadge.SetValue(Border.BackgroundProperty, Theme.Accent);
            playBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            playBadge.SetValue(Border.PaddingProperty, new Thickness(7, 2, 7, 2));
            playBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            playBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            playBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
            playBadge.SetBinding(UIElement.VisibilityProperty, new Binding("PlayingVisibility"));
            var playTxt = new FrameworkElementFactory(typeof(TextBlock));
            playTxt.SetValue(TextBlock.TextProperty, "▶ PLAYING");
            playTxt.SetValue(TextBlock.ForegroundProperty, Theme.AccentFg);
            playTxt.SetValue(TextBlock.FontSizeProperty, 10.0);
            playTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            playBadge.AppendChild(playTxt);
            thumbGrid.AppendChild(playBadge);

            var queuedBadge = new FrameworkElementFactory(typeof(Border));
            queuedBadge.SetValue(Border.BackgroundProperty, Theme.B(228, 228, 236));
            queuedBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            queuedBadge.SetValue(Border.PaddingProperty, new Thickness(7, 2, 7, 2));
            queuedBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            queuedBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            queuedBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
            queuedBadge.SetBinding(UIElement.VisibilityProperty, new Binding("QueuedVisibility"));
            var queuedTxt = new FrameworkElementFactory(typeof(TextBlock));
            queuedTxt.SetValue(TextBlock.TextProperty, "✓ SELECTED");
            queuedTxt.SetValue(TextBlock.ForegroundProperty, Theme.AccentFg);
            queuedTxt.SetValue(TextBlock.FontSizeProperty, 10.0);
            queuedTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            queuedBadge.AppendChild(queuedTxt);
            thumbGrid.AppendChild(queuedBadge);

            var dropBadge = new FrameworkElementFactory(typeof(Border));
            dropBadge.SetValue(Border.BackgroundProperty, Brushes.White);
            dropBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            dropBadge.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
            dropBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dropBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dropBadge.SetBinding(UIElement.VisibilityProperty, new Binding("DropTargetVisibility"));
            var dropTxt = new FrameworkElementFactory(typeof(TextBlock));
            dropTxt.SetValue(TextBlock.TextProperty, "↳ DROP HERE");
            dropTxt.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            dropTxt.SetValue(TextBlock.FontSizeProperty, 11.0);
            dropTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            dropBadge.AppendChild(dropTxt);
            thumbGrid.AppendChild(dropBadge);

            // Clickable Loop Status Pill directly on Card Thumbnail (click to toggle In Loop / Off)
            var loopBtn = new FrameworkElementFactory(typeof(Button));
            loopBtn.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            loopBtn.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            loopBtn.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
            loopBtn.SetValue(FrameworkElement.ToolTipProperty, "Click to toggle In/Out of Playlist Loop");
            loopBtn.SetValue(FrameworkElement.StyleProperty, MakeBtnStyle(Theme.BadgeLoopBg, Theme.BgCardHover, Theme.BgCard, Theme.TextPrimary, Theme.BorderSubtle, 5, 7, 2));
            var loopTxt = new FrameworkElementFactory(typeof(TextBlock));
            loopTxt.SetBinding(TextBlock.TextProperty, new Binding("LoopText"));
            loopTxt.SetBinding(TextBlock.ForegroundProperty, new Binding("LoopBadgeFg"));
            loopTxt.SetValue(TextBlock.FontSizeProperty, 10.5);
            loopTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            loopBtn.AppendChild(loopTxt);
            loopBtn.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var fe = s as FrameworkElement;
                var it = fe != null ? fe.DataContext as Item : null;
                if (it != null) { e.Handled = true; it.InLoop = !it.InLoop; RebuildFilteredItems(); }
            }));
            thumbGrid.AppendChild(loopBtn);

            stack.AppendChild(thumbGrid);

            var foot = new FrameworkElementFactory(typeof(StackPanel));
            foot.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 9, 12, 11));

            var titleTxt = new FrameworkElementFactory(typeof(TextBlock));
            titleTxt.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            titleTxt.SetValue(TextBlock.ForegroundProperty, Theme.TextPrimary);
            titleTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            titleTxt.SetValue(TextBlock.FontSizeProperty, 13.0);
            titleTxt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            foot.AppendChild(titleTxt);

            var footRow = new FrameworkElementFactory(typeof(DockPanel));
            footRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0));

            // Compact Reorder Pill on Card Footer: [ ◀  #1  ▶ ]
            var orderPill = new FrameworkElementFactory(typeof(Border));
            orderPill.SetValue(DockPanel.DockProperty, Dock.Right);
            orderPill.SetValue(Border.BackgroundProperty, Theme.BgInput);
            orderPill.SetValue(Border.BorderBrushProperty, Theme.BorderSubtle);
            orderPill.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            orderPill.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            orderPill.SetValue(Border.PaddingProperty, new Thickness(2, 1, 2, 1));
            orderPill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var orderRow = new FrameworkElementFactory(typeof(StackPanel));
            orderRow.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            orderRow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var miniBtnStyle = MakeBtnStyle(Brushes.Transparent, Theme.BgCardHover, Theme.B(44, 44, 50), Theme.TextSecondary, Brushes.Transparent, 4, 5, 1);

            var btnMovePrev = new FrameworkElementFactory(typeof(Button));
            btnMovePrev.SetValue(ContentControl.ContentProperty, "◀");
            btnMovePrev.SetValue(Control.FontSizeProperty, 9.5);
            btnMovePrev.SetValue(FrameworkElement.StyleProperty, miniBtnStyle);
            btnMovePrev.SetValue(FrameworkElement.ToolTipProperty, "Move Earlier in Order (or Drag Card)");
            btnMovePrev.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var fe = s as FrameworkElement;
                var it = fe != null ? fe.DataContext as Item : null;
                if (it != null) { e.Handled = true; MoveItem(it, -1); }
            }));
            orderRow.AppendChild(btnMovePrev);

            var ordTxt = new FrameworkElementFactory(typeof(TextBlock));
            ordTxt.SetBinding(TextBlock.TextProperty, new Binding("OrderBadge"));
            ordTxt.SetValue(TextBlock.ForegroundProperty, Theme.TextPrimary);
            ordTxt.SetValue(TextBlock.FontSizeProperty, 10.5);
            ordTxt.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            ordTxt.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 3, 0));
            ordTxt.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            orderRow.AppendChild(ordTxt);

            var btnMoveNext = new FrameworkElementFactory(typeof(Button));
            btnMoveNext.SetValue(ContentControl.ContentProperty, "▶");
            btnMoveNext.SetValue(Control.FontSizeProperty, 9.5);
            btnMoveNext.SetValue(FrameworkElement.StyleProperty, miniBtnStyle);
            btnMoveNext.SetValue(FrameworkElement.ToolTipProperty, "Move Later in Order (or Drag Card)");
            btnMoveNext.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var fe = s as FrameworkElement;
                var it = fe != null ? fe.DataContext as Item : null;
                if (it != null) { e.Handled = true; MoveItem(it, 1); }
            }));
            orderRow.AppendChild(btnMoveNext);

            orderPill.AppendChild(orderRow);
            footRow.AppendChild(orderPill);

            var metaTxt = new FrameworkElementFactory(typeof(TextBlock));
            metaTxt.SetBinding(TextBlock.TextProperty, new Binding("MetaLine"));
            metaTxt.SetValue(TextBlock.ForegroundProperty, Theme.TextMuted);
            metaTxt.SetValue(TextBlock.FontSizeProperty, 11.0);
            metaTxt.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            metaTxt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            footRow.AppendChild(metaTxt);

            foot.AppendChild(footRow);

            stack.AppendChild(foot);
            card.AppendChild(stack);
            dt.VisualTree = card;
            lbCards.ItemTemplate = dt;

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            var itemTpl = new ControlTemplate(typeof(ListBoxItem));
            itemTpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTpl));
            lbCards.ItemContainerStyle = itemStyle;

            // Drag-and-Drop Card Reordering in Library Grid with Smooth Scale + Floating Ghost
            lbCards.AllowDrop = true;
            lbCards.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var src = e.OriginalSource as DependencyObject;
                if (IsInsideInteractiveControl(src)) { draggedCardItem = null; return; }
                cardDragStart = e.GetPosition(null);
                draggedCardItem = FindDataContext<Item>(src);
            };
            lbCards.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && draggedCardItem != null)
                {
                    var pos = e.GetPosition(null);
                    if (Math.Abs(pos.X - cardDragStart.X) > 8 || Math.Abs(pos.Y - cardDragStart.Y) > 8)
                    {
                        var itemToDrag = draggedCardItem;
                        draggedCardItem = null;
                        activeDragSrcItem = itemToDrag;
                        itemToDrag.IsDragging = true;
                        ShowDragGhost(itemToDrag);
                        try { DragDrop.DoDragDrop(lbCards, itemToDrag, DragDropEffects.Move); }
                        finally { ClearDragVisuals(); }
                    }
                }
            };
            lbCards.GiveFeedback += (s, e) =>
            {
                UpdateDragGhostPos();
            };
            lbCards.QueryContinueDrag += (s, e) =>
            {
                UpdateDragGhostPos();
                if (e.EscapePressed) { e.Action = DragAction.Cancel; e.Handled = true; }
            };
            lbCards.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(Item)))
                {
                    e.Effects = DragDropEffects.Move;
                    UpdateDragGhostPos();
                    var hover = FindDataContext<Item>(e.OriginalSource as DependencyObject);
                    if (hover == activeDragSrcItem) hover = null;
                    if (activeDragHoverItem != hover)
                    {
                        if (activeDragHoverItem != null) activeDragHoverItem.IsDragOver = false;
                        activeDragHoverItem = hover;
                        if (activeDragHoverItem != null) activeDragHoverItem.IsDragOver = true;
                    }
                    e.Handled = true;
                }
            };
            lbCards.DragLeave += (s, e) =>
            {
                if (activeDragHoverItem != null) { activeDragHoverItem.IsDragOver = false; activeDragHoverItem = null; }
            };
            lbCards.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(typeof(Item)) && items != null)
                {
                    var srcItem = e.Data.GetData(typeof(Item)) as Item;
                    var dstItem = FindDataContext<Item>(e.OriginalSource as DependencyObject);
                    ClearDragVisuals();
                    if (srcItem != null && dstItem != null && srcItem != dstItem)
                    {
                        int targetIdx = items.IndexOf(dstItem);
                        if (targetIdx >= 0) MoveItemToIndex(srcItem, targetIdx);
                    }
                    e.Handled = true;
                }
            };

            lbCards.SelectionChanged += (s, e) =>
            {
                var it = lbCards.SelectedItem as Item;
                if (it != null) InspectItem(it);
            };
            lbCards.MouseDoubleClick += (s, e) =>
            {
                var it = lbCards.SelectedItem as Item;
                if (it != null) PlayOne(it);
            };

            var cm = new ContextMenu();
            var mPlayOne = new MenuItem { Header = "▶  Play Solo (Repeat Only This Video)" };
            mPlayOne.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) PlayOne(it); };
            var mPlayLoopHere = new MenuItem { Header = "↻  Start Playlist Loop From This Video" };
            mPlayLoopHere.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) PlayLoopFrom(it); };
            var mPlayNext = new MenuItem { Header = "⤴  Play Next" };
            mPlayNext.Click += (s, e) => AddToQueue(Sel(), true);
            var mAddQueue = new MenuItem { Header = "☰  Add to Queue" };
            mAddQueue.Click += (s, e) => AddToQueue(Sel(), false);
            var mMoveFirst = new MenuItem { Header = "⇤  Move to First (#1)" };
            mMoveFirst.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) MoveItemToIndex(it, 0); };
            var mMovePrev = new MenuItem { Header = "◀  Move Earlier in Order" };
            mMovePrev.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) MoveItem(it, -1); };
            var mMoveNext = new MenuItem { Header = "▶  Move Later in Order" };
            mMoveNext.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) MoveItem(it, 1); };
            var mMoveLast = new MenuItem { Header = "⇥  Move to Last" };
            mMoveLast.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null && items != null) MoveItemToIndex(it, items.Count - 1); };
            var mToggleLoop = new MenuItem { Header = "Toggle In/Out of Loop" };
            mToggleLoop.Click += (s, e) => { var it = lbCards.SelectedItem as Item; if (it != null) { it.InLoop = !it.InLoop; InspectItem(it); RebuildFilteredItems(); } };
            var mCopyPl = new MenuItem { Header = "Add to Playlist..." };
            mCopyPl.Click += (s, e) => AddItemsToPlaylistDialog(Sel());
            var mOpenExp = new MenuItem { Header = "Open Location / Source" };
            mOpenExp.Click += (s, e) =>
            {
                var it = lbCards.SelectedItem as Item;
                if (it == null) return;
                if (it.IsYouTube) Process.Start("https://www.youtube.com/watch?v=" + it.YtVideoId);
                else if (File.Exists(it.Path)) Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
            };
            var mDel = new MenuItem { Header = "Remove from Playlist" };
            mDel.Click += (s, e) => DeleteSel();
            cm.Items.Add(mPlayOne); cm.Items.Add(mPlayLoopHere); cm.Items.Add(new Separator());
            cm.Items.Add(mPlayNext); cm.Items.Add(mAddQueue); cm.Items.Add(new Separator());
            cm.Items.Add(mMoveFirst); cm.Items.Add(mMovePrev); cm.Items.Add(mMoveNext); cm.Items.Add(mMoveLast); cm.Items.Add(new Separator());
            cm.Items.Add(mToggleLoop); cm.Items.Add(mCopyPl); cm.Items.Add(mOpenExp); cm.Items.Add(new Separator()); cm.Items.Add(mDel);
            lbCards.ContextMenu = cm;
        }

        // ==================== MINIMAL RIGHT INSPECTOR & LIVE VIDEO PREVIEW ====================
        void BuildInspector(Grid shell)
        {
            inspBorder = new Border
            {
                Background = Theme.BgSidebar,
                BorderBrush = Theme.BorderSubtle,
                BorderThickness = new Thickness(1, 0, 0, 0)
            };
            Grid.SetColumn(inspBorder, 2);
            shell.Children.Add(inspBorder);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var pad = new StackPanel { Margin = new Thickness(16, 18, 16, 18) };
            sv.Content = pad;
            inspBorder.Child = sv;

            pad.Children.Add(new TextBlock
            {
                Text = "PREVIEW & INSPECTOR", FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 10)
            });

            // 16:9 Live Video Preview Box (LibVLC hardware preview + fallback thumbnail for YouTube/paused)
            var previewFrame = new Border
            {
                Height = 148, CornerRadius = new CornerRadius(9), ClipToBounds = true,
                Background = Theme.B(10, 10, 12), BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1)
            };
            var previewStackGrid = new Grid();

            previewVideoPanel = new Forms.Panel { Dock = Forms.DockStyle.Fill, BackColor = Draw.Color.FromArgb(12, 12, 14) };
            previewHost = new WinFormsHost { Child = previewVideoPanel, Visibility = Visibility.Collapsed };
            previewStackGrid.Children.Add(previewHost);

            previewThumbBox = new Border { Background = Theme.B(12, 12, 14) };
            previewThumbBox.Child = imgPreview;
            previewStackGrid.Children.Add(previewThumbBox);

            previewFrame.Child = previewStackGrid;
            pad.Children.Add(previewFrame);

            // Compact Preview Status Strip right below preview
            var prevDock = new DockPanel { Margin = new Thickness(0, 8, 0, 14) };
            btnPreviewToggle = StyledBtn("⏸ Pause Preview", () =>
            {
                previewUserPaused = !previewUserPaused;
                if (previewUserPaused) StopPreview();
                else if (inspectedItem != null) StartPreview(inspectedItem, true);
                UpdatePreviewToggleUi();
            }, false, false, 9, 3, 5);
            btnPreviewToggle.FontSize = 11;
            btnPreviewToggle.Margin = new Thickness(0);
            DockPanel.SetDock(btnPreviewToggle, Dock.Right);
            prevDock.Children.Add(btnPreviewToggle);

            txtPreviewStatus.Text = "Standby";
            txtPreviewStatus.FontSize = 11;
            txtPreviewStatus.Foreground = Theme.TextSecondary;
            txtPreviewStatus.VerticalAlignment = VerticalAlignment.Center;
            prevDock.Children.Add(txtPreviewStatus);
            pad.Children.Add(prevDock);

            // Clean Info Block
            txtInspTitle.Text = "Select a Wallpaper";
            txtInspTitle.FontSize = 15; txtInspTitle.FontWeight = FontWeights.Bold;
            txtInspTitle.Foreground = Theme.TextPrimary; txtInspTitle.TextWrapping = TextWrapping.Wrap;
            pad.Children.Add(txtInspTitle);

            txtInspMeta.Text = "Click any card to preview";
            txtInspMeta.FontSize = 11.5; txtInspMeta.Foreground = Theme.TextSecondary;
            txtInspMeta.Margin = new Thickness(0, 4, 0, 6);
            pad.Children.Add(txtInspMeta);

            txtInspLoop.Text = "○ Not selected";
            txtInspLoop.FontSize = 11.5; txtInspLoop.FontWeight = FontWeights.SemiBold;
            txtInspLoop.Foreground = Theme.TextMuted; txtInspLoop.Margin = new Thickness(0, 0, 0, 14);
            pad.Children.Add(txtInspLoop);

            // Distinct Non-Cluttered Actions
            var btnSetWall = StyledBtn("▶  Set as Wallpaper", () =>
            {
                if (inspectedItem == null) return;
                if (inspectedItem.InLoop) PlayLoopFrom(inspectedItem);
                else PlayOne(inspectedItem);
            }, true, false, 14, 8);
            btnSetWall.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnSetWall.Margin = new Thickness(0, 0, 0, 8);
            pad.Children.Add(btnSetWall);

            btnInspLoopToggle = StyledBtn("− Exclude from Loop", () =>
            {
                if (inspectedItem == null) return;
                inspectedItem.InLoop = !inspectedItem.InLoop;
                InspectItem(inspectedItem);
            }, false, false, 12, 7);
            btnInspLoopToggle.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnInspLoopToggle.Margin = new Thickness(0, 0, 0, 8);
            pad.Children.Add(btnInspLoopToggle);

            var btnInspAddPl = StyledBtn("Add to Playlist...", () =>
            {
                if (inspectedItem == null) return;
                AddItemsToPlaylistDialog(new List<Item> { inspectedItem });
            }, false, false, 12, 7);
            btnInspAddPl.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnInspAddPl.Margin = new Thickness(0, 0, 0, 8);
            pad.Children.Add(btnInspAddPl);

            var qGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            qGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            qGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            qGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var btnInspNext = StyledBtn("Play Next", () => { if (inspectedItem != null) AddToQueue(new List<Item> { inspectedItem }, true); }, false, false, 10, 6);
            btnInspNext.HorizontalAlignment = HorizontalAlignment.Stretch; btnInspNext.Margin = new Thickness(0);
            Grid.SetColumn(btnInspNext, 0); qGrid.Children.Add(btnInspNext);
            var btnInspQueue = StyledBtn("Add to Queue", () => { if (inspectedItem != null) AddToQueue(new List<Item> { inspectedItem }, false); }, false, false, 10, 6);
            btnInspQueue.HorizontalAlignment = HorizontalAlignment.Stretch; btnInspQueue.Margin = new Thickness(0);
            Grid.SetColumn(btnInspQueue, 2); qGrid.Children.Add(btnInspQueue);
            pad.Children.Add(qGrid);

            // Reorder Position Card
            var ordCard = new Border
            {
                Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 8, 7), Margin = new Thickness(0, 0, 0, 10)
            };
            var ordDock = new DockPanel();
            var ordBtns = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(ordBtns, Dock.Right);
            var bOrdUp = StyledBtn("◀", () => MoveItem(inspectedItem, -1), false, false, 9, 3, 5);
            var bOrdDn = StyledBtn("▶", () => MoveItem(inspectedItem, 1), false, false, 9, 3, 5);
            bOrdUp.Margin = new Thickness(0, 0, 4, 0); bOrdDn.Margin = new Thickness(0);
            ordBtns.Children.Add(bOrdUp); ordBtns.Children.Add(bOrdDn);
            ordDock.Children.Add(ordBtns);

            var ordTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ordTxt.Children.Add(new TextBlock { Text = "PLAYLIST ORDER", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted });
            txtInspOrder.Text = "#1";
            txtInspOrder.FontSize = 12.5; txtInspOrder.FontWeight = FontWeights.SemiBold; txtInspOrder.Foreground = Theme.TextPrimary;
            ordTxt.Children.Add(txtInspOrder);
            ordDock.Children.Add(ordTxt);
            ordCard.Child = ordDock;
            pad.Children.Add(ordCard);

            // Bottom Utility Pair
            var utilGrid = new Grid();
            utilGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            utilGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            utilGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnInspOpen = StyledBtn("Open File", () =>
            {
                if (inspectedItem == null) return;
                if (inspectedItem.IsYouTube) Process.Start("https://www.youtube.com/watch?v=" + inspectedItem.YtVideoId);
                else if (File.Exists(inspectedItem.Path)) Process.Start("explorer.exe", "/select,\"" + inspectedItem.Path + "\"");
            }, false, false, 10, 6);
            btnInspOpen.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnInspOpen.Margin = new Thickness(0);
            Grid.SetColumn(btnInspOpen, 0);
            utilGrid.Children.Add(btnInspOpen);

            var btnInspRem = StyledBtn("Remove", () =>
            {
                if (inspectedItem == null) return;
                RemoveItemsFromCurrentView(new List<Item> { inspectedItem });
            }, false, true, 10, 6);
            btnInspRem.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnInspRem.Margin = new Thickness(0);
            Grid.SetColumn(btnInspRem, 2);
            utilGrid.Children.Add(btnInspRem);

            pad.Children.Add(utilGrid);
        }

        void UpdatePreviewToggleUi()
        {
            if (btnPreviewToggle == null) return;
            if (inspectedItem != null && inspectedItem.IsStaticPhoto)
            {
                btnPreviewToggle.Visibility = Visibility.Collapsed;
                return;
            }
            btnPreviewToggle.Visibility = Visibility.Visible;
            if (inspectedItem != null && inspectedItem.IsYouTube && previewHost != null && previewHost.Visibility != Visibility.Visible)
                btnPreviewToggle.Content = "▶ Stream Preview";
            else
                btnPreviewToggle.Content = previewUserPaused ? "▶ Resume Preview" : "⏸ Pause Preview";
        }

        void StartPreview(Item it, bool forceYtResolve = false)
        {
            if (it == null || previewVideoPanel == null || previewHost == null) return;
            if (!IsVisible || WindowState == WindowState.Minimized || viewLibrary == null || viewLibrary.Visibility != Visibility.Visible)
                return;

            if (it.IsStaticPhoto)
            {
                StopPreview(false);
                txtPreviewStatus.Text = "Photo";
                UpdatePreviewToggleUi();
                return;
            }

            if (it.IsYouTube)
            {
                if (!forceYtResolve)
                {
                    StopPreview(false);
                    txtPreviewStatus.Text = "YouTube · " + it.YtQualityLabel;
                    UpdatePreviewToggleUi();
                    return;
                }
                string vid = it.YtVideoId;
                int maxH = Math.Min(it.YtMaxHeight, 1080); // Preview cap at 1080p for instant snappy preview
                txtPreviewStatus.Text = "Loading preview...";
                Task.Run(async () =>
                {
                    YouTubeHelper.StreamUrls urls = null;
                    try { urls = await YouTubeHelper.ResolveStreamUrlsAsync(vid, maxH, CancellationToken.None); }
                    catch { }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (inspectedItem != it || previewUserPaused || !IsVisible || WindowState == WindowState.Minimized || viewLibrary.Visibility != Visibility.Visible)
                            return;
                        if (urls == null || string.IsNullOrEmpty(urls.VideoUrl))
                        {
                            txtPreviewStatus.Text = "Preview unavailable";
                            return;
                        }
                        PlayPreviewMedia(urls.VideoUrl, true);
                    }));
                });
                UpdatePreviewToggleUi();
                return;
            }

            if (!File.Exists(it.Path)) { StopPreview(false); return; }
            if (previewPlayingPath == it.Path && previewHost.Visibility == Visibility.Visible && previewMp != IntPtr.Zero)
            {
                int st = Native.libvlc_media_player_get_state(previewMp);
                if (st == 3 || st == 4)
                {
                    if (st == 4) Native.libvlc_media_player_set_pause(previewMp, 0);
                    txtPreviewStatus.Text = "Preview";
                    UpdatePreviewToggleUi();
                    return;
                }
            }
            PlayPreviewMedia(it.Path, false);
        }

        async void PlayPreviewMedia(string pathOrUrl, bool isUrl)
        {
            if (previewVideoPanel == null || previewHost == null) return;
            IntPtr vlc = IntPtr.Zero;
            if (!WallHost.IsVlcReady)
            {
                txtPreviewStatus.Text = "Loading preview...";
                string reqPath = inspectedItem != null ? inspectedItem.Path : null;
                vlc = await WallHost.EnsureVlcAsync();
                if (vlc == IntPtr.Zero || previewUserPaused || !IsVisible || WindowState == WindowState.Minimized || viewLibrary.Visibility != Visibility.Visible) return;
                if (inspectedItem == null || inspectedItem.Path != reqPath) return;
            }
            else
            {
                vlc = WallHost.EnsureVlc();
            }
            if (vlc == IntPtr.Zero) return;

            previewHost.Visibility = Visibility.Visible;
            if (previewThumbBox != null) previewThumbBox.Visibility = Visibility.Collapsed;
            previewHost.UpdateLayout();
            if (!previewVideoPanel.IsHandleCreated) previewVideoPanel.CreateControl();
            IntPtr hwnd = previewVideoPanel.Handle;
            if (hwnd == IntPtr.Zero) return;

            if (previewMp == IntPtr.Zero)
                previewMp = Native.libvlc_media_player_new(vlc);
            Native.libvlc_media_player_set_hwnd(previewMp, hwnd);

            previewPlayingPath = pathOrUrl;
            byte[] utf8 = Encoding.UTF8.GetBytes(pathOrUrl + "\0");
            IntPtr m = isUrl ? Native.libvlc_media_new_location(vlc, utf8) : Native.libvlc_media_new_path(vlc, utf8);
            Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":no-audio\0"));
            Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":input-repeat=65535\0"));
            if (isUrl) Native.libvlc_media_add_option(m, Encoding.UTF8.GetBytes(":network-caching=1200\0"));
            Native.libvlc_media_player_set_media(previewMp, m);
            Native.libvlc_media_release(m);
            Native.libvlc_audio_set_mute(previewMp, 1);
            Native.libvlc_media_player_play(previewMp);

            txtPreviewStatus.Text = "Preview";
            UpdatePreviewToggleUi();
        }

        void StopPreview(bool markPausedText = true)
        {
            previewPlayingPath = null;
            if (previewMp != IntPtr.Zero)
            {
                try { Native.libvlc_media_player_stop(previewMp); } catch { }
            }
            if (previewHost != null) previewHost.Visibility = Visibility.Collapsed;
            if (previewThumbBox != null) previewThumbBox.Visibility = Visibility.Visible;
            if (markPausedText) txtPreviewStatus.Text = previewUserPaused ? "○ Preview Paused" : "○ Thumbnail";
            UpdatePreviewToggleUi();
        }

        // ==================== MINIMAL MONOCHROME SETTINGS VIEW (ZERO DUPLICATES) ====================
        void BuildSettingsView()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var pad = new StackPanel { Margin = new Thickness(34, 28, 34, 28), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
            sv.Content = pad;
            viewSettings.Children.Add(sv);

            // Header
            pad.Children.Add(new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary });
            pad.Children.Add(new TextBlock { Text = "Playback behavior, taskbar appearance, startup, and display preferences.", FontSize = 12.5, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 4, 0, 22) });

            // SECTION 1: Playback Rules Card
            var rulesCard = new Border { Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 0, 0, 18) };
            var rStack = new StackPanel();
            rStack.Children.Add(new TextBlock { Text = "PLAYBACK", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 12) });

            // Rule Row 1: Desktop / Start Menu Focused
            var rr1 = MakeInteractiveRuleRow("\uE7F4", Theme.TextPrimary, "Desktop (or Clean Start Menu)", "When viewing the desktop, or Start menu when no apps are open");
            var rr1Pill = new Border { Background = Theme.Accent, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
            rr1Pill.Child = new TextBlock { Text = "Play Video & Audio", Foreground = Theme.AccentFg, FontSize = 11.5, FontWeight = FontWeights.Bold };
            DockPanel.SetDock(rr1Pill, Dock.Right);
            ((DockPanel)rr1.Child).Children.Insert(0, rr1Pill);
            rStack.Children.Add(rr1);

            // Rule Row 2: Other App Focused (Background Visible)
            var rr2 = MakeInteractiveRuleRow("\uE737", Theme.TextSecondary, "When Another App Is Focused", "When a window is in focus while the wallpaper is still visible");
            var segOther = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            var segOtherSp = new StackPanel { Orientation = Orientation.Horizontal };
            ruleOtherBtn0 = StyledBtn("Mute Audio", () => SelectRuleOther(0), true, false, 10, 5, 5);
            ruleOtherBtn1 = StyledBtn("Keep Audio", () => SelectRuleOther(1), false, false, 10, 5, 5);
            ruleOtherBtn2 = StyledBtn("Pause", () => SelectRuleOther(2), false, false, 10, 5, 5);
            ruleOtherBtn0.Margin = new Thickness(1); ruleOtherBtn1.Margin = new Thickness(1); ruleOtherBtn2.Margin = new Thickness(1);
            segOtherSp.Children.Add(ruleOtherBtn0); segOtherSp.Children.Add(ruleOtherBtn1); segOtherSp.Children.Add(ruleOtherBtn2);
            segOther.Child = segOtherSp;
            DockPanel.SetDock(segOther, Dock.Right);
            ((DockPanel)rr2.Child).Children.Insert(0, segOther);
            rStack.Children.Add(rr2);

            // Rule Row 3: Desktop Covered (Fullscreen / 2-3 Apps)
            var rr3 = MakeInteractiveRuleRow("\uE740", Theme.TextSecondary, "When Desktop Is Covered", "When a fullscreen app or multiple windows cover the desktop");
            rr3.Margin = new Thickness(0);
            var segCov = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            var segCovSp = new StackPanel { Orientation = Orientation.Horizontal };
            ruleCovBtn0 = StyledBtn("Pause", () => SelectRuleCovered(0), true, false, 10, 5, 5);
            ruleCovBtn1 = StyledBtn("Keep Playing", () => SelectRuleCovered(1), false, false, 10, 5, 5);
            ruleCovBtn0.Margin = new Thickness(1); ruleCovBtn1.Margin = new Thickness(1);
            segCovSp.Children.Add(ruleCovBtn0); segCovSp.Children.Add(ruleCovBtn1);
            segCov.Child = segCovSp;
            DockPanel.SetDock(segCov, Dock.Right);
            ((DockPanel)rr3.Child).Children.Insert(0, segCov);
            rStack.Children.Add(rr3);

            rulesCard.Child = rStack;
            pad.Children.Add(rulesCard);

            // SECTION 2: General Card
            var sysCard = new Border { Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 18, 20, 18) };
            var lcSp = new StackPanel();
            lcSp.Children.Add(new TextBlock { Text = "GENERAL", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 12) });

            // Taskbar Transparency Switch Row
            var ttbRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var ttbDock = new DockPanel();
            swTtbSettings = MakePillSwitch();
            swTtbSettings.Checked += (s, e) => { if (!loading) { TaskbarHelper.SetTransparent(true); SyncTtbCheckboxes(true); SaveSettings(); } };
            swTtbSettings.Unchecked += (s, e) => { if (!loading) { TaskbarHelper.SetTransparent(false); SyncTtbCheckboxes(false); SaveSettings(); } };
            DockPanel.SetDock(swTtbSettings, Dock.Right);
            ttbDock.Children.Add(swTtbSettings);
            var ttbTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ttbTxt.Children.Add(new TextBlock { Text = "Transparent Taskbar", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            ttbTxt.Children.Add(new TextBlock { Text = "Make the Windows taskbar clear", Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            ttbDock.Children.Add(ttbTxt);
            ttbRow.Child = ttbDock;
            lcSp.Children.Add(ttbRow);

            // Start on Windows Startup Switch Row
            var bootRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var bootDock = new DockPanel();
            swStartupSettings = MakePillSwitch();
            swStartupSettings.Checked += (s, e) => { if (!loading) { StartupHelper.SetEnabled(true); SyncStartupCheckboxes(true); } };
            swStartupSettings.Unchecked += (s, e) => { if (!loading) { StartupHelper.SetEnabled(false); SyncStartupCheckboxes(false); } };
            DockPanel.SetDock(swStartupSettings, Dock.Right);
            bootDock.Children.Add(swStartupSettings);
            var bootTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bootTxt.Children.Add(new TextBlock { Text = "Start with Windows", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            bootTxt.Children.Add(new TextBlock { Text = "Launch minimized in the system tray at sign-in", Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            bootDock.Children.Add(bootTxt);
            bootRow.Child = bootDock;
            lcSp.Children.Add(bootRow);

            // Desktop Mini Controls HUD Switch Row
            var hudRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var hudDock = new DockPanel();
            swDeskHudSettings = MakePillSwitch();
            swDeskHudSettings.IsChecked = deskHudEnabled;
            swDeskHudSettings.Checked += (s, e) => { if (!loading) { deskHudEnabled = true; UpdateDesktopHud(); SaveSettings(); } };
            swDeskHudSettings.Unchecked += (s, e) => { if (!loading) { deskHudEnabled = false; UpdateDesktopHud(); SaveSettings(); } };
            DockPanel.SetDock(swDeskHudSettings, Dock.Right);
            hudDock.Children.Add(swDeskHudSettings);
            var hudTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hudTxt.Children.Add(new TextBlock { Text = "Desktop Controls", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            hudTxt.Children.Add(new TextBlock { Text = "Show playback controls on the desktop", Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            hudDock.Children.Add(hudTxt);
            hudRow.Child = hudDock;
            lcSp.Children.Add(hudRow);

            // YouTube Download Folder Row
            var dlRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var dlDock = new DockPanel();
            var dlBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dlBtns, Dock.Right);
            var bDlBrowse = StyledBtn("Browse...", () => ChooseDownloadFolder(), false, false, 11, 5, 6);
            var bDlOpen = StyledBtn("Open", () => { try { Directory.CreateDirectory(YouTubeHelper.DownloadsDir); Process.Start("explorer.exe", "\"" + YouTubeHelper.DownloadsDir + "\""); } catch { } }, false, false, 10, 5, 6);
            var bDlReset = StyledBtn("Default", () =>
            {
                YouTubeHelper.CustomDownloadsDir = null;
                UpdateDlDirText();
                SaveSettings();
            }, false, false, 10, 5, 6);
            bDlReset.Margin = new Thickness(0);
            dlBtns.Children.Add(bDlBrowse); dlBtns.Children.Add(bDlOpen); dlBtns.Children.Add(bDlReset);
            dlDock.Children.Add(dlBtns);
            var dlTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            dlTxt.Children.Add(new TextBlock { Text = "Downloads Folder", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            txtDlDirSettings.Text = YouTubeHelper.DownloadsDir;
            txtDlDirSettings.Foreground = Theme.TextSecondary; txtDlDirSettings.FontSize = 11.5; txtDlDirSettings.Margin = new Thickness(0, 2, 0, 0);
            txtDlDirSettings.TextTrimming = TextTrimming.CharacterEllipsis;
            dlTxt.Children.Add(txtDlDirSettings);
            dlDock.Children.Add(dlTxt);
            dlRow.Child = dlDock;
            lcSp.Children.Add(dlRow);

            // Scaling Segmented Row
            var scaleRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var scaleDock = new DockPanel();
            var segScale = new Border { Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            var segScaleSp = new StackPanel { Orientation = Orientation.Horizontal };
            setFitBtn0 = StyledBtn("Fill", () => SelectFitMode(0), true, false, 12, 5, 5);
            setFitBtn1 = StyledBtn("Fit", () => SelectFitMode(1), false, false, 12, 5, 5);
            setFitBtn2 = StyledBtn("Stretch", () => SelectFitMode(2), false, false, 12, 5, 5);
            setFitBtn0.Margin = new Thickness(1); setFitBtn1.Margin = new Thickness(1); setFitBtn2.Margin = new Thickness(1);
            segScaleSp.Children.Add(setFitBtn0); segScaleSp.Children.Add(setFitBtn1); segScaleSp.Children.Add(setFitBtn2);
            segScale.Child = segScaleSp;
            DockPanel.SetDock(segScale, Dock.Right);
            scaleDock.Children.Add(segScale);
            var scaleTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            scaleTxt.Children.Add(new TextBlock { Text = "Wallpaper Fit", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            scaleTxt.Children.Add(new TextBlock { Text = "Choose how wallpapers fit your screen", Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            scaleDock.Children.Add(scaleTxt);
            scaleRow.Child = scaleDock;
            lcSp.Children.Add(scaleRow);

            // Reset App Data Row
            var resetRow = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12) };
            var resetDock = new DockPanel();
            var btnResetApp = StyledBtn("Reset App", ResetWholeApp, false, false, 14, 6, 6);
            btnResetApp.Margin = new Thickness(0);
            btnResetApp.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(btnResetApp, Dock.Right);
            resetDock.Children.Add(btnResetApp);
            var resetTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            resetTxt.Children.Add(new TextBlock { Text = "Reset LiveDesk", Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            resetTxt.Children.Add(new TextBlock { Text = "Clear all playlists and restore default settings (your files are kept)", Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            resetDock.Children.Add(resetTxt);
            resetRow.Child = resetDock;
            lcSp.Children.Add(resetRow);

            sysCard.Child = lcSp;
            pad.Children.Add(sysCard);
        }

        void ResetWholeApp()
        {
            var res = MessageBox.Show(
                this,
                "Reset LiveDesk back to default settings and clear all playlists?\n\nYour video and photo files on disk will not be deleted.",
                "Reset LiveDesk",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            StopPreview(false);
            StopWall();
            ClearSelection();

            try
            {
                if (File.Exists(libFile)) File.Delete(libFile);
                if (File.Exists(colFile)) File.Delete(colFile);
                if (File.Exists(plFile)) File.Delete(plFile);
                if (File.Exists(setFile)) File.Delete(setFile);
            }
            catch { }

            collectionPl.Items.Clear();
            playlists.Clear();
            activePl = null;
            inspectedItem = null;
            tbSearch.Text = "";
            SetFilter("all");

            loading = true;
            shuffleEnabled = false;
            muteEnabled = false;
            if (vol != null) vol.Value = 70;
            if (txtVolPct != null) txtVolPct.Text = "70%";
            deskHudEnabled = true;
            if (swDeskHudSettings != null) swDeskHudSettings.IsChecked = true;
            YouTubeHelper.CustomDownloadsDir = null;
            loading = false;

            SelectFitMode(0);
            SelectRuleOther(0);
            SelectRuleCovered(0);
            SetTimerValues(0, 5, 0, false);
            SetCycleMode(false);
            TaskbarHelper.SetTransparent(false);
            SyncTtbCheckboxes(false);
            StartupHelper.SetEnabled(false);
            SyncStartupCheckboxes(false);
            UpdateDlDirText();
            UpdateShuffleVisual();
            UpdateMuteVisual();
            UpdateDesktopHud();

            txtInspTitle.Text = "Select a Wallpaper";
            txtInspMeta.Text = "Click any card to preview";
            txtInspLoop.Text = "Not selected";
            imgPreview.Source = null;

            SelectCollection(false);
            SaveLib();
            SaveSettings();
            SwitchTab(0);
        }

        Border MakeInteractiveRuleRow(string iconGlyph, Brush iconColor, string title, string subtitle)
        {
            var bd = new Border { Background = Theme.BgCardInner, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
            var dp = new DockPanel();
            var iconBadge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            iconBadge.Child = new TextBlock { Text = iconGlyph, FontFamily = Theme.IconFont, FontSize = 14, Foreground = iconColor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dp.Children.Add(iconBadge);
            var txtSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txtSp.Children.Add(new TextBlock { Text = title, Foreground = Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            txtSp.Children.Add(new TextBlock { Text = subtitle, Foreground = Theme.TextSecondary, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            dp.Children.Add(txtSp);
            bd.Child = dp;
            return bd;
        }

        void SelectRuleOther(int r)
        {
            ruleOtherApp = Math.Min(2, Math.Max(0, r));
            if (ruleOtherBtn0 != null) ruleOtherBtn0.Style = SegBtnStyle(ruleOtherApp == 0, 5, 10, 5);
            if (ruleOtherBtn1 != null) ruleOtherBtn1.Style = SegBtnStyle(ruleOtherApp == 1, 5, 10, 5);
            if (ruleOtherBtn2 != null) ruleOtherBtn2.Style = SegBtnStyle(ruleOtherApp == 2, 5, 10, 5);
            if (host != null) { host.RuleOtherApp = ruleOtherApp; host.WatchNow(); host.ApplyPlayState(); }
            SaveSettings();
        }

        void SelectRuleCovered(int r)
        {
            ruleCovered = Math.Min(1, Math.Max(0, r));
            if (ruleCovBtn0 != null) ruleCovBtn0.Style = SegBtnStyle(ruleCovered == 0, 5, 10, 5);
            if (ruleCovBtn1 != null) ruleCovBtn1.Style = SegBtnStyle(ruleCovered == 1, 5, 10, 5);
            if (host != null) { host.RuleCovered = ruleCovered; host.WatchNow(); host.ApplyPlayState(); }
            SaveSettings();
        }

        void SelectFitMode(int f)
        {
            fitMode = Math.Min(2, Math.Max(0, f));
            if (setFitBtn0 != null) setFitBtn0.Style = SegBtnStyle(fitMode == 0, 5, 12, 5);
            if (setFitBtn1 != null) setFitBtn1.Style = SegBtnStyle(fitMode == 1, 5, 12, 5);
            if (setFitBtn2 != null) setFitBtn2.Style = SegBtnStyle(fitMode == 2, 5, 12, 5);
            ApplyFit();
            SaveSettings();
        }

        // ==================== BOTTOM TRANSPORT BAR ====================
        void BuildBottomTransport(Grid root)
        {
            var bar = new Border { Background = Theme.BgTransport, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 8, 18, 8) };
            Grid.SetRow(bar, 1);
            root.Children.Add(bar);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            bar.Child = g;

            // Left: Clickable Now Playing Block (Spotify-style Queue Popup)
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var miniThumb = new Border { Width = 72, Height = 42, CornerRadius = new CornerRadius(6), Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0) };
            miniThumb.Child = imgNowThumb;
            left.Children.Add(miniThumb);

            var npText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 178 };
            txtNowTitle.Text = "No Wallpaper Active";
            txtNowTitle.FontSize = 13.5; txtNowTitle.FontWeight = FontWeights.Bold; txtNowTitle.Foreground = Theme.TextPrimary;
            txtNowTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            txtNowSub.Text = "Click 'Play Loop' or double-click a card";
            txtNowSub.FontSize = 11; txtNowSub.Foreground = Theme.TextSecondary;
            txtNowSub.TextTrimming = TextTrimming.CharacterEllipsis;
            npText.Children.Add(txtNowTitle);
            npText.Children.Add(txtNowSub);
            left.Children.Add(npText);

            var npCue = new TextBlock
            {
                Text = "\uE90B", FontFamily = Theme.IconFont, FontSize = 13,
                Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            left.Children.Add(npCue);

            var btnNowQueue = new Button
            {
                Content = left,
                Style = MakeBtnStyle(Brushes.Transparent, Theme.BgCardHover, Theme.BgCard, Theme.TextPrimary, Brushes.Transparent, 8, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Click to view active playing queue",
                Cursor = Cursors.Hand
            };
            btnNowQueue.Click += (s, e) => ToggleQueuePopup(btnNowQueue);
            Grid.SetColumn(btnNowQueue, 0);
            g.Children.Add(btnNowQueue);

            // Center: Crisp Monochrome Transport Buttons + Modern Seek Slider
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(20, 0, 20, 0) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };

            btnShuffleToggle = FluentIconBtn("\uE8B1", "Shuffle Queue", () =>
            {
                shuffleEnabled = !shuffleEnabled;
                UpdateShuffleVisual();
                SaveSettings();
            }, false, 32, 7);
            btns.Children.Add(btnShuffleToggle);

            btns.Children.Add(FluentIconBtn("\uE892", "Previous Wallpaper", () => Step(-1), false, 32, 7));
            btnPlayPause = FluentIconBtn("\uE768", "Play / Pause", PauseToggle, true, 36, 18);
            btns.Children.Add(btnPlayPause);
            btns.Children.Add(FluentIconBtn("\uE893", "Next Wallpaper", () => Step(1), false, 32, 7));
            btns.Children.Add(FluentIconBtn("\uE71A", "Stop Wallpaper", StopWall, false, 32, 7));
            center.Children.Add(btns);

            var seekRow = new Grid();
            seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            txtTimeCur.Text = "0:00"; txtTimeCur.FontSize = 11; txtTimeCur.Foreground = Theme.TextMuted; txtTimeCur.VerticalAlignment = VerticalAlignment.Center;
            txtTimeTot.Text = "0:00"; txtTimeTot.FontSize = 11; txtTimeTot.Foreground = Theme.TextMuted; txtTimeTot.HorizontalAlignment = HorizontalAlignment.Right; txtTimeTot.VerticalAlignment = VerticalAlignment.Center;
            seekSlider.Cursor = Cursors.Hand;
            StyleModernSlider(seekSlider);
            seekSlider.PreviewMouseLeftButtonDown += (s, e) => isDraggingSeek = true;
            seekSlider.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (host != null) host.Seek(seekSlider.Value);
                isDraggingSeek = false;
            };
            Grid.SetColumn(txtTimeCur, 0);
            Grid.SetColumn(seekSlider, 1);
            Grid.SetColumn(txtTimeTot, 2);
            seekRow.Children.Add(txtTimeCur);
            seekRow.Children.Add(seekSlider);
            seekRow.Children.Add(txtTimeTot);
            center.Children.Add(seekRow);
            Grid.SetColumn(center, 1);
            g.Children.Add(center);

            // Right: Audio Volume
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

            var volRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            btnMuteIcon = FluentIconBtn("\uE767", "Mute / Unmute Audio", () =>
            {
                muteEnabled = !muteEnabled;
                UpdateMuteVisual();
                ApplyAudio(); SaveSettings();
            }, false, 30, 6);
            volRow.Children.Add(btnMuteIcon);

            vol = new Slider { Minimum = 0, Maximum = 100, Width = 110, Value = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), Cursor = Cursors.Hand };
            StyleModernSlider(vol);
            vol.ValueChanged += (s, e) =>
            {
                if (txtVolPct != null) txtVolPct.Text = ((int)vol.Value) + "%";
                ApplyAudio(); SaveSettings();
            };
            volRow.Children.Add(vol);
            txtVolPct.Text = "70%"; txtVolPct.Width = 34; txtVolPct.FontSize = 11.5; txtVolPct.Foreground = Theme.TextSecondary; txtVolPct.VerticalAlignment = VerticalAlignment.Center;
            volRow.Children.Add(txtVolPct);
            right.Children.Add(volRow);

            Grid.SetColumn(right, 2);
            g.Children.Add(right);
        }

        void SetIconBtnGlyph(Button b, string glyph)
        {
            if (b == null) return;
            var tb = b.Content as TextBlock;
            if (tb != null) tb.Text = glyph;
        }

        void UpdateShuffleVisual()
        {
            if (btnShuffleToggle != null)
                btnShuffleToggle.Style = shuffleEnabled
                    ? MakeBtnStyle(Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Theme.AccentFg, Theme.Accent, 7, 0, 0)
                    : MakeBtnStyle(Theme.BgCard, Theme.BgCardHover, Theme.B(38, 38, 44), Theme.TextPrimary, Theme.BorderSubtle, 7, 0, 0);
        }

        void UpdateMuteVisual()
        {
            SetIconBtnGlyph(btnMuteIcon, muteEnabled ? "\uE74F" : "\uE767");
        }

        void BuildTray()
        {
            Draw.Icon trayIco = Draw.SystemIcons.Application;
            try { trayIco = Draw.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Draw.SystemIcons.Application; } catch { }
            ni = new Forms.NotifyIcon { Icon = trayIco, Text = "LiveDesk", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open LiveDesk", null, (s, e) => ShowMe());
            trayPlMenu = new Forms.ToolStripMenuItem("Playlists (1-Click Play)");
            menu.Items.Add(trayPlMenu);
            trayTtbItem = new Forms.ToolStripMenuItem("Transparent Taskbar (Inbuilt)", null, (s, e) =>
            {
                bool next = !ttbEnabled;
                TaskbarHelper.SetTransparent(next);
                SyncTtbCheckboxes(next);
                SaveSettings();
            });
            menu.Items.Add(trayTtbItem);
            trayStartupItem = new Forms.ToolStripMenuItem("Run on Windows Startup", null, (s, e) =>
            {
                bool next = !startupEnabled;
                StartupHelper.SetEnabled(next);
                SyncStartupCheckboxes(next);
            });
            menu.Items.Add(trayStartupItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Next Wallpaper", null, (s, e) => Step(1));
            menu.Items.Add("Previous Wallpaper", null, (s, e) => Step(-1));
            menu.Items.Add("Pause / Resume", null, (s, e) => PauseToggle());
            menu.Items.Add("Stop Wallpaper", null, (s, e) => StopWall());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            ni.ContextMenuStrip = menu;
            ni.DoubleClick += (s, e) => ShowMe();
        }

        void ShowMe()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            bool prevTop = Topmost;
            Topmost = true;
            Topmost = prevTop;
            Focus();
        }
        public void OpenFromWakeup() { ShowMe(); }

        void BuildDesktopHud()
        {
            if (deskHudWin != null) return;
            deskHudWin = new Window
            {
                Width = 600, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize
            };
            var pill = new Border
            {
                Background = Theme.BA(232, 12, 12, 14),
                BorderBrush = Theme.B(54, 54, 62), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8)
            };
            var dock = new DockPanel();

            // Left: Open LiveDesk icon + Prev / PlayPause / Next
            var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(leftBtns, Dock.Left);
            var bOpen = FluentIconBtn("\uE8A7", "Open LiveDesk (or Double-Click bar)", () => ShowMe(), false, 28, 7);
            bOpen.Margin = new Thickness(0, 0, 6, 0);
            var bPrev = FluentIconBtn("\uE892", "Previous Wallpaper", () => Step(-1), false, 28, 7);
            hudBtnPlayPause = FluentIconBtn("\uE769", "Play / Pause Wallpaper", () => PauseToggle(), true, 30, 15);
            var bNext = FluentIconBtn("\uE893", "Next Wallpaper", () => Step(1), false, 28, 7);
            bNext.Margin = new Thickness(2, 0, 10, 0);
            leftBtns.Children.Add(bOpen); leftBtns.Children.Add(bPrev); leftBtns.Children.Add(hudBtnPlayPause); leftBtns.Children.Add(bNext);
            dock.Children.Add(leftBtns);

            // Right: Close / Hide Desktop Bar button
            var bHideHud = FluentIconBtn("\uE711", "Hide Desktop Controls (re-enable in Settings)", () =>
            {
                deskHudEnabled = false;
                loading = true;
                if (swDeskHudSettings != null) swDeskHudSettings.IsChecked = false;
                loading = false;
                UpdateDesktopHud();
                SaveSettings();
            }, false, 24, 6);
            bHideHud.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(bHideHud, Dock.Right);
            dock.Children.Add(bHideHud);

            // Center: Title + Time + Seekbar
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var topRow = new DockPanel { Margin = new Thickness(2, 0, 2, 3) };
            hudTxtTime = new TextBlock { Text = "0:00 / 0:00", Foreground = Theme.TextSecondary, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(hudTxtTime, Dock.Right);
            topRow.Children.Add(hudTxtTime);
            hudTxtTitle = new TextBlock { Text = "LiveDesk", Foreground = Theme.TextPrimary, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            topRow.Children.Add(hudTxtTitle);
            mid.Children.Add(topRow);

            hudSeekSlider = new Slider { Minimum = 0, Maximum = 100, Value = 0, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            StyleModernSlider(hudSeekSlider);
            hudSeekSlider.PreviewMouseLeftButtonDown += (s, e) => isDraggingHudSeek = true;
            hudSeekSlider.PreviewMouseLeftButtonUp += (s, e) =>
            {
                isDraggingHudSeek = false;
                if (host != null && (cur == null || !cur.IsStaticPhoto)) host.Seek(hudSeekSlider.Value);
            };
            mid.Children.Add(hudSeekSlider);
            dock.Children.Add(mid);

            pill.Child = dock;
            pill.MouseLeftButtonDown += (s, e) =>
            {
                if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) return;
                if (e.ClickCount == 2) { ShowMe(); return; }
                try { deskHudWin.DragMove(); PinDesktopHudBottom(); } catch { }
            };
            deskHudWin.Content = pill;

            var wa = Forms.Screen.PrimaryScreen.WorkingArea;
            var ps = PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;
            if (ps != null && ps.CompositionTarget != null)
            {
                dpiX = ps.CompositionTarget.TransformToDevice.M11;
                dpiY = ps.CompositionTarget.TransformToDevice.M22;
            }
            deskHudWin.Left = (wa.Left / dpiX) + ((wa.Width / dpiX) - deskHudWin.Width) / 2.0;
            deskHudWin.Top = (wa.Bottom / dpiY) - 70;

            deskHudWin.SourceInitialized += (s, e) =>
            {
                deskHudHwnd = new WindowInteropHelper(deskHudWin).Handle;
                if (host != null) host.DeskHudHandle = deskHudHwnd;
                int ex = Native.GetWindowLong(deskHudHwnd, -20);
                Native.SetWindowLong(deskHudHwnd, -20, ex | 0x00000080 | 0x08000000);
                var src = HwndSource.FromHwnd(deskHudHwnd);
                if (src != null) src.AddHook(DesktopHudWndProc);
                PinDesktopHudBottom();
            };
        }

        IntPtr DesktopHudWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0046 && lParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING -> stay at HWND_BOTTOM (on desktop, below apps)
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = (IntPtr)1;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return IntPtr.Zero;
        }

        void PinDesktopHudBottom()
        {
            if (deskHudHwnd != IntPtr.Zero)
                Native.SetWindowPos(deskHudHwnd, (IntPtr)1, 0, 0, 0, 0, 0x0013);
        }

        void UpdateDesktopHud()
        {
            if (!deskHudEnabled || host == null || cur == null)
            {
                if (deskHudWin != null && deskHudWin.IsVisible) deskHudWin.Hide();
                return;
            }
            if (deskHudWin == null) BuildDesktopHud();
            if (!deskHudWin.IsVisible)
            {
                deskHudWin.Show();
                PinDesktopHudBottom();
            }
            else
            {
                PinDesktopHudBottom();
            }

            if (hudBtnPlayPause != null)
                SetIconBtnGlyph(hudBtnPlayPause, paused ? "\uE768" : "\uE769");
            if (hudTxtTitle != null)
                hudTxtTitle.Text = cur.Name;
            if (cur.IsStaticPhoto)
            {
                if (hudTxtTime != null) hudTxtTime.Text = cycleByTimer ? "PHOTO · TIMER" : "PHOTO · ∞";
                if (hudSeekSlider != null) { hudSeekSlider.IsEnabled = false; hudSeekSlider.Maximum = 1; hudSeekSlider.Value = 1; }
            }
            else
            {
                double pos = host.Pos, dur = host.Dur;
                if (dur <= 0 && cur.Dur.TotalSeconds > 0) dur = cur.Dur.TotalSeconds;
                if (hudTxtTime != null) hudTxtTime.Text = TimeSpan.FromSeconds(pos).ToString(@"m\:ss") + " / " + TimeSpan.FromSeconds(dur).ToString(@"m\:ss");
                if (hudSeekSlider != null)
                {
                    hudSeekSlider.IsEnabled = true;
                    if (!isDraggingHudSeek && dur > 0) { hudSeekSlider.Maximum = dur; hudSeekSlider.Value = Math.Min(dur, pos); }
                }
            }
        }

        void ExitApp()
        {
            StopPreview();
            if (previewMp != IntPtr.Zero) { try { Native.libvlc_media_player_release(previewMp); previewMp = IntPtr.Zero; } catch { } }
            if (deskHudWin != null) { try { deskHudWin.Close(); } catch { } deskHudWin = null; }
            StopWall();
            TaskbarHelper.Shutdown();
            ni.Visible = false; ni.Dispose();
            Application.Current.Shutdown();
        }

        void SyncTtbCheckboxes(bool v)
        {
            ttbEnabled = v;
            loading = true;
            if (swTtbSettings != null) swTtbSettings.IsChecked = v;
            if (trayTtbItem != null) trayTtbItem.Checked = v;
            loading = false;
        }

        void SyncStartupCheckboxes(bool v)
        {
            startupEnabled = v;
            loading = true;
            if (swStartupSettings != null) swStartupSettings.IsChecked = v;
            if (trayStartupItem != null) trayStartupItem.Checked = v;
            loading = false;
        }

        static T FindDataContext<T>(DependencyObject d) where T : class
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.DataContext is T) return (T)fe.DataContext;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        static bool IsInsideInteractiveControl(DependencyObject d)
        {
            while (d != null)
            {
                if (d is ButtonBase) return true;
                if (d is ListBoxItem) return false;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        void RefreshOrderIndices()
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
                items[i].OrderIndex = i + 1;
            if (inspectedItem != null && txtInspOrder != null)
            {
                int idx = items.IndexOf(inspectedItem);
                if (idx >= 0) txtInspOrder.Text = "#" + (idx + 1) + " of " + items.Count;
            }
        }

        void SyncQueueAfterReorder()
        {
            if (items == null || queue == null || queue.Count == 0 || shuffleEnabled) return;
            if (mode == "loop")
            {
                queue = items.Where(i => i.InLoop).ToList();
                if (cur != null) qi = Math.Max(0, queue.IndexOf(cur));
            }
            else if (mode == "multiloop")
            {
                queue = GetMultiLoopItems();
                if (cur != null) qi = Math.Max(0, queue.IndexOf(cur));
            }
            else if (mode == "all")
            {
                queue = items.ToList();
                if (cur != null) qi = Math.Max(0, queue.IndexOf(cur));
            }
        }

        void MoveItem(Item it, int delta)
        {
            if (items == null || it == null) return;
            int idx = items.IndexOf(it);
            if (idx < 0) return;
            MoveItemToIndex(it, idx + delta);
        }

        void MoveItemToIndex(Item it, int newIdx)
        {
            if (items == null || it == null || items.Count <= 1) return;
            int oldIdx = items.IndexOf(it);
            if (oldIdx < 0) return;
            newIdx = Math.Max(0, Math.Min(items.Count - 1, newIdx));
            if (oldIdx == newIdx) return;

            items.Move(oldIdx, newIdx);
            RefreshOrderIndices();
            SyncQueueAfterReorder();
            SaveLib();
            RebuildFilteredItems();
            lbCards.SelectedItem = it;
            InspectItem(it);
        }

        void MovePlaylist(Playlist pl, int delta)
        {
            if (pl == null || playlists.Count <= 1) return;
            int oldIdx = playlists.IndexOf(pl);
            if (oldIdx < 0) return;
            int newIdx = Math.Max(0, Math.Min(playlists.Count - 1, oldIdx + delta));
            if (oldIdx == newIdx) return;
            playlists.RemoveAt(oldIdx);
            playlists.Insert(newIdx, pl);
            SaveLib();
            RefreshPlaylistUi();
        }

        void ShowDragGhost(Item it)
        {
            ClearDragVisuals();
            activeDragSrcItem = it;
            if (it == null) return;
            it.IsDragging = true;
            try
            {
                var cardBorder = new Border
                {
                    Width = 176, Background = Theme.B(22, 22, 26),
                    BorderBrush = Brushes.White, BorderThickness = new Thickness(1.8),
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(6), Opacity = 0.90
                };
                var sp = new StackPanel();
                var th = new Border { Height = 92, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = Theme.B(12, 12, 14) };
                th.Child = new Image { Source = it.Thumb ?? ThumbCache.Get(it.Path), Stretch = Stretch.UniformToFill };
                sp.Children.Add(th);
                var row = new DockPanel { Margin = new Thickness(2, 6, 2, 2) };
                var ord = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0) };
                ord.Child = new TextBlock { Text = it.OrderBadge, Foreground = Brushes.Black, FontSize = 10, FontWeight = FontWeights.Bold };
                DockPanel.SetDock(ord, Dock.Right);
                row.Children.Add(ord);
                row.Children.Add(new TextBlock { Text = it.Name, Foreground = Theme.TextPrimary, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(row);
                cardBorder.Child = sp;

                dragGhostWin = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                    ShowInTaskbar = false, ShowActivated = false, Topmost = true, IsHitTestVisible = false,
                    SizeToContent = SizeToContent.WidthAndHeight, Content = cardBorder
                };
                dragGhostWin.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(dragGhostWin).Handle;
                    int ex = Native.GetWindowLong(h, -20);
                    Native.SetWindowLong(h, -20, ex | 0x00000020 | 0x00000080);
                };
                UpdateDragGhostPos();
                dragGhostWin.Show();
            }
            catch { }
        }

        void UpdateDragGhostPos()
        {
            if (dragGhostWin == null) return;
            try
            {
                var pt = Forms.Control.MousePosition;
                double sx = pt.X + 18, sy = pt.Y + 18;
                var ps = PresentationSource.FromVisual(this);
                if (ps != null && ps.CompositionTarget != null)
                {
                    var p = ps.CompositionTarget.TransformFromDevice.Transform(new Point(pt.X, pt.Y));
                    sx = p.X + 18;
                    sy = p.Y + 18;
                }
                dragGhostWin.Left = sx;
                dragGhostWin.Top = sy;
            }
            catch { }
        }

        void ClearDragVisuals()
        {
            if (dragGhostWin != null) { try { dragGhostWin.Close(); } catch { } dragGhostWin = null; }
            if (activeDragSrcItem != null) { activeDragSrcItem.IsDragging = false; activeDragSrcItem = null; }
            if (activeDragHoverItem != null) { activeDragHoverItem.IsDragOver = false; activeDragHoverItem = null; }
            if (activeDragSrcPl != null) { activeDragSrcPl.IsDragging = false; activeDragSrcPl = null; }
            if (activeDragHoverPl != null) { activeDragHoverPl.IsDragOver = false; activeDragHoverPl = null; }
        }

        void InspectItem(Item it)
        {
            inspectedItem = it;
            if (it == null) { RefreshCycleModeUi(); return; }
            if (it.IsYouTube)
            {
                imgPreview.Source = it.Thumb;
                ThumbCache.GetYouTubeAsync(it.YtVideoId, bs =>
                {
                    it.Thumb = bs;
                    if (inspectedItem == it) imgPreview.Source = bs;
                });
            }
            else
            {
                imgPreview.Source = it.Thumb ?? (it.Thumb = ThumbCache.Get(it.Path));
            }
            txtInspTitle.Text = it.Name;
            txtInspMeta.Text = it.MetaLine;
            txtInspLoop.Text = it.InLoop ? "● Included in playlist loop" : "○ Excluded from playlist loop";
            txtInspLoop.Foreground = it.InLoop ? Theme.TextPrimary : Theme.TextMuted;
            if (btnInspLoopToggle != null)
                btnInspLoopToggle.Content = it.InLoop ? "− Exclude from Loop" : "＋ Include in Loop";
            if (btnInspOpen != null)
                btnInspOpen.Content = it.IsYouTube ? "Watch on YT" : "Open File";
            if (txtInspOrder != null && items != null)
            {
                int idx = items.IndexOf(it);
                txtInspOrder.Text = idx >= 0 ? ("#" + (idx + 1) + " of " + items.Count) : ("#" + it.OrderIndex);
            }
            if (!previewUserPaused) StartPreview(it);
            else UpdatePreviewToggleUi();
            RefreshCycleModeUi();
        }

        void RebuildFilteredItems()
        {
            RefreshOrderIndices();
            filteredItems.Clear();
            if (items == null) return;
            string q = (tbSearch.Text ?? "").Trim().ToLowerInvariant();
            foreach (var it in items)
            {
                if (filterMode == "loop" && !it.InLoop) continue;
                if (filterMode == "out" && it.InLoop) continue;
                if (q.Length > 0 && !it.Name.ToLowerInvariant().Contains(q)) continue;
                filteredItems.Add(it);
            }
            if (inspectedItem == null && filteredItems.Count > 0)
                InspectItem(filteredItems[0]);
            UpdateHeaderCounts();
            UpdateSelectionBar();
            RefreshCycleModeUi();
        }

        void UpdateHeaderCounts()
        {
            var board = currentPl;
            if (txtNavColLabel != null) txtNavColLabel.Text = "Collection (" + collectionPl.Items.Count + ")";
            if (txtNoPlaylistsHint != null) txtNoPlaylistsHint.Visibility = playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (board == null) return;
            int tot = board.Items.Count;
            int inLoop = board.Items.Count(i => i.InLoop);
            var loopDur = TimeSpan.FromSeconds(board.Items.Where(i => i.InLoop).Sum(i => i.Dur.TotalSeconds));
            txtHeaderTitle.Text = board == collectionPl ? "Collection" : board.Name;
            string durStr = (int)loopDur.TotalMinutes + ":" + loopDur.Seconds.ToString("00");
            txtHeaderSub.Text = tot + " wallpapers  ·  " + inLoop + " in loop (" + durStr + ")";
            if (filterBtnAll != null) filterBtnAll.Content = "All (" + tot + ")";
            if (filterBtnLoop != null) filterBtnLoop.Content = "Included (" + inLoop + ")";
            if (filterBtnOut != null) filterBtnOut.Content = "Excluded (" + (tot - inLoop) + ")";
        }

        // ==================== PLAYLISTS ====================
        Playlist GetOrCreatePlaylist(string name)
        {
            var p = playlists.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                p = new Playlist { Name = name };
                p.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "InMultiLoop" && !loading)
                    {
                        SaveLib();
                        UpdateMultiLoopUi();
                        SyncQueueAfterReorder();
                    }
                };
                playlists.Add(p);
            }
            return p;
        }

        List<Item> GetMultiLoopItems()
        {
            var list = new List<Item>();
            foreach (var pl in playlists.Where(p => p.InMultiLoop))
                foreach (var it in pl.Items)
                    if (it.InLoop) list.Add(it);
            return list;
        }

        void UpdateMultiLoopUi()
        {
            if (btnPlayMultiLoop == null) return;
            int cnt = playlists.Count(p => p.InMultiLoop);
            bool activeMulti = (mode == "multiloop" && host != null);
            btnPlayMultiLoop.Content = cnt > 0 ? ("▶  Multi-Loop (" + cnt + ")") : "▶  Multi-Playlist Loop";
            btnPlayMultiLoop.Style = SegBtnStyle(activeMulti, 8, 12, 8);
        }

        void StartMultiLoop()
        {
            var picked = playlists.Where(p => p.InMultiLoop).ToList();
            if (picked.Count == 0)
            {
                ShowMultiPlaylistLoopDialog();
                return;
            }
            PlaySet("multiloop");
        }

        void ShowMultiPlaylistLoopDialog()
        {
            if (playlists.Count == 0) return;
            var w = new Window
            {
                Title = "Multi-Playlist Loop",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = Theme.BgSidebar,
                Foreground = Theme.TextPrimary
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "Play Multiple Playlists in Loop", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary });
            sp.Children.Add(new TextBlock { Text = "Select playlists to cycle through continuously.", FontSize = 11.5, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 4, 0, 12) });

            var listSp = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var checks = new List<Tuple<Playlist, CheckBox>>();
            foreach (var pl in playlists)
            {
                var cb = new CheckBox
                {
                    Content = pl.Name + " (" + pl.Items.Count(i => i.InLoop) + " in loop)",
                    IsChecked = pl.InMultiLoop,
                    Foreground = Theme.TextPrimary,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 7)
                };
                checks.Add(Tuple.Create(pl, cb));
                listSp.Children.Add(cb);
            }
            sp.Children.Add(listSp);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var bCancel = StyledBtn("Cancel", () => w.Close(), false, false, 12, 6, 6);
            var bPlay = StyledBtn("▶  Play Multi-Loop", () =>
            {
                loading = true;
                foreach (var c in checks) c.Item1.InMultiLoop = (c.Item2.IsChecked == true);
                loading = false;
                SaveLib();
                UpdateMultiLoopUi();
                if (playlists.Any(p => p.InMultiLoop))
                {
                    w.Close();
                    PlaySet("multiloop");
                }
                else
                {
                    MessageBox.Show(w, "Select at least one playlist.", "Multi-Playlist Loop");
                }
            }, true, false, 14, 6, 6);
            bPlay.Margin = new Thickness(0);
            btns.Children.Add(bCancel);
            btns.Children.Add(bPlay);
            sp.Children.Add(btns);
            w.Content = sp;
            w.ShowDialog();
        }

        void SelectCollection(bool autoPlay)
        {
            activePl = null;
            foreach (var p in playlists) p.IsActive = false;
            inspectedItem = null;
            RefreshPlaylistUi();
            RebuildFilteredItems();
            UpdateSelectionBar();
            if (navBtnLib != null && viewLibrary != null)
                navBtnLib.Style = SegBtnStyle(viewLibrary.Visibility == Visibility.Visible && activePl == null, 8, 12, 9);
            SaveSettings();
            if (autoPlay && collectionPl.Items.Count > 0)
            {
                if (collectionPl.Items.Any(i => i.InLoop)) PlaySet("loop", true);
                else PlaySet("all", true);
            }
        }

        void SelectPlaylist(Playlist pl, bool autoPlay)
        {
            if (pl == null) { SelectCollection(false); return; }
            activePl = pl;
            foreach (var p in playlists) p.IsActive = (p == activePl);
            inspectedItem = null;
            RefreshPlaylistUi();
            RebuildFilteredItems();
            UpdateSelectionBar();
            if (navBtnLib != null && viewLibrary != null)
                navBtnLib.Style = SegBtnStyle(false, 8, 12, 9);
            SaveSettings();
            if (autoPlay && activePl.Items.Count > 0)
            {
                if (activePl.Items.Any(i => i.InLoop)) PlaySet("loop", true);
                else PlaySet("all", true);
            }
        }

        void RefreshPlaylistUi()
        {
            switchingPl = true;
            foreach (var p in playlists)
            {
                p.IsActive = (p == activePl);
                p.RefreshCounts();
            }
            lbSidebarPlaylists.ItemsSource = null;
            lbSidebarPlaylists.ItemsSource = playlists;
            lbSidebarPlaylists.SelectedItem = activePl;

            if (trayPlMenu != null)
            {
                trayPlMenu.DropDownItems.Clear();
                var colMi = new Forms.ToolStripMenuItem("Collection (" + collectionPl.Items.Count + ")", null, (s, e) => SelectCollection(true)) { Checked = (activePl == null) };
                trayPlMenu.DropDownItems.Add(colMi);
                if (playlists.Count > 0) trayPlMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
                foreach (var pl in playlists)
                {
                    var p = pl;
                    var mi = new Forms.ToolStripMenuItem(p.Title, null, (s, e) => SelectPlaylist(p, true)) { Checked = (p == activePl) };
                    trayPlMenu.DropDownItems.Add(mi);
                }
            }
            UpdateHeaderCounts();
            UpdateMultiLoopUi();
            switchingPl = false;
        }

        string PromptText(string title, string label, string defaultVal, out bool includeSelected, bool showIncludeOption)
        {
            includeSelected = false;
            var w = new Window
            {
                Title = title, Width = 390, Height = showIncludeOption ? 200 : 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, Background = Theme.BgSidebar, Foreground = Theme.TextPrimary
            };
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold });
            var tb = new TextBox { Text = defaultVal ?? "", Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12), Background = Theme.BgInput, Foreground = Theme.TextPrimary, BorderBrush = Theme.BorderSubtle };
            sp.Children.Add(tb);
            CheckBox chkInc = null;
            if (showIncludeOption)
            {
                var selCount = Sel().Count;
                chkInc = new CheckBox
                {
                    Content = "Include currently selected videos (" + selCount + ")",
                    Foreground = Theme.TextPrimary, IsChecked = selCount > 0, Margin = new Thickness(0, 0, 0, 12)
                };
                sp.Children.Add(chkInc);
            }
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            bool accepted = false;
            row.Children.Add(StyledBtn("Save", () => { accepted = true; w.Close(); }, true));
            row.Children.Add(StyledBtn("Cancel", () => w.Close()));
            sp.Children.Add(row);
            w.Content = sp;
            tb.Focus(); tb.SelectAll();
            w.ShowDialog();
            if (!accepted || string.IsNullOrWhiteSpace(tb.Text)) return null;
            if (chkInc != null) includeSelected = chkInc.IsChecked == true;
            return tb.Text.Trim().Replace("|", "-");
        }

        void CreatePlaylistDialog()
        {
            CreatePlaylistWithItems(null);
        }

        void CreatePlaylistWithItems(List<Item> initialItems)
        {
            bool inc;
            var seed = initialItems ?? new List<Item>();
            string name = PromptText("New Playlist", "Enter playlist name:", "Playlist " + (playlists.Count + 1), out inc, initialItems == null);
            if (string.IsNullOrEmpty(name)) return;
            if (playlists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A playlist with that name already exists.");
                return;
            }
            var toCopy = initialItems != null ? seed : (inc ? Sel() : new List<Item>());
            var pl = GetOrCreatePlaylist(name);
            foreach (var it in toCopy)
            {
                AddItemToPlaylist(pl, it.Path, true);
                EnsurePathInCollection(it.Path, it.IsYouTube ? it.YtVideoId : null, true);
            }
            SaveLib();
            SelectPlaylist(pl, pl.Items.Count > 0);
        }

        void AddItemsToPlaylistDialog(List<Item> sel)
        {
            if (sel == null || sel.Count == 0) { MessageBox.Show("Select a wallpaper card first."); return; }
            var targets = playlists.Where(p => p != activePl).ToList();

            var cm = new ContextMenu();
            foreach (var targetPl in targets)
            {
                var t = targetPl;
                var mi = new MenuItem { Header = "Add " + sel.Count + " item(s) to '" + t.Name + "'" };
                mi.Click += (s, e) =>
                {
                    int skipped = 0;
                    foreach (var i in sel)
                    {
                        EnsurePathInCollection(i.Path, i.IsYouTube ? i.YtVideoId : null, true);
                        if (FindDuplicateByHash(t, i.Path, i.IsYouTube ? i.YtVideoId : null) == null)
                            AddItemToPlaylist(t, i.Path, true);
                        else
                            skipped++;
                    }
                    SaveLib(); RefreshPlaylistUi(); ClearSelection();
                    if (skipped > 0) MessageBox.Show(skipped + " wallpaper(s) already exist in '" + t.Name + "'.");
                };
                cm.Items.Add(mi);
            }
            if (targets.Count > 0) cm.Items.Add(new Separator());
            var mNew = new MenuItem { Header = "＋ New Playlist with Selected..." };
            mNew.Click += (s, e) => { CreatePlaylistWithItems(sel); ClearSelection(); };
            cm.Items.Add(mNew);
            cm.IsOpen = true;
        }

        void ShowAddFromCollectionDialog(Playlist targetPl)
        {
            if (targetPl == null) return;
            if (collectionPl.Items.Count == 0)
            {
                MessageBox.Show("Your Collection is empty. Add videos or photos to Collection first.", "Add from Collection");
                return;
            }

            var w = new Window
            {
                Title = "Add from Collection",
                Width = 520,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = Theme.BgSidebar,
                Foreground = Theme.TextPrimary
            };

            var root = new DockPanel { Margin = new Thickness(20) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new TextBlock { Text = "Add from Collection to '" + targetPl.Name + "'", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary });
            head.Children.Add(new TextBlock { Text = "Select wallpapers from your Collection to include in this playlist.", FontSize = 11.5, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 3, 0, 10) });

            var tbFind = new TextBox
            {
                Padding = new Thickness(9, 7, 9, 7), FontSize = 12.5,
                Background = Theme.BgInput, Foreground = Theme.TextPrimary, CaretBrush = Brushes.White,
                BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1)
            };
            head.Children.Add(tbFind);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var checks = new List<Tuple<Item, CheckBox, Border>>();
            var listSp = new StackPanel();
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listSp };

            var foot = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btns, Dock.Right);
            var bSelAll = StyledBtn("Select All", () =>
            {
                foreach (var c in checks) if (c.Item2.IsEnabled && c.Item3.Visibility == Visibility.Visible) c.Item2.IsChecked = true;
            }, false, false, 11, 6, 6);
            DockPanel.SetDock(bSelAll, Dock.Left);
            foot.Children.Add(bSelAll);

            var bCancel = StyledBtn("Cancel", () => w.Close(), false, false, 12, 6, 6);
            var bAdd = StyledBtn("Add to Playlist", () =>
            {
                int added = 0;
                foreach (var c in checks)
                {
                    if (c.Item2.IsEnabled && c.Item2.IsChecked == true)
                    {
                        if (FindDuplicateByHash(targetPl, c.Item1.Path, c.Item1.IsYouTube ? c.Item1.YtVideoId : null) == null)
                        {
                            AddItemToPlaylist(targetPl, c.Item1.Path, true);
                            added++;
                        }
                    }
                }
                if (added > 0) { SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems(); }
                w.Close();
            }, true, false, 14, 6, 6);
            bAdd.Margin = new Thickness(0);
            btns.Children.Add(bCancel);
            btns.Children.Add(bAdd);
            foot.Children.Add(btns);
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);
            root.Children.Add(sv);

            foreach (var it in collectionPl.Items)
            {
                bool exists = FindDuplicateByHash(targetPl, it.Path, it.IsYouTube ? it.YtVideoId : null) != null;
                var rowBd = new Border
                {
                    Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6)
                };
                var dpRow = new DockPanel();
                var cb = new CheckBox
                {
                    IsEnabled = !exists,
                    IsChecked = exists,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                DockPanel.SetDock(cb, Dock.Left);
                dpRow.Children.Add(cb);

                var th = new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(4), ClipToBounds = true, Background = Theme.BgInput, Margin = new Thickness(0, 0, 10, 0) };
                th.Child = new Image { Source = it.Thumb ?? ThumbCache.Get(it.Path), Stretch = Stretch.UniformToFill };
                DockPanel.SetDock(th, Dock.Left);
                dpRow.Children.Add(th);

                var infoSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoSp.Children.Add(new TextBlock { Text = it.Name, Foreground = exists ? Theme.TextMuted : Theme.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
                infoSp.Children.Add(new TextBlock { Text = exists ? "Already in playlist" : it.MetaLine, Foreground = Theme.TextMuted, FontSize = 11 });
                dpRow.Children.Add(infoSp);
                rowBd.Child = dpRow;
                rowBd.MouseLeftButtonUp += (s, e) => { if (cb.IsEnabled && !(e.OriginalSource is CheckBox)) cb.IsChecked = !(cb.IsChecked == true); };
                checks.Add(Tuple.Create(it, cb, rowBd));
                listSp.Children.Add(rowBd);
            }

            tbFind.TextChanged += (s, e) =>
            {
                string q = (tbFind.Text ?? "").Trim().ToLowerInvariant();
                foreach (var c in checks)
                    c.Item3.Visibility = (q.Length == 0 || c.Item1.Name.ToLowerInvariant().Contains(q)) ? Visibility.Visible : Visibility.Collapsed;
            };

            w.Content = root;
            w.ShowDialog();
        }

        void RenamePlaylistDialog()
        {
            if (activePl == null) return;
            bool dummy;
            string name = PromptText("Rename Playlist", "New name for '" + activePl.Name + "':", activePl.Name, out dummy, false);
            if (string.IsNullOrEmpty(name) || string.Equals(name, activePl.Name, StringComparison.OrdinalIgnoreCase)) return;
            if (playlists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A playlist with that name already exists.");
                return;
            }
            activePl.Name = name;
            SaveLib(); SaveSettings(); RefreshPlaylistUi();
        }

        void DeleteCurrentPlaylist()
        {
            if (activePl == null) return;
            if (MessageBox.Show("Delete playlist '" + activePl.Name + "'? (Wallpapers remain in Collection and files on disk are not deleted)", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            playlists.Remove(activePl);
            SaveLib();
            if (playlists.Count > 0) SelectPlaylist(playlists[0], false);
            else SelectCollection(false);
        }

        // ==================== LIBRARY ====================
        List<Item> Sel()
        {
            if (items == null) return new List<Item>();
            var l = items.Where(i => i.Checked).ToList();
            if (l.Count == 0 && lbCards.SelectedItem is Item)
                l.Add((Item)lbCards.SelectedItem);
            return l;
        }

        static readonly string[] Exts = { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".webm", ".m4v", ".mpg", ".mpeg", ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };

        void AddFiles()
        {
            var d = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Wallpapers (Videos & Photos)|*.mp4;*.mkv;*.avi;*.wmv;*.mov;*.webm;*.m4v;*.mpg;*.mpeg;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|Videos|*.mp4;*.mkv;*.avi;*.wmv;*.mov;*.webm;*.m4v;*.mpg;*.mpeg|Photos & GIFs|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|All files|*.*"
            };
            if (d.ShowDialog() == true) AddPaths(d.FileNames);
        }

        void AddFolder()
        {
            var d = new Forms.FolderBrowserDialog();
            if (d.ShowDialog() == Forms.DialogResult.OK) AddPaths(new[] { d.SelectedPath });
        }

        void UpdateDlDirText()
        {
            if (txtDlDirSettings != null)
            {
                txtDlDirSettings.Text = YouTubeHelper.DownloadsDir;
                txtDlDirSettings.ToolTip = YouTubeHelper.DownloadsDir;
            }
        }

        void ChooseDownloadFolder(Action<string> onUpdated = null)
        {
            var d = new Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(YouTubeHelper.DownloadsDir) ? YouTubeHelper.DownloadsDir : YouTubeHelper.DefaultDownloadsDir };
            if (d.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(d.SelectedPath))
            {
                YouTubeHelper.CustomDownloadsDir = d.SelectedPath.Trim();
                try { Directory.CreateDirectory(YouTubeHelper.DownloadsDir); } catch { }
                UpdateDlDirText();
                SaveSettings();
                if (onUpdated != null) onUpdated(YouTubeHelper.DownloadsDir);
            }
        }

        void ShowYouTubeDialog()
        {
            var ytTargetPl = currentPl;
            if (ytTargetPl == null) return;
            var w = new Window
            {
                Title = "Add from YouTube",
                Width = 560, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.NoResize, Background = Theme.BgSidebar, Foreground = Theme.TextPrimary
            };

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
            root.Children.Add(new TextBlock { Text = "Add from YouTube", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary });
            root.Children.Add(new TextBlock { Text = "Download as MP4 or stream directly from a YouTube link.", FontSize = 12, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 3, 0, 14) });

            // URL Input Row
            root.Children.Add(new TextBlock { Text = "YOUTUBE VIDEO LINK", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 6) });
            var urlGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string initUrl = "";
            try { string clip = Clipboard.GetText(); if (YouTubeHelper.IsYouTubeUrl(clip)) initUrl = clip.Trim(); } catch { }

            var tbUrl = new TextBox
            {
                Text = initUrl, Padding = new Thickness(10, 8, 10, 8), FontSize = 12.5,
                Background = Theme.BgInput, Foreground = Theme.TextPrimary, CaretBrush = Brushes.White,
                BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(tbUrl, 0);
            urlGrid.Children.Add(tbUrl);

            var btnCheck = StyledBtn("Check Qualities", null, false, false, 12, 8);
            btnCheck.Margin = new Thickness(0);
            Grid.SetColumn(btnCheck, 1);
            urlGrid.Children.Add(btnCheck);
            root.Children.Add(urlGrid);

            // Preview Card (hidden until info checked)
            var infoCard = new Border
            {
                Visibility = Visibility.Collapsed, Background = Theme.BgCard, BorderBrush = Theme.BorderSubtle,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 14)
            };
            var infoDock = new DockPanel();
            var imgYtThumb = new Image { Width = 112, Height = 63, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 12, 0) };
            var thumbWrap = new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = Theme.BgInput };
            thumbWrap.Child = imgYtThumb;
            DockPanel.SetDock(thumbWrap, Dock.Left);
            infoDock.Children.Add(thumbWrap);
            var infoTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var txtYtTitle = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis };
            var txtYtMeta = new TextBlock { FontSize = 11.5, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 3, 0, 0) };
            infoTxt.Children.Add(txtYtTitle); infoTxt.Children.Add(txtYtMeta);
            infoDock.Children.Add(infoTxt);
            infoCard.Child = infoDock;
            root.Children.Add(infoCard);

            // Quality Picker
            root.Children.Add(new TextBlock { Text = "QUALITY", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 6) });
            int selectedHeight = 2160;
            int maxDetectedHeight = 0; // 0 = not checked yet (all options clickable until video max is known)
            var qWrap = new Border { Background = Theme.BgInput, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Left };
            var qSp = new StackPanel { Orientation = Orientation.Horizontal };
            var qOpts = new[]
            {
                Tuple.Create(4320, "8K (4320p)"),
                Tuple.Create(2160, "4K (2160p)"),
                Tuple.Create(1440, "2K (1440p)"),
                Tuple.Create(1080, "1080p HD"),
                Tuple.Create(720, "720p")
            };
            var qBtns = new List<Tuple<int, Button>>();
            Action refreshQ = () =>
            {
                if (maxDetectedHeight > 0 && selectedHeight > maxDetectedHeight)
                {
                    var fallback = qOpts.Select(o => o.Item1).Where(h => h <= maxDetectedHeight).OrderByDescending(h => h).FirstOrDefault();
                    selectedHeight = fallback > 0 ? fallback : 720;
                }
                foreach (var qb in qBtns)
                {
                    bool allowed = maxDetectedHeight <= 0 || qb.Item1 <= maxDetectedHeight;
                    qb.Item2.IsEnabled = allowed;
                    if (!allowed)
                    {
                        qb.Item2.Style = MakeBtnStyle(Theme.B(10, 10, 12), Theme.B(10, 10, 12), Theme.B(10, 10, 12), Theme.B(58, 58, 64), Brushes.Transparent, 6, 12, 6);
                        qb.Item2.Opacity = 0.42;
                        qb.Item2.ToolTip = "Not available (video max is " + YouTubeHelper.HeightToLabel(maxDetectedHeight) + ")";
                    }
                    else
                    {
                        qb.Item2.Style = SegBtnStyle(qb.Item1 == selectedHeight, 6, 12, 6);
                        qb.Item2.Opacity = 1.0;
                        qb.Item2.ToolTip = null;
                    }
                }
            };
            foreach (var opt in qOpts)
            {
                int hVal = opt.Item1;
                var b = StyledBtn(opt.Item2, () => { if (maxDetectedHeight <= 0 || hVal <= maxDetectedHeight) { selectedHeight = hVal; refreshQ(); } }, hVal == selectedHeight, false, 11, 6, 6);
                b.Margin = new Thickness(1);
                qBtns.Add(Tuple.Create(hVal, b));
                qSp.Children.Add(b);
            }
            refreshQ();
            qWrap.Child = qSp;
            root.Children.Add(qWrap);

            // Mode Picker: Download MP4 vs Direct Play Stream
            root.Children.Add(new TextBlock { Text = "MODE", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(0, 0, 0, 6) });
            bool modeDownload = true;
            var mWrap = new Border { Background = Theme.BgInput, BorderBrush = Theme.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Left };
            var mSp = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnModeDl = null, btnModeStream = null, btnGo = null;
            Action refreshMode = () =>
            {
                if (btnModeDl != null) btnModeDl.Style = SegBtnStyle(modeDownload, 6, 14, 7);
                if (btnModeStream != null) btnModeStream.Style = SegBtnStyle(!modeDownload, 6, 14, 7);
                if (btnGo != null) btnGo.Content = modeDownload ? "Download & Play" : "Stream & Play";
            };
            btnModeDl = StyledBtn("Download MP4", () => { modeDownload = true; refreshMode(); }, true, false, 12, 7, 6);
            btnModeStream = StyledBtn("Stream Only", () => { modeDownload = false; refreshMode(); }, false, false, 12, 7, 6);
            btnModeDl.Margin = new Thickness(1); btnModeStream.Margin = new Thickness(1);
            mSp.Children.Add(btnModeDl); mSp.Children.Add(btnModeStream);
            mWrap.Child = mSp;
            root.Children.Add(mWrap);

            // Options Checkboxes
            var chkAddPl = new CheckBox { Content = activePl != null ? "Add to playlist '" + activePl.Name + "' & Collection" : "Add to Collection", IsChecked = true, Foreground = Theme.TextPrimary, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            var chkPlayNow = new CheckBox { Content = "Play immediately as desktop wallpaper", IsChecked = true, Foreground = Theme.TextPrimary, FontSize = 12, Margin = new Thickness(0, 0, 0, 14) };
            root.Children.Add(chkAddPl);
            root.Children.Add(chkPlayNow);

            // Progress Area
            var progPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) };
            var txtProg = new TextBlock { Text = "Preparing...", FontSize = 11.5, Foreground = Theme.TextSecondary, Margin = new Thickness(0, 0, 0, 6) };
            var pb = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Background = Theme.BgInput, Foreground = Theme.Accent, BorderThickness = new Thickness(0) };
            progPanel.Children.Add(txtProg);
            progPanel.Children.Add(pb);
            root.Children.Add(progPanel);

            // Action Footer
            var foot = new DockPanel();
            var destRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 285 };
            var txtDest = new TextBlock { Text = "Save: " + YouTubeHelper.DownloadsDir, ToolTip = YouTubeHelper.DownloadsDir, FontSize = 10.5, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 215 };
            var btnChangeDest = StyledBtn("Change", () => ChooseDownloadFolder(p => { txtDest.Text = "Save: " + p; txtDest.ToolTip = p; }), false, false, 7, 2, 5);
            btnChangeDest.FontSize = 10.5; btnChangeDest.Margin = new Thickness(6, 0, 0, 0);
            destRow.Children.Add(txtDest);
            destRow.Children.Add(btnChangeDest);
            DockPanel.SetDock(destRow, Dock.Left);
            foot.Children.Add(destRow);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            CancellationTokenSource cts = null;
            bool busy = false;

            var btnCancel = StyledBtn("Cancel", () =>
            {
                if (busy && cts != null) { cts.Cancel(); txtProg.Text = "Cancelling..."; }
                else w.Close();
            }, false, false, 12, 7);
            btnGo = StyledBtn("⬇  Download & Play", null, true, false, 16, 7);
            btnGo.Margin = new Thickness(0);
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnGo);
            foot.Children.Add(btnRow);
            root.Children.Add(foot);

            YouTubeHelper.VideoInfo fetchedInfo = null;
            w.Closing += (s, e) => { if (cts != null) cts.Cancel(); };

            tbUrl.TextChanged += (s, e) =>
            {
                maxDetectedHeight = 0;
                fetchedInfo = null;
                refreshQ();
            };

            Action<string> setStatus = (msg) =>
            {
                w.Dispatcher.BeginInvoke(new Action(() =>
                {
                    progPanel.Visibility = Visibility.Visible;
                    txtProg.Text = msg;
                }));
            };

            Func<bool, Task> runCheckAsync = async (silentIfEmpty) =>
            {
                string raw = tbUrl.Text.Trim();
                string vid = YouTubeHelper.ExtractVideoId(raw);
                if (string.IsNullOrEmpty(vid)) { if (!silentIfEmpty) MessageBox.Show("Please paste a valid YouTube video URL."); return; }
                btnCheck.IsEnabled = false; btnCheck.Content = "Checking...";
                cts = new CancellationTokenSource();
                try
                {
                    if (!await YouTubeHelper.EnsureToolsAsync(false, (msg, pct) =>
                    {
                        w.Dispatcher.BeginInvoke(new Action(() => { progPanel.Visibility = Visibility.Visible; txtProg.Text = msg; pb.Value = pct; }));
                    }, cts.Token)) return;

                    setStatus("Fetching available qualities...");
                    var info = await YouTubeHelper.GetVideoInfoAsync(raw, cts.Token);
                    if (info != null)
                    {
                        fetchedInfo = info;
                        if (info.MaxHeight > 0)
                        {
                            maxDetectedHeight = info.MaxHeight;
                            refreshQ();
                        }
                        infoCard.Visibility = Visibility.Visible;
                        txtYtTitle.Text = info.Title;
                        string durStr = info.DurationSec > 0 ? TimeSpan.FromSeconds(info.DurationSec).ToString(@"m\:ss") : "--:--";
                        string avail = info.AvailableHeights.Count > 0 ? string.Join(", ", info.AvailableHeights.Where(x => x >= 720).Select(x => YouTubeHelper.HeightToLabel(x)).Distinct().Take(5)) : "Standard";
                        txtYtMeta.Text = "Duration: " + durStr + "  ·  Available: " + avail + "  ·  Max: " + YouTubeHelper.HeightToLabel(info.MaxHeight);
                        ThumbCache.GetYouTubeAsync(info.Id, bs => { imgYtThumb.Source = bs; });
                        progPanel.Visibility = Visibility.Collapsed;
                    }
                    else if (!silentIfEmpty) setStatus("Could not fetch video details. You can still try Download / Stream.");
                    else progPanel.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex) { if (!silentIfEmpty) setStatus("Check failed: " + ex.Message); }
                finally { btnCheck.IsEnabled = true; btnCheck.Content = "Check Qualities"; }
            };

            btnCheck.Click += async (s, e) => { await runCheckAsync(false); };
            if (!string.IsNullOrEmpty(initUrl))
                w.Loaded += async (s, e) => { await runCheckAsync(true); };

            btnGo.Click += async (s, e) =>
            {
                if (busy) return;
                string raw = tbUrl.Text.Trim();
                string vid = YouTubeHelper.ExtractVideoId(raw);
                if (string.IsNullOrEmpty(vid)) { MessageBox.Show("Please paste a valid YouTube link first."); return; }

                busy = true;
                cts = new CancellationTokenSource();
                btnGo.IsEnabled = false; btnCheck.IsEnabled = false; tbUrl.IsEnabled = false;
                progPanel.Visibility = Visibility.Visible;
                pb.IsIndeterminate = false; pb.Value = 0;

                try
                {
                    if (chkAddPl.IsChecked == true)
                    {
                        var ytDup = FindDuplicateByHash(ytTargetPl, null, vid);
                        if (ytDup != null)
                        {
                            setStatus("Already exists in '" + ytTargetPl.Name + "' as '" + ytDup.Name + "'!");
                            MessageBox.Show("This YouTube video already exists in '" + ytTargetPl.Name + "' as '" + ytDup.Name + "'.", "Already Exists");
                            busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true;
                            return;
                        }
                    }

                    if (fetchedInfo == null || fetchedInfo.Id != vid)
                    {
                        setStatus("Checking video max quality...");
                        pb.Value = 5;
                        if (await YouTubeHelper.EnsureToolsAsync(false, (msg, pct) =>
                        {
                            w.Dispatcher.BeginInvoke(new Action(() => { txtProg.Text = msg; pb.Value = pct * 0.1; }));
                        }, cts.Token))
                        {
                            var chkInfo = await YouTubeHelper.GetVideoInfoAsync(raw, cts.Token);
                            if (chkInfo != null)
                            {
                                fetchedInfo = chkInfo;
                                if (chkInfo.MaxHeight > 0) { maxDetectedHeight = chkInfo.MaxHeight; refreshQ(); }
                            }
                        }
                        if (cts.IsCancellationRequested) { setStatus("Cancelled."); busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true; return; }
                    }
                    if (modeDownload)
                    {
                        string outFile = await YouTubeHelper.DownloadVideoAsync(raw, selectedHeight, (msg, pct) =>
                        {
                            w.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                txtProg.Text = msg;
                                pb.Value = Math.Max(0, Math.Min(100, pct));
                            }));
                        }, cts.Token);

                        if (cts.IsCancellationRequested) { setStatus("Cancelled."); busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true; return; }
                        if (string.IsNullOrEmpty(outFile) || !File.Exists(outFile))
                        {
                            setStatus("Download failed. Verify link or internet connection.");
                            busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true;
                            return;
                        }

                        if (chkAddPl.IsChecked == true)
                        {
                            setStatus("Verifying content hash (SHA-256)...");
                            var hashDup = await Task.Run(() => FindDuplicateByHash(ytTargetPl, outFile, vid));
                            if (hashDup != null)
                            {
                                if (!string.Equals(outFile, hashDup.Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { File.Delete(outFile); } catch { }
                                }
                                setStatus("Already exists in '" + ytTargetPl.Name + "' as '" + hashDup.Name + "'!");
                                MessageBox.Show("Already exists in '" + ytTargetPl.Name + "'!\n\nDownloaded file matches existing wallpaper:\n'" + hashDup.Name + "' (identical content hash).", "Already Exists");
                                busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true;
                                return;
                            }
                        }

                        Item newItem = null;
                        if (chkAddPl.IsChecked == true)
                        {
                            newItem = AddItemToPlaylist(ytTargetPl, outFile, true);
                            EnsurePathInCollection(outFile, vid);
                            SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems();
                        }
                        else
                        {
                            newItem = new Item { Path = outFile, InLoop = false, Dur = Mp4Info.GetDuration(outFile), Thumb = ThumbCache.Get(outFile) };
                        }

                        if (chkPlayNow.IsChecked == true && newItem != null)
                        {
                            if (chkAddPl.IsChecked == true) PlayLoopFrom(newItem);
                            else PlayOne(newItem);
                        }
                        w.Close();
                    }
                    else
                    {
                        setStatus("Resolving " + YouTubeHelper.HeightToLabel(selectedHeight) + " stream from YouTube...");
                        pb.Value = 25;
                        if (fetchedInfo == null || fetchedInfo.Id != vid)
                            fetchedInfo = await YouTubeHelper.GetVideoInfoAsync(raw, cts.Token);

                        string title = fetchedInfo != null ? fetchedInfo.Title : ("YouTube " + vid);
                        double dur = fetchedInfo != null ? fetchedInfo.DurationSec : 0;

                        pb.Value = 60;
                        var streamUrls = await YouTubeHelper.ResolveStreamUrlsAsync(vid, selectedHeight, cts.Token);
                        if (cts.IsCancellationRequested || streamUrls == null)
                        {
                            setStatus(cts.IsCancellationRequested ? "Cancelled." : "Could not resolve YouTube stream.");
                            busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true;
                            return;
                        }
                        pb.Value = 100;

                        string streamPath = YouTubeHelper.BuildStreamPath(vid, selectedHeight, title, dur);
                        Item streamItem = null;
                        if (chkAddPl.IsChecked == true)
                        {
                            streamItem = ytTargetPl.Items.FirstOrDefault(x => x.Path == streamPath);
                            if (streamItem == null)
                            {
                                streamItem = AddItemToPlaylist(ytTargetPl, streamPath, true);
                                EnsurePathInCollection(streamPath, vid);
                                SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems();
                            }
                        }
                        else
                        {
                            streamItem = new Item { Path = streamPath, InLoop = false, Dur = TimeSpan.FromSeconds(dur) };
                            ThumbCache.GetYouTubeAsync(vid, bs => { streamItem.Thumb = bs; });
                        }

                        if (chkPlayNow.IsChecked == true && streamItem != null)
                        {
                            if (chkAddPl.IsChecked == true) PlayLoopFrom(streamItem);
                            else PlayOne(streamItem);
                        }
                        w.Close();
                    }
                }
                catch (Exception ex)
                {
                    setStatus("Error: " + ex.Message);
                    busy = false; btnGo.IsEnabled = true; btnCheck.IsEnabled = true; tbUrl.IsEnabled = true;
                }
            };

            refreshMode();
            w.Content = root;
            w.ShowDialog();
        }

        Item FindDuplicateByHash(Playlist pl, string candidatePath, string ytVideoId = null)
        {
            if (pl == null) return null;
            if (!string.IsNullOrEmpty(ytVideoId))
            {
                var ytMatch = pl.Items.FirstOrDefault(x =>
                    (x.IsYouTube && string.Equals(x.YtVideoId, ytVideoId, StringComparison.Ordinal)) ||
                    (!x.IsYouTube && !string.IsNullOrEmpty(x.Path) && x.Path.IndexOf("-" + ytVideoId + "]", StringComparison.Ordinal) >= 0));
                if (ytMatch != null) return ytMatch;
            }
            if (string.IsNullOrEmpty(candidatePath) || !File.Exists(candidatePath)) return null;
            var exactPath = pl.Items.FirstOrDefault(x => string.Equals(x.Path, candidatePath, StringComparison.OrdinalIgnoreCase));
            if (exactPath != null) return exactPath;

            long candSize = 0;
            try { candSize = new FileInfo(candidatePath).Length; } catch { }
            if (candSize <= 0) return null;

            string candHash = null;
            foreach (var existing in pl.Items)
            {
                if (existing.IsYouTube || string.IsNullOrEmpty(existing.Path) || !File.Exists(existing.Path)) continue;
                if (existing.SizeBytes <= 0) { try { existing.SizeBytes = new FileInfo(existing.Path).Length; } catch { } }
                if (existing.SizeBytes != candSize) continue;

                if (candHash == null) candHash = FileHashHelper.GetSha256(candidatePath);
                if (string.IsNullOrEmpty(candHash)) break;
                string exHash = existing.EnsureHash();
                if (!string.IsNullOrEmpty(exHash) && string.Equals(candHash, exHash, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            return null;
        }

        void CollectMediaFiles(IEnumerable<string> paths, List<string> outFiles)
        {
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) CollectMediaFiles(Directory.GetFiles(p, "*", SearchOption.AllDirectories), outFiles);
                    else if (File.Exists(p) && Exts.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()))
                        outFiles.Add(p);
                }
                catch { }
            }
        }

        void EnsurePathInCollection(string p, string ytVideoId = null, bool inLoop = true)
        {
            if (string.IsNullOrEmpty(p)) return;
            if (FindDuplicateByHash(collectionPl, p, ytVideoId) == null)
                AddItemToPlaylist(collectionPl, p, inLoop);
        }

        void AddPaths(IEnumerable<string> paths)
        {
            var target = currentPl;
            if (target == null) return;
            var candidates = new List<string>();
            CollectMediaFiles(paths, candidates);
            if (candidates.Count == 0) return;

            int added = 0;
            var dups = new List<string>();
            foreach (var p in candidates)
            {
                var dup = FindDuplicateByHash(target, p);
                if (dup != null)
                {
                    dups.Add("• " + System.IO.Path.GetFileName(p) + "  →  already exists as '" + dup.Name + "'");
                    continue;
                }
                AddItemToPlaylist(target, p, true);
                if (target != collectionPl) EnsurePathInCollection(p);
                added++;
            }
            if (added > 0) { SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems(); }

            if (dups.Count > 0)
            {
                string detail = string.Join("\n", dups.Take(8));
                if (dups.Count > 8) detail += "\n...and " + (dups.Count - 8) + " more.";
                MessageBox.Show(
                    "Duplicate content detected (SHA-256 hash matched — already exists in '" + target.Name + "'):\n\n" + detail,
                    "Already Exists");
            }
        }

        Item AddItemToPlaylist(Playlist pl, string p, bool inl)
        {
            bool isYt = p.StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase);
            long sz = 0;
            if (!isYt) { try { sz = new FileInfo(p).Length; } catch { } }
            TimeSpan dur = TimeSpan.Zero;
            if (isYt)
            {
                double ds;
                if (double.TryParse(YouTubeHelper.ParseStreamField(p, "dur"), out ds) && ds > 0)
                    dur = TimeSpan.FromSeconds(ds);
            }

            var it = new Item
            {
                Path = p, InLoop = inl, SizeBytes = sz,
                Dur = dur,
                Thumb = null
            };
            if (isYt)
            {
                ThumbCache.GetYouTubeAsync(it.YtVideoId, bs => { it.Thumb = bs; });
            }
            else
            {
                ThumbCache.GetAsync(p, bs => { it.Thumb = bs; if (inspectedItem == it && imgPreview != null) imgPreview.Source = bs; });
                if (!it.IsStaticPhoto)
                {
                    Task.Run(() =>
                    {
                        var d = Mp4Info.GetDuration(p);
                        if (d.TotalSeconds > 0)
                            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { it.Dur = d; }));
                    });
                }
            }

            pl.Items.Add(it);
            it.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "InLoop" && !loading)
                {
                    SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems();
                }
                else if (e.PropertyName == "Checked" && !loading)
                {
                    UpdateSelectionBar();
                }
            };
            return it;
        }

        void SetLoop(List<Item> l, bool v)
        {
            if (l == null || l.Count == 0) return;
            bool prev = loading;
            loading = true;
            foreach (var i in l) i.InLoop = v;
            loading = prev;
            SaveLib();
            RefreshPlaylistUi();
            RebuildFilteredItems();
        }

        void RemoveItemsFromCurrentView(List<Item> l)
        {
            if (items == null || l == null || l.Count == 0) return;
            bool isCol = (activePl == null);
            string msg = isCol
                ? "Remove " + l.Count + " wallpaper(s) from Collection?\n\nThey will also be removed from playlists. Files on disk are untouched."
                : "Remove " + l.Count + " wallpaper(s) from playlist '" + activePl.Name + "'?\n\nThey remain in Collection and files on disk are untouched.";
            if (MessageBox.Show(msg, "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            foreach (var i in l)
            {
                if (inspectedItem == i) { inspectedItem = null; StopPreview(); }
                items.Remove(i);
                queue.Remove(i);
                if (isCol)
                {
                    foreach (var pl in playlists)
                    {
                        var matches = pl.Items.Where(x => string.Equals(x.Path, i.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var m in matches) { pl.Items.Remove(m); queue.Remove(m); }
                    }
                }
            }
            SaveLib(); RefreshPlaylistUi(); RebuildFilteredItems(); ClearSelection();
        }

        void DeleteSel()
        {
            RemoveItemsFromCurrentView(Sel());
        }

        void SaveLib()
        {
            if (loading) return;
            var colLines = new List<string>();
            foreach (var i in collectionPl.Items)
                colLines.Add((i.InLoop ? "1" : "0") + "|" + i.Path);
            File.WriteAllLines(colFile, colLines.ToArray());

            var lines = new List<string>();
            foreach (var pl in playlists)
            {
                lines.Add("@" + pl.Name + "|" + (pl.InMultiLoop ? "1" : "0"));
                foreach (var i in pl.Items)
                    lines.Add(pl.Name + "|" + (i.InLoop ? "1" : "0") + "|" + i.Path);
            }
            File.WriteAllLines(plFile, lines.ToArray());
        }

        void LoadAll()
        {
            if (File.Exists(colFile))
            {
                foreach (var l in File.ReadAllLines(colFile))
                {
                    var parts = l.Split(new[] { '|' }, 2);
                    if (parts.Length == 2 && (File.Exists(parts[1]) || parts[1].StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!collectionPl.Items.Any(x => x.Path == parts[1]))
                            AddItemToPlaylist(collectionPl, parts[1], parts[0] == "1");
                    }
                }
            }
            else if (File.Exists(libFile))
            {
                foreach (var l in File.ReadAllLines(libFile))
                    if (l.Length > 2 && File.Exists(l.Substring(2)))
                        EnsurePathInCollection(l.Substring(2), null, l[0] == '1');
            }

            if (File.Exists(plFile))
            {
                foreach (var l in File.ReadAllLines(plFile))
                {
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    if (l.StartsWith("@") && l.Length > 1)
                    {
                        var meta = l.Substring(1).Split(new[] { '|' }, 2);
                        var plMeta = GetOrCreatePlaylist(meta[0]);
                        if (meta.Length > 1 && meta[1] == "1") plMeta.InMultiLoop = true;
                        continue;
                    }
                    var parts = l.Split(new[] { '|' }, 3);
                    if (parts.Length == 3 && (File.Exists(parts[2]) || parts[2].StartsWith("ytstream://", StringComparison.OrdinalIgnoreCase)))
                    {
                        var pl = GetOrCreatePlaylist(parts[0]);
                        if (!pl.Items.Any(x => x.Path == parts[2]))
                            AddItemToPlaylist(pl, parts[2], parts[1] == "1");
                        EnsurePathInCollection(parts[2], null, true);
                    }
                }
            }

            string savedPl = null;
            bool hasTtbSetting = false, ttbVal = false;
            if (File.Exists(setFile))
            {
                foreach (var l in File.ReadAllLines(setFile))
                {
                    var kv = l.Split(new[] { '=' }, 2); if (kv.Length != 2) continue;
                    if (kv[0] == "shuffle") shuffleEnabled = kv[1] == "1";
                    if (kv[0] == "mute") muteEnabled = kv[1] == "1";
                    if (kv[0] == "vol") vol.Value = double.Parse(kv[1]);
                    if (kv[0] == "fit") SelectFitMode(int.Parse(kv[1]));
                    if (kv[0] == "ruleother") SelectRuleOther(int.Parse(kv[1]));
                    if (kv[0] == "rulecov") SelectRuleCovered(int.Parse(kv[1]));
                    if (kv[0] == "playlist") savedPl = kv[1];
                    if (kv[0] == "ttb") { hasTtbSetting = true; ttbVal = kv[1] == "1"; }
                    if (kv[0] == "cyclebytimer") cycleByTimer = kv[1] == "1";
                    if (kv[0] == "timerh") int.TryParse(kv[1], out timerHours);
                    if (kv[0] == "timerm") int.TryParse(kv[1], out timerMinutes);
                    if (kv[0] == "timers") int.TryParse(kv[1], out timerSeconds);
                    if (kv[0] == "deskhud") deskHudEnabled = kv[1] != "0";
                    if (kv[0] == "dldir" && !string.IsNullOrWhiteSpace(kv[1])) YouTubeHelper.CustomDownloadsDir = kv[1].Trim();
                }
            }
            else muteEnabled = false;

            loading = true;
            if (swDeskHudSettings != null) swDeskHudSettings.IsChecked = deskHudEnabled;
            loading = false;
            UpdateDlDirText();
            UpdateShuffleVisual();
            UpdateMuteVisual();
            SetTimerValues(timerHours, timerMinutes, timerSeconds, false);
            SetCycleMode(cycleByTimer);

            if (hasTtbSetting)
            {
                SyncTtbCheckboxes(ttbVal);
                TaskbarHelper.SetTransparent(ttbVal);
            }
            else SyncTtbCheckboxes(false);

            SyncStartupCheckboxes(StartupHelper.IsEnabled());

            var startPl = (!string.IsNullOrEmpty(savedPl) && savedPl != "__COLLECTION__")
                ? playlists.FirstOrDefault(p => string.Equals(p.Name, savedPl, StringComparison.OrdinalIgnoreCase))
                : null;
            if (startPl != null) SelectPlaylist(startPl, false);
            else SelectCollection(false);
            SaveLib();
        }

        int GetTimerTotalSeconds()
        {
            int tot = timerHours * 3600 + timerMinutes * 60 + timerSeconds;
            return Math.Max(3, tot);
        }

        bool IsCurrentMediaPicture()
        {
            Item refItem = cur ?? inspectedItem ?? (filteredItems.Count > 0 ? filteredItems[0] : (items != null && items.Count > 0 ? items[0] : null));
            return refItem != null && refItem.IsPhotoOrGif;
        }

        void RefreshCycleModeUi()
        {
            bool isPic = IsCurrentMediaPicture();
            if (btnCycleEndHeader != null)
            {
                btnCycleEndHeader.Content = isPic ? "∞ Indefinite" : "↻ Video End";
                btnCycleEndHeader.ToolTip = isPic
                    ? "Pictures stay on screen indefinitely until you click Next or switch to ⏱ Timer"
                    : "Advance to next wallpaper when the current video finishes";
                btnCycleEndHeader.Style = SegBtnStyle(!cycleByTimer, 6, 10, 5);
            }
            if (btnCycleTimerHeader != null) btnCycleTimerHeader.Style = SegBtnStyle(cycleByTimer, 6, 10, 5);
            if (timerPanelHeader != null) timerPanelHeader.Visibility = cycleByTimer ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetCycleMode(bool byTimer)
        {
            cycleByTimer = byTimer;
            RefreshCycleModeUi();
            timerRemainingSec = GetTimerTotalSeconds();
            lastTimerTick = DateTime.UtcNow;
            SaveSettings();
        }

        void SetTimerValues(int h, int m, int s, bool enableTimerMode)
        {
            timerHours = Math.Max(0, Math.Min(99, h));
            timerMinutes = Math.Max(0, Math.Min(59, m));
            timerSeconds = Math.Max(0, Math.Min(59, s));
            if (timerHours == 0 && timerMinutes == 0 && timerSeconds < 3) timerSeconds = 5;

            bool prevLoad = loading;
            loading = true;
            string hs = timerHours.ToString("00"), ms = timerMinutes.ToString("00"), ss = timerSeconds.ToString("00");
            if (tbHrHeader != null) tbHrHeader.Text = hs;
            if (tbMinHeader != null) tbMinHeader.Text = ms;
            if (tbSecHeader != null) tbSecHeader.Text = ss;
            loading = prevLoad;

            timerRemainingSec = GetTimerTotalSeconds();
            lastTimerTick = DateTime.UtcNow;
            if (enableTimerMode) SetCycleMode(true);
            else SaveSettings();
        }

        void ParseTimerInputs(bool fromHeader)
        {
            int h = 0, m = 0, s = 0;
            int.TryParse(tbHrHeader != null ? tbHrHeader.Text : "0", out h);
            int.TryParse(tbMinHeader != null ? tbMinHeader.Text : "0", out m);
            int.TryParse(tbSecHeader != null ? tbSecHeader.Text : "0", out s);
            timerHours = Math.Max(0, Math.Min(99, h));
            timerMinutes = Math.Max(0, Math.Min(59, m));
            timerSeconds = Math.Max(0, Math.Min(59, s));
            timerRemainingSec = GetTimerTotalSeconds();
            lastTimerTick = DateTime.UtcNow;
            SaveSettings();
        }

        void SaveSettings()
        {
            if (loading || vol == null) return;
            File.WriteAllLines(setFile, new[]
            {
                "shuffle=" + (shuffleEnabled ? 1 : 0),
                "mute=" + (muteEnabled ? 1 : 0),
                "vol=" + (int)vol.Value,
                "fit=" + fitMode,
                "ruleother=" + ruleOtherApp,
                "rulecov=" + ruleCovered,
                "ttb=" + (ttbEnabled ? 1 : 0),
                "cyclebytimer=" + (cycleByTimer ? 1 : 0),
                "timerh=" + timerHours,
                "timerm=" + timerMinutes,
                "timers=" + timerSeconds,
                "deskhud=" + (deskHudEnabled ? 1 : 0),
                "dldir=" + (YouTubeHelper.CustomDownloadsDir ?? ""),
                "playlist=" + (activePl != null ? activePl.Name : "__COLLECTION__")
            });
        }

        // ==================== PLAYBACK ENGINE ====================
        void PlaySet(string m, bool silent = false)
        {
            if (items == null && m != "multiloop") return;
            var src = m == "sel" ? Sel() : m == "all" ? items.ToList() : m == "multiloop" ? GetMultiLoopItems() : items.Where(i => i.InLoop).ToList();
            if (src.Count == 0)
            {
                if (!silent) MessageBox.Show(m == "sel" ? "Tick or select some wallpaper cards first." : m == "multiloop" ? "Selected playlists have no wallpapers included in loop." : "No wallpapers in this playlist loop.");
                return;
            }
            mode = m; queue = src;
            if (shuffleEnabled) queue = queue.OrderBy(x => rnd.Next()).ToList();
            qi = -1; cur = null; fails = 0; UpdateMultiLoopUi(); Step(1);
        }

        void PlayLoopFrom(Item startItem)
        {
            if (items == null || startItem == null) return;
            startItem.InLoop = true;
            mode = "loop";
            queue = items.Where(i => i.InLoop).ToList();
            qi = Math.Max(0, queue.IndexOf(startItem));
            fails = 0;
            Play(queue[qi]);
        }

        void PlayOne(Item it)
        {
            mode = "single";
            queue = new List<Item> { it };
            qi = -1; cur = null; fails = 0;
            Step(1);
        }

        bool Resolve(int d, out List<Item> nq, out int ni)
        {
            nq = queue; ni = qi; if (queue.Count == 0) return false;
            int q = qi;
            if (mode == "loop" || mode == "multiloop")
            {
                if (mode == "loop" && items == null) return false;
                var sourceList = mode == "multiloop" ? GetMultiLoopItems() : items.Where(i => i.InLoop).ToList();
                int idx = cur != null ? queue.IndexOf(cur) : -1;
                if (!shuffleEnabled)
                {
                    nq = sourceList;
                }
                else
                {
                    nq = queue.Where(i => sourceList.Contains(i)).ToList();
                    foreach (var i in sourceList) if (!nq.Contains(i)) nq.Add(i);
                }
                if (nq.Count == 0) return false;
                var keep = nq;
                if (cur != null && nq.Contains(cur)) q = nq.IndexOf(cur);
                else q = queue.Take(Math.Max(idx, 0)).Count(i => keep.Contains(i)) - 1;
            }
            q += d;
            if (q >= nq.Count)
            {
                if (shuffleEnabled && nq.Count > 1)
                {
                    var last = cur;
                    nq = nq.OrderBy(x => rnd.Next()).ToList();
                    if (nq[0] == last && nq.Count > 1) { var tmp = nq[0]; nq[0] = nq[1]; nq[1] = tmp; }
                }
                q = 0;
            }
            if (q < 0) q = nq.Count - 1;
            ni = q; return true;
        }

        void Step(int d)
        {
            if (queue.Count == 0) { PlaySet("loop"); return; }
            List<Item> nq; int n;
            if (!Resolve(d, out nq, out n)) { StopWall(); return; }
            queue = nq; qi = n; Play(queue[qi]);
        }

        string PeekNext()
        {
            List<Item> nq; int n;
            if (mode == "single" || !Resolve(1, out nq, out n)) return null;
            return nq[n].Path;
        }

        // ==================== QUEUE (Spotify-style) ====================
        Popup queuePopup;
        StackPanel queuePopupList;
        TextBlock queuePopupTitle, queuePopupSub;
        Border queueNowCard;
        int queueDragFrom = -1;
        Point queueDragStart;

        static string ModeLabel(string m)
        {
            switch (m)
            {
                case "sel": return "Selected";
                case "queue": return "Queue";
                case "loop": return "Loop";
                case "all": return "All";
                case "single": return "Solo";
                case "multiloop": return "Multi-Loop";
                default: return m;
            }
        }

        /// Converts any automatic mode (loop / all / solo / multi-loop) into a manual queue so user edits stick.
        void EnsureManualQueue()
        {
            if (queue == null) queue = new List<Item>();
            if (mode == "queue" || mode == "sel") return;
            if (mode == "loop" || mode == "multiloop")
            {
                // freeze the current resolved order
                List<Item> nq; int n;
                if (cur != null && Resolve(0, out nq, out n)) { queue = new List<Item>(nq); qi = Math.Max(0, queue.IndexOf(cur)); }
            }
            mode = "queue";
            if (cur != null && !queue.Contains(cur)) { queue.Insert(0, cur); qi = 0; }
            else if (cur != null) qi = queue.IndexOf(cur);
        }

        void AfterQueueChanged()
        {
            if (cur != null) qi = queue.IndexOf(cur);
            SyncQueueFlags();
            if (host != null) host.Preload(PeekNext());
            UpdateMultiLoopUi();
        }

        void AddToQueue(List<Item> list, bool playNext)
        {
            if (list == null || list.Count == 0) { MessageBox.Show("Select a wallpaper card first."); return; }
            if (host == null || cur == null)
            {
                mode = "queue";
                queue = new List<Item>(list);
                qi = -1; cur = null; fails = 0;
                Step(1);
                return;
            }
            EnsureManualQueue();
            int insertAt = playNext ? Math.Min(queue.Count, qi + 1) : queue.Count;
            foreach (var it in list)
            {
                if (it == cur) continue;
                queue.Insert(insertAt, it);
                insertAt++;
            }
            AfterQueueChanged();
            ShowToast(playNext ? (list.Count == 1 ? "Will play next" : list.Count + " will play next") : (list.Count == 1 ? "Added to queue" : list.Count + " added to queue"));
        }

        void MoveQueueItem(int from, int to)
        {
            if (queue == null || from < 0 || from >= queue.Count) return;
            to = Math.Max(0, Math.Min(queue.Count - 1, to));
            if (from == to) return;
            EnsureManualQueue();
            var it = queue[from];
            queue.RemoveAt(from);
            queue.Insert(to, it);
            AfterQueueChanged();
        }

        void RemoveQueueItem(int idx)
        {
            if (queue == null || idx < 0 || idx >= queue.Count) return;
            EnsureManualQueue();
            var it = queue[idx];
            if (it == cur)
            {
                queue.RemoveAt(idx);
                if (queue.Count == 0) { StopWall(); RefreshQueuePopupContent(); return; }
                qi = Math.Min(idx, queue.Count - 1);
                Play(queue[qi]);
                return;
            }
            queue.RemoveAt(idx);
            AfterQueueChanged();
        }

        void ClearUpcoming()
        {
            if (queue == null || cur == null) return;
            EnsureManualQueue();
            queue = new List<Item> { cur };
            qi = 0;
            AfterQueueChanged();
        }

        Popup toastPopup;
        DispatcherTimer toastTimer;
        void ShowToast(string text)
        {
            if (toastPopup == null)
            {
                toastPopup = new Popup { AllowsTransparency = true, Placement = PlacementMode.Center, PlacementTarget = this, StaysOpen = true, IsHitTestVisible = false };
                toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                toastTimer.Tick += (s, e) => { toastTimer.Stop(); toastPopup.IsOpen = false; };
            }
            toastPopup.Child = new Border
            {
                Background = Theme.Accent, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 9, 16, 9),
                Child = new TextBlock { Text = text, Foreground = Theme.AccentFg, FontSize = 12.5, FontWeight = FontWeights.SemiBold }
            };
            toastPopup.VerticalOffset = Math.Max(0, ActualHeight / 2 - 120);
            toastPopup.IsOpen = false;
            toastPopup.IsOpen = true;
            toastTimer.Stop(); toastTimer.Start();
        }

        Button QueueIconBtn(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 11 },
                ToolTip = tip, Width = 24, Height = 24, Margin = new Thickness(2, 0, 0, 0),
                Style = MakeBtnStyle(Brushes.Transparent, Theme.BgCardHover, Theme.B(44, 44, 50), Theme.TextSecondary, Brushes.Transparent, 5, 0, 0)
            };
            b.Click += (s, e) => { e.Handled = true; onClick(); };
            return b;
        }

        void ToggleQueuePopup(UIElement anchor)
        {
            if (queuePopup != null && queuePopup.IsOpen) { queuePopup.IsOpen = false; return; }
            if (queuePopup == null)
            {
                queuePopup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Top, VerticalOffset = -10, PopupAnimation = PopupAnimation.Fade };
                var shell = new Border
                {
                    Width = 400,
                    Background = Theme.BgSidebar,
                    BorderBrush = Theme.B(52, 52, 60),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 10, 12)
                };
                var rootSp = new StackPanel();

                var hd = new DockPanel { Margin = new Thickness(2, 0, 4, 10) };
                var btnClear = StyledBtn("Clear upcoming", ClearUpcoming, false, false, 10, 4, 6);
                btnClear.FontSize = 11; btnClear.Margin = new Thickness(0);
                DockPanel.SetDock(btnClear, Dock.Right);
                hd.Children.Add(btnClear);
                var hdTxt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                queuePopupTitle = new TextBlock { Text = "Queue", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Theme.TextPrimary };
                queuePopupSub = new TextBlock { FontSize = 11, Foreground = Theme.TextMuted, Margin = new Thickness(0, 1, 0, 0) };
                hdTxt.Children.Add(queuePopupTitle); hdTxt.Children.Add(queuePopupSub);
                hd.Children.Add(hdTxt);
                rootSp.Children.Add(hd);

                rootSp.Children.Add(new TextBlock { Text = "NOW PLAYING", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(2, 0, 0, 6) });
                queueNowCard = new Border { Margin = new Thickness(0, 0, 4, 12) };
                rootSp.Children.Add(queueNowCard);

                rootSp.Children.Add(new TextBlock { Text = "NEXT UP  ·  drag to reorder", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Theme.TextMuted, Margin = new Thickness(2, 0, 0, 6) });
                var sv = new ScrollViewer { MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
                queuePopupList = new StackPanel { AllowDrop = true };
                sv.Content = queuePopupList;
                rootSp.Children.Add(sv);

                shell.Child = rootSp;
                queuePopup.Child = shell;
            }
            queuePopup.PlacementTarget = anchor;
            RefreshQueuePopupContent();
            queuePopup.IsOpen = true;
        }

        Border MakeQueueRow(Item item, int idx, bool isCur, int displayNo)
        {
            var row = new Border
            {
                Background = isCur ? Theme.B(30, 30, 35) : Brushes.Transparent,
                BorderBrush = isCur ? Theme.B(70, 70, 80) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 3),
                Cursor = Cursors.Hand,
                Tag = idx
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var no = new TextBlock
            {
                Text = isCur ? "\uE768" : displayNo.ToString(),
                FontFamily = isCur ? Theme.IconFont : new FontFamily("Segoe UI"),
                FontSize = isCur ? 11 : 11.5,
                Foreground = isCur ? Theme.Accent : Theme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(no, 0); g.Children.Add(no);

            var th = new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = Theme.BgInput, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            th.Child = new Image { Source = item.Thumb ?? ThumbCache.Get(item.Path), Stretch = Stretch.UniformToFill };
            Grid.SetColumn(th, 1); g.Children.Add(th);

            var ownerPl = playlists.FirstOrDefault(p => p.Items.Contains(item));
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = item.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = isCur ? Theme.Accent : Theme.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = (ownerPl != null ? ownerPl.Name : "Collection") + "  ·  " + item.Length, FontSize = 10.5, Foreground = Theme.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(txt, 2); g.Children.Add(txt);

            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.0 };
            if (!isCur)
            {
                acts.Children.Add(QueueIconBtn("\uE70E", "Move up", () => MoveQueueItem(idx, idx - 1)));
                acts.Children.Add(QueueIconBtn("\uE70D", "Move down", () => MoveQueueItem(idx, idx + 1)));
            }
            acts.Children.Add(QueueIconBtn("\uE711", "Remove from queue", () => RemoveQueueItem(idx)));
            Grid.SetColumn(acts, 3); g.Children.Add(acts);

            if (!isCur)
            {
                var grip = new TextBlock { Text = "\uE76F", FontFamily = Theme.IconFont, FontSize = 12, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), Cursor = Cursors.SizeAll, ToolTip = "Drag to reorder" };
                Grid.SetColumn(grip, 4); g.Children.Add(grip);
            }

            row.Child = g;
            row.MouseEnter += (s, e) => { acts.Opacity = 1.0; if (!isCur) row.Background = Theme.BgCardHover; };
            row.MouseLeave += (s, e) => { acts.Opacity = 0.0; if (!isCur) row.Background = Brushes.Transparent; };

            if (!isCur)
            {
                row.AllowDrop = true;
                row.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) { queueDragFrom = -1; return; }
                    queueDragFrom = idx; queueDragStart = e.GetPosition(null);
                };
                row.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || queueDragFrom != idx) return;
                    var p = e.GetPosition(null);
                    if (Math.Abs(p.X - queueDragStart.X) < 5 && Math.Abs(p.Y - queueDragStart.Y) < 5) return;
                    row.Opacity = 0.45;
                    try { DragDrop.DoDragDrop(row, new DataObject("LiveDeskQueueIdx", idx), DragDropEffects.Move); }
                    finally { row.Opacity = 1.0; queueDragFrom = -1; }
                };
                row.MouseLeftButtonUp += (s, e) =>
                {
                    if (queueDragFrom == idx && !IsInsideInteractiveControl(e.OriginalSource as DependencyObject))
                    {
                        queueDragFrom = -1;
                        if (idx >= 0 && idx < queue.Count) { qi = idx; Play(queue[qi]); }
                    }
                };
                row.DragOver += (s, e) =>
                {
                    if (!e.Data.GetDataPresent("LiveDeskQueueIdx")) return;
                    e.Effects = DragDropEffects.Move;
                    row.BorderBrush = Theme.Accent;
                    e.Handled = true;
                };
                row.DragLeave += (s, e) => { row.BorderBrush = Brushes.Transparent; };
                row.Drop += (s, e) =>
                {
                    row.BorderBrush = Brushes.Transparent;
                    if (!e.Data.GetDataPresent("LiveDeskQueueIdx")) return;
                    int from = (int)e.Data.GetData("LiveDeskQueueIdx");
                    e.Handled = true;
                    Dispatcher.BeginInvoke(new Action(() => MoveQueueItem(from, idx)));
                };
            }
            return row;
        }

        void RefreshQueuePopupContent()
        {
            if (queuePopupList == null || queuePopupTitle == null) return;
            queuePopupList.Children.Clear();
            queueNowCard.Child = null;

            int cnt = queue != null ? queue.Count : 0;
            queuePopupTitle.Text = "Queue";
            queuePopupSub.Text = cur == null ? "Nothing playing" : (ModeLabel(mode) + "  ·  " + cnt + (cnt == 1 ? " wallpaper" : " wallpapers"));

            if (cur == null || cnt == 0)
            {
                queueNowCard.Child = new TextBlock { Text = "Nothing playing. Right-click a card → Add to Queue.", Foreground = Theme.TextSecondary, FontSize = 12, Margin = new Thickness(4, 4, 4, 4), TextWrapping = TextWrapping.Wrap };
                return;
            }

            int curIdx = queue.IndexOf(cur);
            queueNowCard.Child = MakeQueueRow(cur, curIdx, true, 0);

            int shown = 0;
            for (int k = 1; k < cnt; k++)
            {
                int idx = (curIdx + k) % cnt;
                if (idx < 0) continue;
                shown++;
                queuePopupList.Children.Add(MakeQueueRow(queue[idx], idx, false, shown));
            }
            if (shown == 0)
            {
                queuePopupList.Children.Add(new TextBlock { Text = "Nothing up next. Right-click any card → Add to Queue.", Foreground = Theme.TextMuted, FontSize = 11.5, Margin = new Thickness(4, 2, 4, 6), TextWrapping = TextWrapping.Wrap });
            }
        }

        void SyncQueueFlags()
        {
            bool markQueued = ((mode == "sel" || mode == "queue") && queue != null && queue.Count > 0);
            var qset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (markQueued) foreach (var q in queue) if (q != null && q.Path != null) qset.Add(q.Path);
            string curPath = cur != null ? cur.Path : null;
            Action<Item> apply = x =>
            {
                x.IsPlaying = curPath != null && string.Equals(x.Path, curPath, StringComparison.OrdinalIgnoreCase);
                x.IsQueued = qset.Contains(x.Path);
            };
            foreach (var x in collectionPl.Items) apply(x);
            foreach (var pl in playlists)
                foreach (var x in pl.Items) apply(x);
            if (queuePopup != null && queuePopup.IsOpen)
                RefreshQueuePopupContent();
        }

        void Play(Item it)
        {
            int reqId = ++playReqId;
            ytResolving = false;
            cur = it;
            SyncQueueFlags();
            paused = false;
            timerRemainingSec = GetTimerTotalSeconds();
            lastTimerTick = DateTime.UtcNow;
            SetIconBtnGlyph(btnPlayPause, "\uE769");
            InspectItem(it);
            RefreshCycleModeUi();

            if (host == null)
            {
                host = new WallHost
                {
                    MainWinHandle = new WindowInteropHelper(this).Handle,
                    DeskHudHandle = deskHudHwnd,
                    RuleOtherApp = ruleOtherApp,
                    RuleCovered = ruleCovered
                };
                host.Ended = () =>
                {
                    if (mode == "single" || cycleByTimer || (cur != null && cur.IsPhotoOrGif))
                        host.Restart();
                    else
                        Step(1);
                };
                host.Failed = () => { fails++; if (fails >= queue.Count) { StopWall(); MessageBox.Show("Could not play the videos."); } else Step(1); };
                host.Opened = () => { fails = 0; if (cur != null && cur.Dur.TotalSeconds <= 0 && host.Dur > 0) { cur.Dur = TimeSpan.FromSeconds(host.Dur); UpdateHeaderCounts(); } };
            }
            else if (host.MainWinHandle == IntPtr.Zero)
            {
                host.MainWinHandle = new WindowInteropHelper(this).Handle;
            }
            host.DeskHudHandle = deskHudHwnd;
            host.RuleOtherApp = ruleOtherApp;
            host.RuleCovered = ruleCovered;
            host.WatchNow();
            ApplyAudio(); ApplyFit();

            if (!it.IsYouTube)
            {
                host.PlayMedia(it.Path, null);
                host.Preload(PeekNext());
            }
            else
            {
                ytResolving = true;
                txtNowTitle.Text = it.Name;
                txtNowSub.Text = "Loading stream...";
                imgNowThumb.Source = it.Thumb;

                string vid = it.YtVideoId;
                int maxH = it.YtMaxHeight;
                Task.Run(async () =>
                {
                    YouTubeHelper.StreamUrls urls = null;
                    try { urls = await YouTubeHelper.ResolveStreamUrlsAsync(vid, maxH, CancellationToken.None); }
                    catch { }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (reqId != playReqId || cur != it || host == null) return;
                        ytResolving = false;
                        if (urls == null || string.IsNullOrEmpty(urls.VideoUrl))
                        {
                            fails++;
                            if (fails >= queue.Count) { StopWall(); MessageBox.Show("Could not stream YouTube video."); }
                            else Step(1);
                            return;
                        }
                        host.PlayMedia(urls.VideoUrl, urls.AudioUrl);
                    }));
                });
            }
        }

        void PauseToggle()
        {
            if (host == null) { PlaySet("loop"); return; }
            paused = !paused;
            lastTimerTick = DateTime.UtcNow;
            if (paused) { host.Pause(); SetIconBtnGlyph(btnPlayPause, "\uE768"); }
            else { host.Resume(); SetIconBtnGlyph(btnPlayPause, "\uE769"); }
        }

        void StopWall()
        {
            playReqId++;
            ytResolving = false;
            if (host != null) { host.Close(); host = null; }
            cur = null; paused = false;
            queue.Clear();
            SyncQueueFlags();
            SetIconBtnGlyph(btnPlayPause, "\uE768");
            txtNowTitle.Text = "No Wallpaper Active";
            txtNowSub.Text = "Click 'Play Loop' or double-click a card";
            imgNowThumb.Source = null;
            txtTimeCur.Text = "0:00"; txtTimeTot.Text = "0:00"; seekSlider.Value = 0; seekSlider.IsEnabled = true;
            UpdateDesktopHud();
            UpdateMultiLoopUi();
            RefreshCycleModeUi();
        }

        void ApplyAudio() { if (host == null || vol == null) return; host.SetAudio(muteEnabled, (int)vol.Value); }
        void ApplyFit() { if (host == null) return; host.SetFit(fitMode); }

        void LoopSidePreviewIfNeeded()
        {
            if (previewMp == IntPtr.Zero || previewHost == null || previewHost.Visibility != Visibility.Visible || previewUserPaused) return;
            if (!IsVisible || WindowState == WindowState.Minimized || viewLibrary == null || viewLibrary.Visibility != Visibility.Visible) return;
            int pst = Native.libvlc_media_player_get_state(previewMp);
            if (pst == 6)
            {
                Native.libvlc_media_player_stop(previewMp);
                Native.libvlc_media_player_play(previewMp);
            }
            else if (pst == 4)
            {
                Native.libvlc_media_player_set_pause(previewMp, 0);
            }
            else if (pst == 3)
            {
                long pt = Native.libvlc_media_player_get_time(previewMp);
                long plen = Native.libvlc_media_player_get_length(previewMp);
                if (plen > 600 && pt >= plen - 350)
                    Native.libvlc_media_player_set_time(previewMp, 0);
            }
        }

        void UpdateNow()
        {
            var now = DateTime.UtcNow;
            double dt = Math.Max(0, Math.Min(2.0, (now - lastTimerTick).TotalSeconds));
            lastTimerTick = now;

            TaskbarHelper.Tick();
            LoopSidePreviewIfNeeded();
            UpdateDesktopHud();

            if (host == null || cur == null || ytResolving) return;

            if (cycleByTimer && mode != "single" && !paused && !host.IsEcoPaused)
            {
                timerRemainingSec -= dt;
                if (timerRemainingSec <= 0)
                {
                    timerRemainingSec = GetTimerTotalSeconds();
                    Step(1);
                    return;
                }
            }

            double pos = host.Pos, dur = host.Dur;
            if (dur <= 0 && cur.Dur.TotalSeconds > 0) dur = cur.Dur.TotalSeconds;

            txtNowTitle.Text = cur.Name;
            var ownerPl = playlists.FirstOrDefault(p => p.Items.Contains(cur));
            string plName = ownerPl != null ? ownerPl.Name : (collectionPl.Items.Contains(cur) ? "Collection" : (activePl != null ? activePl.Name : "Collection"));
            string cycleInfo = "";
            if (mode != "single")
            {
                if (cycleByTimer)
                {
                    var rem = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(timerRemainingSec)));
                    cycleInfo = "  ·  Next in " + ((int)rem.TotalHours).ToString("00") + ":" + rem.Minutes.ToString("00") + ":" + rem.Seconds.ToString("00");
                }
                else
                {
                    cycleInfo = cur.IsPhotoOrGif ? "  ·  Indefinite" : "  ·  On Video End";
                }
            }
            string modeTxt = ModeLabel(mode);
            txtNowSub.Text = plName + "  ·  " + modeTxt + " " + (qi + 1) + " / " + queue.Count + cycleInfo;
            imgNowThumb.Source = cur.Thumb;

            if (cur.IsStaticPhoto)
            {
                txtTimeCur.Text = "PHOTO";
                txtTimeTot.Text = cycleByTimer ? "TIMER" : "∞";
                seekSlider.IsEnabled = false;
                seekSlider.Maximum = 1;
                seekSlider.Value = 1;
            }
            else
            {
                seekSlider.IsEnabled = true;
                txtTimeCur.Text = TimeSpan.FromSeconds(pos).ToString(@"m\:ss");
                txtTimeTot.Text = TimeSpan.FromSeconds(dur).ToString(@"m\:ss");
                if (!isDraggingSeek && dur > 0)
                {
                    seekSlider.Maximum = dur;
                    seekSlider.Value = Math.Min(dur, pos);
                }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] a)
        {
            bool created;
            using (var m = new Mutex(true, "LiveDeskSingle", out created))
            {
                if (!created)
                {
                    try
                    {
                        using (var evSignal = EventWaitHandle.OpenExisting("LiveDeskWakeupEvent"))
                            evSignal.Set();
                    }
                    catch { }
                    return;
                }
                using (var evWait = new EventWaitHandle(false, EventResetMode.AutoReset, "LiveDeskWakeupEvent"))
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var w = new MainWin(a.Contains("--tray"));
                    var t = new Thread(() =>
                    {
                        while (evWait.WaitOne())
                        {
                            try { w.Dispatcher.BeginInvoke(new Action(() => w.OpenFromWakeup())); } catch { }
                        }
                    }) { IsBackground = true };
                    t.Start();
                    if (!a.Contains("--tray")) w.Show();
                    app.Run();
                }
            }
        }
    }
}
