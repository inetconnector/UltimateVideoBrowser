using System;
using System.IO;
using System.Threading;
using SkiaSharp;
using SkiaSharp.Extended.Svg;
using SkiaSharp.Views.Maui.Controls;

namespace UltimateVideoBrowser.Views.Controls;

public sealed class SvgImage : SKCanvasView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(string),
        typeof(SvgImage),
        default(string),
        propertyChanged: OnSourceChanged);

    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect),
        typeof(Aspect),
        typeof(SvgImage),
        Aspect.AspectFit,
        propertyChanged: OnAspectChanged);

    private SKPicture? _picture;
    private SKRect _bounds;
    private int _loadToken;

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear();

        if (_picture is null || _bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return;
        }

        var destRect = CalculateDestinationRect(_bounds, e.Info.Width, e.Info.Height, Aspect);
        if (destRect.Width <= 0 || destRect.Height <= 0)
        {
            return;
        }

        var scaleX = destRect.Width / _bounds.Width;
        var scaleY = destRect.Height / _bounds.Height;

        canvas.Translate(destRect.Left - (_bounds.Left * scaleX), destRect.Top - (_bounds.Top * scaleY));
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(_picture);
    }

    private static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (SvgImage)bindable;
        control.LoadSvgAsync(newValue as string);
    }

    private static void OnAspectChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SvgImage)bindable).InvalidateSurface();
    }

    private async void LoadSvgAsync(string? source)
    {
        var currentToken = Interlocked.Increment(ref _loadToken);

        _picture = null;
        _bounds = SKRect.Empty;
        DispatchInvalidate();

        var resolved = NormalizeSvgFileName(source);
        if (resolved is null)
        {
            return;
        }

        try
        {
            await using var stream = await TryOpenSvgStreamAsync(resolved);
            if (stream is null)
            {
                return;
            }

            var svg = new SKSvg();
            svg.Load(stream);

            if (currentToken != _loadToken)
            {
                return;
            }

            _picture = svg.Picture;
            _bounds = svg.Picture?.CullRect ?? SKRect.Empty;
        }
        catch (Exception)
        {
            _picture = null;
            _bounds = SKRect.Empty;
        }
        finally
        {
            DispatchInvalidate();
        }
    }

    private void DispatchInvalidate()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(InvalidateSurface);
        }
        else
        {
            InvalidateSurface();
        }
    }

    private static string? NormalizeSvgFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var resolvedName = fileName.Trim();
        var hasExtension = Path.HasExtension(resolvedName);
        if (!hasExtension && !HasDirectorySeparator(resolvedName))
        {
            resolvedName += ".svg";
            hasExtension = true;
        }

        if (!hasExtension || !resolvedName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resolvedName;
    }

    private static bool HasDirectorySeparator(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }

    private static async Task<Stream?> TryOpenSvgStreamAsync(string fileName)
    {
        if (File.Exists(fileName))
        {
            return File.OpenRead(fileName);
        }

        try
        {
            return await FileSystem.OpenAppPackageFileAsync(fileName);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }

        if (!HasDirectorySeparator(fileName))
        {
            var fallbackPath = Path.Combine("Resources", "Images", fileName);
            try
            {
                return await FileSystem.OpenAppPackageFileAsync(fallbackPath);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        return null;
    }

    private static SKRect CalculateDestinationRect(SKRect sourceBounds, float width, float height, Aspect aspect)
    {
        if (width <= 0 || height <= 0)
        {
            return SKRect.Empty;
        }

        if (aspect == Aspect.Fill)
        {
            return new SKRect(0, 0, width, height);
        }

        var scaleX = width / sourceBounds.Width;
        var scaleY = height / sourceBounds.Height;
        var scale = aspect == Aspect.AspectFill
            ? Math.Max(scaleX, scaleY)
            : Math.Min(scaleX, scaleY);

        var destWidth = sourceBounds.Width * scale;
        var destHeight = sourceBounds.Height * scale;

        var left = (width - destWidth) / 2f;
        var top = (height - destHeight) / 2f;

        return new SKRect(left, top, left + destWidth, top + destHeight);
    }
}
