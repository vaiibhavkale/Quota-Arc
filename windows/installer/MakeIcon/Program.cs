using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var sourcePath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "assets", "windows", "QuotaArc-logo.jpg"));
var destIco = args.Length > 1
    ? args[1]
    : Path.Combine(Path.GetDirectoryName(sourcePath)!, "QuotaArc.ico");

if (!File.Exists(sourcePath))
    throw new FileNotFoundException("Logo source not found.", sourcePath);

Directory.CreateDirectory(Path.GetDirectoryName(destIco)!);

using var original = new Bitmap(sourcePath);
using var prepared = KnockoutCanvas(original);
using var framed = CropToContent(prepared, padding: 4);

var sizes = new[] { 16, 20, 22, 24, 26, 32, 40, 48, 64, 128, 256 };
var frames = sizes.Select(size => Scale(framed, size, fill: 0.94)).ToList();
WriteIco(destIco, frames);

var destDir = Path.GetDirectoryName(destIco)!;
var png256 = Path.Combine(destDir, "QuotaArc-256.png");
frames.First(f => f.Width == 256).Save(png256, ImageFormat.Png);
WriteInstallerBanner(framed, Path.Combine(destDir, "WixUIBannerBmp.bmp"));
WriteInstallerDialog(framed, Path.Combine(destDir, "WixUIDialogBmp.bmp"));

var macIconDir = Path.GetFullPath(Path.Combine(destDir, "..", "..",
    "Sources", "Assets.xcassets", "AppIcon.appiconset"));
WriteMacAppIcons(framed, macIconDir);

foreach (var frame in frames) frame.Dispose();
Console.WriteLine(destIco);

static Bitmap KnockoutCanvas(Bitmap source)
{
    var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
        g.DrawImage(source, 0, 0, source.Width, source.Height);

    var data = bmp.LockBits(
        new Rectangle(0, 0, bmp.Width, bmp.Height),
        ImageLockMode.ReadWrite,
        PixelFormat.Format32bppArgb);
    var stride = data.Stride;
    var bytes = new byte[Math.Abs(stride) * bmp.Height];
    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

    int Idx(int x, int y) => y * stride + x * 4;
    bool IsCanvas(int x, int y)
    {
        var i = Idx(x, y);
        return bytes[i + 2] <= 18 && bytes[i + 1] <= 18 && bytes[i] <= 22;
    }

    var seen = new bool[bmp.Width * bmp.Height];
    var queue = new Queue<(int X, int Y)>();
    void Enqueue(int x, int y)
    {
        if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return;
        var key = y * bmp.Width + x;
        if (seen[key] || !IsCanvas(x, y)) return;
        seen[key] = true;
        queue.Enqueue((x, y));
    }

    for (var x = 0; x < bmp.Width; x++)
    {
        Enqueue(x, 0);
        Enqueue(x, bmp.Height - 1);
    }
    for (var y = 0; y < bmp.Height; y++)
    {
        Enqueue(0, y);
        Enqueue(bmp.Width - 1, y);
    }

    while (queue.Count > 0)
    {
        var (x, y) = queue.Dequeue();
        var i = Idx(x, y);
        bytes[i] = 0;
        bytes[i + 1] = 0;
        bytes[i + 2] = 0;
        bytes[i + 3] = 0;
        Enqueue(x + 1, y);
        Enqueue(x - 1, y);
        Enqueue(x, y + 1);
        Enqueue(x, y - 1);
    }

    Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
    bmp.UnlockBits(data);
    return bmp;
}

static Bitmap CropToContent(Bitmap source, int padding)
{
    var minX = source.Width;
    var minY = source.Height;
    var maxX = 0;
    var maxY = 0;
    var found = false;

    for (var y = 0; y < source.Height; y++)
    {
        for (var x = 0; x < source.Width; x++)
        {
            if (source.GetPixel(x, y).A <= 12) continue;
            found = true;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }

    if (!found)
        return new Bitmap(source);

    minX = Math.Max(0, minX - padding);
    minY = Math.Max(0, minY - padding);
    maxX = Math.Min(source.Width - 1, maxX + padding);
    maxY = Math.Min(source.Height - 1, maxY + padding);

    var width = maxX - minX + 1;
    var height = maxY - minY + 1;
    var crop = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(crop))
    {
        g.DrawImage(source, new Rectangle(0, 0, width, height),
            new Rectangle(minX, minY, width, height), GraphicsUnit.Pixel);
    }
    return crop;
}

