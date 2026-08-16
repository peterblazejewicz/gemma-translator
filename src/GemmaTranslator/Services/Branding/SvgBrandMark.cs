// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Svg.Skia;

namespace GemmaTranslator.Services.Branding;

/// <summary>
/// Reads the mark from an SVG beside the binary. The file of the owner comes
/// first, then the file with no brand.
/// </summary>
public sealed partial class SvgBrandMark : IBrandMark
{
    /// <summary>
    /// The order of the search. The first file that is there wins.
    /// </summary>
    private static readonly string[] Candidates =
    [
        Path.Combine("Assets", "branded", "brand-mark.svg"),
        Path.Combine("Assets", "brand-mark.svg"),
    ];

    /// <summary>
    /// The width in pixels that the raster gets. The panel gives the mark
    /// 250 px on the warm-up screen and 200 px on the screensaver, and this is
    /// four times the larger of the two. The letters of the mark with no brand
    /// are a stroke and not a fill, thus a raster that is too small thins them.
    /// </summary>
    private const float RasterWidth = 1000f;

    public SvgBrandMark(ILogger<SvgBrandMark> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Image = Load(logger);
    }

    public IImage? Image { get; }

    private static IImage? Load(ILogger logger)
    {
        foreach (string candidate in Candidates)
        {
            string path = Path.Combine(AppContext.BaseDirectory, candidate);

            if (!File.Exists(path))
            {
                continue;
            }

            IImage? image = Rasterize(path, logger);

            if (image is not null)
            {
                LogMark(logger, path);
                return image;
            }

            // The file is there and it did not draw. Do not take the next
            // candidate: a person who put a file in place must learn that it
            // failed, and not see the mark of the other one and believe that
            // theirs operates.
            return null;
        }

        LogNoMark(logger, AppContext.BaseDirectory);
        return null;
    }

    // The appliance must start with no mark rather than not start. This process
    // holds the speech of a person and it stands in a public place, thus a
    // defect in a file of art is not a cause to lose the translator.
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "See the comment above.")]
    private static IImage? Rasterize(string path, ILogger logger)
    {
        try
        {
            using SKSvg svg = new();
            using FileStream file = File.OpenRead(path);

            if (svg.Load(file) is null || svg.Picture is null)
            {
                LogNotDrawn(logger, path);
                return null;
            }

            SKRect box = svg.Picture.CullRect;

            if (box.Width <= 0 || box.Height <= 0)
            {
                LogNotDrawn(logger, path);
                return null;
            }

            float scale = RasterWidth / box.Width;
            SKImageInfo info = new(
                (int)MathF.Round(box.Width * scale),
                (int)MathF.Round(box.Height * scale));

            using SKSurface surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale(scale);
            surface.Canvas.DrawPicture(svg.Picture);

            using SKImage rendered = surface.Snapshot();
            using SKData png = rendered.Encode(SKEncodedImageFormat.Png, 100);
            using MemoryStream memory = new();
            png.SaveTo(memory);
            memory.Position = 0;

            return new Bitmap(memory);
        }
        catch (Exception exception)
        {
            LogNotDrawn(logger, path, exception);
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The mark of the appliance comes from {path}.")]
    private static partial void LogMark(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "There is no mark below {directory}. The two screens show none.")]
    private static partial void LogNoMark(ILogger logger, string directory);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{path} did not draw. The two screens show no mark.")]
    private static partial void LogNotDrawn(ILogger logger, string path, Exception? exception = null);
}
