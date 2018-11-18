using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace QRCodeHelper
{
    public sealed partial class MainPage : Page
    {
        // conveniently convert to uint and int
        private const ushort DEFAULT_SIZE = 320;

        // delay QRCode Rendering
        private DispatcherTimer updateQRCodeImageTimer;

        private static readonly BarcodeWriter qrWriter = new BarcodeWriter()
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Height = DEFAULT_SIZE,
                Width = DEFAULT_SIZE,
                CharacterSet = "utf-8"
            }
        };

        private static readonly BarcodeReader qrReader = new BarcodeReader()
        {
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                CharacterSet = "utf-8"
            }
        };

        private WriteableBitmap qrBitmap;

        public MainPage()
        {
            this.updateQRCodeImageTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            this.updateQRCodeImageTimer.Tick += UpdateQRCodeImageTimer_Tick;
            this.InitializeComponent();
            this.UpdateQRCodeImage();
        }

        private void UpdateQRCodeImage()
        {
            updateQRCodeImageTimer.Stop();
            if (!String.IsNullOrEmpty(QRTextBox.Text))
            {
                qrBitmap = qrWriter.Write(QRTextBox.Text);
                QRCodeImage.Stretch = Stretch.None;
                QRCodeImage.Source = qrBitmap;
            }
        }

        private void DelayedUpdateQRCodeImage()
        {
            updateQRCodeImageTimer.Start();
        }

        private void UpdateQRCodeImageTimer_Tick(object sender, object e)
        {
            UpdateQRCodeImage();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (updateQRCodeImageTimer.IsEnabled)
            {
                UpdateQRCodeImage();
            }

            FileSavePicker picker = new FileSavePicker()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"QRCode-Helper_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.png"
            };
            picker.FileTypeChoices.Add("PNG File", new List<string>() { ".png" });
            picker.FileTypeChoices.Add("JPG File", new List<string>() { ".jpg", ".jpeg" });
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile == null)
            {
                return;
            }
            using (IRandomAccessStream stream = await savefile.OpenAsync(FileAccessMode.ReadWrite))
            using (Stream pixelStream = qrBitmap.PixelBuffer.AsStream())
            {
                Guid encoderId = BitmapEncoder.PngEncoderId;
                switch (savefile.FileType)
                {
                    case ".png":
                        // encoderId = BitmapEncoder.PngEncoderId;
                        break;
                    case ".jpg":
                    case ".jpeg":
                        encoderId = BitmapEncoder.JpegEncoderId;
                        break;
                }

                var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);

                byte[] pixels = new byte[pixelStream.Length];
                await pixelStream.ReadAsync(pixels, 0, pixels.Length);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    (uint)qrBitmap.PixelWidth,
                    (uint)qrBitmap.PixelHeight,
                    96.0,
                    96.0,
                    pixels);
                await encoder.FlushAsync();
            }
        }

        private void QRTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            DelayedUpdateQRCodeImage();
        }

        private void SizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!ushort.TryParse(SizeTextBox.Text, out ushort size))
            {
                size = DEFAULT_SIZE;
            }
            qrWriter.Options.Width = size;
            qrWriter.Options.Height = size;
            DelayedUpdateQRCodeImage();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new FileOpenPicker()
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".txt");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadFile(file);
            }
        }

        private async void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataPackageView(Clipboard.GetContent());
        }

        private async Task LoadFile(IStorageFile file)
        {
            switch (file.FileType)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                    await LoadImageAndDecode(file);
                    break;
                case ".txt":
                    QRTextBox.Text = await FileIO.ReadTextAsync(file, UnicodeEncoding.Utf8);
                    UpdateQRCodeImage();
                    break;
            }
        }

        private async Task LoadImageAndDecode(IRandomAccessStreamReference streamRef)
        {
            using (var stream = await streamRef.OpenReadAsync())
            {
                try
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    qrBitmap = new WriteableBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight);
                }
                catch (Exception)
                {
                    var dialog = new MessageDialog("Invalid image");
                    await dialog.ShowAsync();
                    return;
                }

                stream.Seek(0);
                await qrBitmap.SetSourceAsync(stream);

                var result = qrReader.Decode(qrBitmap);

                QRCodeImage.Source = qrBitmap;
                QRCodeImage.Stretch = Stretch.Uniform;

                if (result != null)
                {
                    QRTextBox.TextChanged -= QRTextBox_TextChanged;
                    QRTextBox.Text = result.Text;
                    QRTextBox.TextChanged += QRTextBox_TextChanged;
                }
                else
                {
                    var dialog = new MessageDialog("No QRCode detected");
                    await dialog.ShowAsync();
                }
            }
        }

        private async Task LoadDataPackageView(DataPackageView view)
        {
            if (view.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await view.GetBitmapAsync();
                await LoadImageAndDecode(bitmap);
            }
            else if (view.Contains(StandardDataFormats.Text))
            {
                QRTextBox.Text = await view.GetTextAsync();
                UpdateQRCodeImage();
            }
            else if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync();
                if (items[0] is IStorageFile file)
                {
                    await LoadFile(file);
                }
            }
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            await LoadDataPackageView(e.DataView);
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }
    }
}
