using System.IO;
using System.Windows.Media.Imaging;

namespace SnipText
{
    public static class ImageFile
    {
        public static BitmapSource Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }

        public static void SavePng(BitmapSource image, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }
    }
}
