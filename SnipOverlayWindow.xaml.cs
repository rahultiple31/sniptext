using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SnipText
{
    public partial class SnipOverlayWindow : Window
    {
        private readonly Drawing.Rectangle virtualScreen;
        private readonly BitmapSource screenCapture;
        private Point startPoint;
        private bool isSelecting;

        public BitmapSource CroppedImage { get; private set; }

        public SnipOverlayWindow()
        {
            InitializeComponent();

            virtualScreen = Forms.SystemInformation.VirtualScreen;
            Left = virtualScreen.Left;
            Top = virtualScreen.Top;
            Width = virtualScreen.Width;
            Height = virtualScreen.Height;

            screenCapture = CaptureVirtualScreen(virtualScreen);
            ScreenImage.Source = screenCapture;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            startPoint = e.GetPosition(OverlayCanvas);
            isSelecting = true;
            SelectionRectangle.Visibility = Visibility.Visible;
            CaptureMouse();
            UpdateSelection(startPoint, startPoint);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelecting)
            {
                return;
            }

            UpdateSelection(startPoint, e.GetPosition(OverlayCanvas));
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSelecting)
            {
                return;
            }

            isSelecting = false;
            ReleaseMouseCapture();

            var rect = Normalize(startPoint, e.GetPosition(OverlayCanvas));
            if (rect.Width < 4 || rect.Height < 4)
            {
                DialogResult = false;
                Close();
                return;
            }

            CroppedImage = CropSelection(rect);
            DialogResult = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void UpdateSelection(Point start, Point end)
        {
            var rect = Normalize(start, end);
            Canvas.SetLeft(SelectionRectangle, rect.X);
            Canvas.SetTop(SelectionRectangle, rect.Y);
            SelectionRectangle.Width = rect.Width;
            SelectionRectangle.Height = rect.Height;
        }

        private static Int32Rect Normalize(Point start, Point end)
        {
            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var width = Math.Abs(start.X - end.X);
            var height = Math.Abs(start.Y - end.Y);
            return new Int32Rect((int)x, (int)y, (int)width, (int)height);
        }

        private BitmapSource CropSelection(Int32Rect displayRect)
        {
            var scaleX = screenCapture.PixelWidth / ActualWidth;
            var scaleY = screenCapture.PixelHeight / ActualHeight;

            var pixelRect = new Int32Rect(
                Clamp((int)Math.Round(displayRect.X * scaleX), 0, screenCapture.PixelWidth - 1),
                Clamp((int)Math.Round(displayRect.Y * scaleY), 0, screenCapture.PixelHeight - 1),
                Clamp((int)Math.Round(displayRect.Width * scaleX), 1, screenCapture.PixelWidth),
                Clamp((int)Math.Round(displayRect.Height * scaleY), 1, screenCapture.PixelHeight));

            if (pixelRect.X + pixelRect.Width > screenCapture.PixelWidth)
            {
                pixelRect.Width = screenCapture.PixelWidth - pixelRect.X;
            }

            if (pixelRect.Y + pixelRect.Height > screenCapture.PixelHeight)
            {
                pixelRect.Height = screenCapture.PixelHeight - pixelRect.Y;
            }

            var cropped = new CroppedBitmap(screenCapture, pixelRect);
            cropped.Freeze();
            return cropped;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static BitmapSource CaptureVirtualScreen(Drawing.Rectangle bounds)
        {
            using (var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                }

                var handle = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(handle);
                }
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
