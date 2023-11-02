using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Foundation;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Hosting;

namespace FileTest;

public class ImageOpenedEventsArgs : EventArgs
{
    public Visual ImageVisual { get; }
    public bool Handled { get; set; }

    public ImageOpenedEventsArgs(Visual visual)
    {
        ImageVisual = visual;
    }
}

public class StorageFileImage : Control
{
    // Should actually be per-dipatcher too
    private static Dictionary<string, BitmapImage> _iconCache { get; } = new();

    public event EventHandler<ImageOpenedEventsArgs> ImageOpened;

    public Image PART_Image { get; private set; }

    public bool AllowIcons
    {
        get { return (bool)GetValue(AllowIconsProperty); }
        set { SetValue(AllowIconsProperty, value); }
    }

    public static readonly DependencyProperty AllowIconsProperty =
        DependencyProperty.Register(nameof(AllowIcons), typeof(bool), typeof(StorageFileImage), new PropertyMetadata(false));


    public Stretch Stretch
    {
        get { return (Stretch)GetValue(StretchProperty); }
        set { SetValue(StretchProperty, value); }
    }

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(StorageFileImage), new PropertyMetadata(Stretch.Uniform));

    public object StorageFile
    {
        get { return (object)GetValue(StorageFileProperty); }
        set { SetValue(StorageFileProperty, value); }
    }

    public static readonly DependencyProperty StorageFileProperty =
        DependencyProperty.Register(nameof(StorageFile), typeof(object), typeof(StorageFileImage), new PropertyMetadata(null, (d, e) =>
        {
            _ = ((StorageFileImage)d).SetImageSourceAsync(e.NewValue);
        }));

    public StorageFileImage()
    {
        DefaultStyleKey = typeof(StorageFileImage);
    }

    protected override async void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (DesignMode.DesignModeEnabled)
            return;

        PART_Image = GetTemplateChild(nameof(PART_Image)) as Image;
        PART_Image.Opacity = 1;

        await SetImageSourceAsync(StorageFile);
    }

    bool _isRetryThumb = false;

    protected virtual async Task SetImageSourceAsync(object file)
    {
        _sourceFile = null;

        StorageFile s = file as StorageFile;

        if (s is null && file is FileInfo fInfo)
            s = await fInfo.GetFileAsync();

        if (file != StorageFile)
            return;

        _sourceFile = s;
        await SetImageSourceAsync(s);
    }

    bool IsSourceFile(StorageFile file) => file == _sourceFile;

    private StorageFile _sourceFile = null;
    protected virtual async Task SetImageSourceAsync(StorageFile file)
    {
        bool retryThumb = _isRetryThumb;
        _isRetryThumb = false;

        if (PART_Image == null)
            return;

        // 1. Hide the current visual
        Visual v = ElementCompositionPreview.GetElementVisual(PART_Image);
        v.Opacity = 0;
        PART_Image.Source = null;

        if (file == null)
            return;

        // 2. Attempt to load an image stream either from file or windows thumbnail
        IRandomAccessStream stream;
        if (!file.Attributes.HasFlag(FileAttributes.Archive)
            && file.ContentType.StartsWith("image"))
        {
            // 2.1. Path for image files. Load the actual file.
            stream = await file.OpenReadAsync();
        }
        else
        {
            // 2.2. Path for other files. Request a thumbnail from windows.
            stream = await file.GetThumbnailAsync(ThumbnailMode.SingleItem);

            // If we're using a thumbnail we don't want to ever show it as an "icon" if windows
            // gives that to us, so we should try to re-request it. 
            // Only retry if this isn't already a retry attempt, otherwise just show the icon.

            // TODO : perhaps retry multiple times?
            if (stream is StorageItemThumbnail thumb
                && thumb.Type == ThumbnailType.Icon
                && !retryThumb
                && !AllowIcons)
            {
                stream.Dispose();
                await Task.Delay(250);
                if (StorageFile == file)
                {
                    _isRetryThumb = true;
                    await SetImageSourceAsync(file);
                }

                return;
            }

        }

        // 3. Check source is still valid after async calls
        if (!IsSourceFile(file) || stream == null)
            return;

        // 4. Create our new display bitmap image at the desired render size
        BitmapImage bitmapImage;
        bool fromCache = false;
        if (AllowIcons
              && stream is StorageItemThumbnail t
              && t.Type == ThumbnailType.Icon
              && _iconCache.TryGetValue(file.ContentType, out BitmapImage img))
        {
            fromCache = true;
            bitmapImage = img;
        }
        else
        {
            bitmapImage = new() { DecodePixelType = DecodePixelType.Logical };
            if (AllowIcons && stream is StorageItemThumbnail t2 && t2.Type == ThumbnailType.Icon)
            {
                _iconCache[file.ContentType] = bitmapImage;
            }
        }

        // 5. Set the source on the new bitmap image to our image stream
        PART_Image.Source = bitmapImage;
        if (!fromCache)
        {
            stream.Seek(0);
            await bitmapImage.SetSourceAsync(stream);
            stream.Dispose();
        }

        // 6. Check source is still valid after the above async call, and only fire
        //    "Opened" event if it is
        if (IsSourceFile(file) && PART_Image != null && PART_Image.Source == bitmapImage)
        {
            ImageOpenedEventsArgs args = new (v);
            ImageOpened?.Invoke(this, args);
            if (args.Handled == false)
                v.Opacity = 1;
        }
    }
}