static void WriteInstallerBanner(Bitmap mark, string path)
{
    const int width = 493;
    const int height = 58;
    using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);
    Qualify(g);
    g.Clear(Color.White);
    using (var line = new Pen(Color.FromArgb(224, 224, 224)))
        g.DrawLine(line, 0, height - 1, width, height - 1);

    var side = 48;
    using var icon = Scale(mark, side, fill: 1);
    g.DrawImage(icon, width - side - 8, (height - side) / 2, side, side);
    SaveBmp24(canvas, path);
}

static void WriteInstallerDialog(Bitmap mark, string path)
{
    const int width = 493;
    const int height = 312;
    const int strip = 164;
    using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(canvas);
    Qualify(g);
    g.Clear(Color.White);
    using (var brush = new SolidBrush(Color.FromArgb(10, 10, 12)))
        g.FillRectangle(brush, 0, 0, strip, height);

    var side = 120;
    using var icon = Scale(mark, side, fill: 1);
    g.DrawImage(icon, (strip - side) / 2, (height - side) / 2, side, side);
    SaveBmp24(canvas, path);
}

static void SaveBmp24(Bitmap source, string path)
{
    using var flat = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
    using var g = Graphics.FromImage(flat);
    g.CompositingMode = CompositingMode.SourceCopy;
    g.DrawImage(source, 0, 0, source.Width, source.Height);
    flat.Save(path, ImageFormat.Bmp);
}

static void Qualify(Graphics g)
{
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.CompositingMode = CompositingMode.SourceOver;
    g.CompositingQuality = CompositingQuality.HighQuality;
}

static void WriteMacAppIcons(Bitmap mark, string dir)
{
    if (!Directory.Exists(dir)) return;
    (string Name, int Size)[] files =
    [
        ("icon_16x16.png", 16),
        ("icon_16x16@2x.png", 32),
        ("icon_32x32.png", 32),
        ("icon_32x32@2x.png", 64),
        ("icon_128x128.png", 128),
        ("icon_128x128@2x.png", 256),
        ("icon_256x256.png", 256),
        ("icon_256x256@2x.png", 512),
        ("icon_512x512.png", 512),
        ("icon_512x512@2x.png", 1024),
    ];
    foreach (var (name, size) in files)
    {
        using var frame = Scale(mark, size, fill: 1);
        frame.Save(Path.Combine(dir, name), ImageFormat.Png);
    }
}

static Bitmap Scale(Bitmap source, int size, double fill)
{
    var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dest);
    g.Clear(Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.CompositingMode = CompositingMode.SourceOver;
    g.CompositingQuality = CompositingQuality.HighQuality;

    var draw = Math.Max(1, (int)Math.Round(size * fill));
    var offset = (size - draw) / 2;
    g.DrawImage(source, new Rectangle(offset, offset, draw, draw));
    return dest;
}

static void WriteIco(string path, List<Bitmap> frames)
{
    var blobs = new List<byte[]>();
    foreach (var frame in frames)
    {
        using var ms = new MemoryStream();
        frame.Save(ms, ImageFormat.Png);
        blobs.Add(ms.ToArray());
    }

    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)blobs.Count);
    var offset = 6 + 16 * blobs.Count;
    for (var i = 0; i < frames.Count; i++)
    {
        var w = frames[i].Width;
        var h = frames[i].Height;
        bw.Write((byte)(w >= 256 ? 0 : w));
        bw.Write((byte)(h >= 256 ? 0 : h));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(blobs[i].Length);
        bw.Write(offset);
        offset += blobs[i].Length;
    }
    foreach (var blob in blobs) bw.Write(blob);
}
