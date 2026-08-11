using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace SnipText
{
    public static class OcrImagePreprocessor
    {
        private const int DarkThreshold = 175;

        public static IReadOnlyList<BitmapSource> CreateRowImages(BitmapSource image)
        {
            using (var bitmap = ToBitmap(image))
            {
                var rowBounds = DetectRows(bitmap);
                if (rowBounds.Count == 0)
                {
                    return new[] { PrepareBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)) };
                }

                var rows = new List<BitmapSource>();
                foreach (var rowBoundsItem in rowBounds)
                {
                    rows.Add(PrepareBitmap(bitmap, rowBoundsItem));
                }

                return rows;
            }
        }

        private static List<Rectangle> DetectRows(Bitmap bitmap)
        {
            var rows = new List<Rectangle>();
            var minInkPerRow = Math.Max(3, bitmap.Width / 250);
            var gapTolerance = Math.Max(1, bitmap.Height / 160);

            var inBand = false;
            var bandStart = 0;
            var lastInkRow = 0;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var ink = CountDarkPixelsInRow(bitmap, y);
                if (ink >= minInkPerRow)
                {
                    if (!inBand)
                    {
                        inBand = true;
                        bandStart = y;
                    }

                    lastInkRow = y;
                }
                else if (inBand && y - lastInkRow > gapTolerance)
                {
                    AddRowIfTextLike(rows, bitmap, bandStart, lastInkRow);
                    inBand = false;
                }
            }

            if (inBand)
            {
                AddRowIfTextLike(rows, bitmap, bandStart, lastInkRow);
            }

            return rows;
        }

        private static void AddRowIfTextLike(List<Rectangle> rows, Bitmap bitmap, int top, int bottom)
        {
            var height = bottom - top + 1;
            if (height <= 2)
            {
                return;
            }

            var bounds = FindInkBounds(bitmap, top, bottom);
            if (bounds.Width < 8 || bounds.Height < 4)
            {
                return;
            }

            if (bounds.Width > bitmap.Width * 0.88 && bounds.Height <= 4)
            {
                return;
            }

            rows.Add(Pad(bounds, bitmap.Width, bitmap.Height, 18, 3));
        }

        private static Rectangle FindInkBounds(Bitmap bitmap, int top, int bottom)
        {
            var left = bitmap.Width;
            var right = -1;
            var actualTop = bitmap.Height;
            var actualBottom = -1;

            for (var y = top; y <= bottom; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (!IsDark(bitmap.GetPixel(x, y)))
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    actualTop = Math.Min(actualTop, y);
                    actualBottom = Math.Max(actualBottom, y);
                }
            }

            if (right < left || actualBottom < actualTop)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(left, actualTop, right + 1, actualBottom + 1);
        }

        private static int CountDarkPixelsInRow(Bitmap bitmap, int y)
        {
            var count = 0;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsDark(bitmap.GetPixel(x, y)))
                {
                    count++;
                }
            }

            return count;
        }

        private static BitmapSource PrepareBitmap(Bitmap source, Rectangle bounds)
        {
            var crop = Rectangle.Intersect(bounds, new Rectangle(0, 0, source.Width, source.Height));
            var scale = GetScale(crop);
            using (var prepared = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(prepared))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.DrawImage(source, new Rectangle(0, 0, prepared.Width, prepared.Height), crop, GraphicsUnit.Pixel);
                }

                return ToGrayscaleBitmapSource(prepared);
            }
        }

        private static int GetScale(Rectangle bounds)
        {
            var targetHeight = 120;
            var scale = Math.Max(2, (int)Math.Round((double)targetHeight / Math.Max(1, bounds.Height)));

            while ((bounds.Width * scale > 2400 || bounds.Height * scale > 2400) && scale > 2)
            {
                scale--;
            }

            return Math.Min(8, scale);
        }

        private static Rectangle Pad(Rectangle rectangle, int maxWidth, int maxHeight, int horizontal, int vertical)
        {
            var left = Math.Max(0, rectangle.Left - horizontal);
            var top = Math.Max(0, rectangle.Top - vertical);
            var right = Math.Min(maxWidth, rectangle.Right + horizontal);
            var bottom = Math.Min(maxHeight, rectangle.Bottom + vertical);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static bool IsDark(Color color)
        {
            var luminance = (color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114);
            return color.A > 32 && luminance < DarkThreshold;
        }

        private static Bitmap ToBitmap(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;

                using (var bitmap = new Bitmap(stream))
                {
                    return new Bitmap(bitmap);
                }
            }
        }

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }

        private static BitmapSource ToGrayscaleBitmapSource(Bitmap bitmap)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    var luminance = (int)((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114));
                    var adjusted = luminance < 210 ? Math.Max(0, luminance - 35) : 255;
                    bitmap.SetPixel(x, y, Color.FromArgb(color.A, adjusted, adjusted, adjusted));
                }
            }

            return ToBitmapSource(bitmap);
        }
    }
}
