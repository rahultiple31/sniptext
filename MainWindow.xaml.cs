using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SnipText
{
    public partial class MainWindow : Window
    {
        private BitmapSource currentImage;

        public MainWindow()
        {
            InitializeComponent();
            UpdateImageState();
        }

        private async void NewSnipButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Hide();
                await Task.Delay(180);

                var snipWindow = new SnipOverlayWindow();
                if (snipWindow.ShowDialog() == true && snipWindow.CroppedImage != null)
                {
                    SetImage(snipWindow.CroppedImage);

                    if (AutoReadCheckBox.IsChecked == true)
                    {
                        await RecognizeCurrentImageAsync();
                    }
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private async void OpenImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open image",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                SetImage(ImageFile.Load(dialog.FileName));

                if (AutoReadCheckBox.IsChecked == true)
                {
                    await RecognizeCurrentImageAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus("Could not open image: " + ex.Message);
            }
        }

        private async void ReadTextButton_Click(object sender, RoutedEventArgs e)
        {
            await RecognizeCurrentImageAsync();
        }

        private void CopyTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TextResultBox.Text))
            {
                SetStatus("There is no text to copy.");
                return;
            }

            Clipboard.SetText(TextResultBox.Text);
            SetStatus("Text copied to clipboard.");
        }

        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage == null)
            {
                SetStatus("Capture an image before saving.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save snip",
                Filter = "PNG image|*.png",
                FileName = "snip.png"
            };

            if (dialog.ShowDialog(this) == true)
            {
                ImageFile.SavePng(currentImage, dialog.FileName);
                SetStatus("Image saved.");
            }
        }

        private void SaveTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TextResultBox.Text))
            {
                SetStatus("There is no text to save.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save extracted text",
                Filter = "Text file|*.txt",
                FileName = "snip-text.txt"
            };

            if (dialog.ShowDialog(this) == true)
            {
                System.IO.File.WriteAllText(dialog.FileName, TextResultBox.Text);
                SetStatus("Text saved.");
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            currentImage = null;
            PreviewImage.Source = null;
            TextResultBox.Clear();
            UpdateImageState();
            SetStatus("Ready");
        }

        private async Task RecognizeCurrentImageAsync()
        {
            if (currentImage == null)
            {
                SetStatus("Capture or open an image first.");
                return;
            }

            SetBusy(true);
            SetStatus("Reading text from image...");

            try
            {
                var text = await WindowsOcrService.RecognizeAsync(currentImage);
                TextResultBox.Text = text;
                var rowCount = CountTextRows(text);
                SetStatus(rowCount == 0 ? "No text rows found." : $"{rowCount} text row{(rowCount == 1 ? "" : "s")} extracted.");
            }
            catch (Exception ex)
            {
                SetStatus("OCR failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetImage(BitmapSource image)
        {
            currentImage = image;
            PreviewImage.Source = image;
            TextResultBox.Clear();
            UpdateImageState();
            SetStatus($"Image ready: {image.PixelWidth} x {image.PixelHeight}px");
        }

        private void UpdateImageState()
        {
            EmptyImagePanel.Visibility = currentImage == null ? Visibility.Visible : Visibility.Collapsed;
            SaveImageButton.IsEnabled = currentImage != null;
            ReadTextButton.IsEnabled = currentImage != null;
        }

        private void SetBusy(bool busy)
        {
            NewSnipButton.IsEnabled = !busy;
            OpenImageButton.IsEnabled = !busy;
            ReadTextButton.IsEnabled = !busy && currentImage != null;
            SaveImageButton.IsEnabled = !busy && currentImage != null;
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private static int CountTextRows(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var count = 0;

            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
