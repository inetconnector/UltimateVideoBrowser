using System;
using System.IO;

namespace UltimateVideoBrowser.Views.Controls;

public sealed class AdaptiveImage : ContentView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(ImageSource),
        typeof(AdaptiveImage),
        default(ImageSource),
        propertyChanged: OnSourceChanged);

    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect),
        typeof(Aspect),
        typeof(AdaptiveImage),
        Aspect.AspectFit,
        propertyChanged: OnAspectChanged);

    private readonly Image _bitmapImage;
    private readonly SvgImage _svgImage;

    public AdaptiveImage()
    {
        _bitmapImage = new Image
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit
        };

        _svgImage = new SvgImage
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit
        };

        var grid = new Grid();
        grid.Children.Add(_bitmapImage);
        grid.Children.Add(_svgImage);

        Content = grid;
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    private static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AdaptiveImage)bindable).UpdateSource();
    }

    private static void OnAspectChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AdaptiveImage)bindable).UpdateAspect();
    }

    private void UpdateSource()
    {
        var source = Source;
        var fileSource = source as FileImageSource;
        if (fileSource?.File is { Length: > 0 } fileName && IsSvgFile(fileName))
        {
            _svgImage.Source = fileName;
            _svgImage.IsVisible = true;
            _bitmapImage.Source = null;
            _bitmapImage.IsVisible = false;
        }
        else
        {
            _bitmapImage.Source = source;
            _bitmapImage.IsVisible = source is not null;
            _svgImage.Source = null;
            _svgImage.IsVisible = false;
        }
    }

    private void UpdateAspect()
    {
        _bitmapImage.Aspect = Aspect;
        _svgImage.Aspect = Aspect;
    }

    private static bool IsSvgFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return !HasDirectorySeparator(fileName);
        }

        return extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDirectorySeparator(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }
}
