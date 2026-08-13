// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GemmaTranslator.Controls;

/// <remarks>
/// The design is an SVG with a viewBox of 42 by 22. Each value below is in
/// that space, and <see cref="Render"/> puts one transform on all of it. Thus
/// the primary display uses the icon at 42 by 22 and the display of the low
/// charge uses the same code at 120 by 60.
/// </remarks>
public sealed class BatteryGlyph : Control
{
    private const double DesignWidth = 42;
    private const double DesignHeight = 22;

    private const double BodyStroke = 2;
    private const double BodyRadius = 3.5;

    private const double TerminalRadius = 1.5;

    private const double ChargeX = 4;
    private const double ChargeY = 6;
    private const double ChargeFullWidth = 28;
    private const double ChargeHeight = 10;
    private const double ChargeRadius = 1.5;

    private const double BoltStroke = 1;

    private const double UnknownSize = 14;
    private const double UnknownCentreX = 18;
    private const double UnknownBaseline = 16;

    // Avalonia puts the line of the body on its centre, and SVG does the same.
    // Thus the rectangle here is the same as the one of the design.
    private static readonly Rect BodyRect = new(1, 3, 34, 16);
    private static readonly Rect TerminalRect = new(37, 8, 4, 6);

    // M20 4 L13 12 h5 l-2 6 7-8 h-5 z, with each point made absolute.
    private static readonly Geometry Bolt = MakeBolt();

    public static readonly StyledProperty<int?> PercentProperty =
        AvaloniaProperty.Register<BatteryGlyph, int?>(nameof(Percent));

    public static readonly StyledProperty<bool> IsChargingProperty =
        AvaloniaProperty.Register<BatteryGlyph, bool>(nameof(IsCharging));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<BatteryGlyph, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<BatteryGlyph, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> GroundBrushProperty =
        AvaloniaProperty.Register<BatteryGlyph, IBrush?>(nameof(GroundBrush));

    static BatteryGlyph()
    {
        AffectsRender<BatteryGlyph>(
            PercentProperty,
            IsChargingProperty,
            StrokeProperty,
            FillProperty,
            GroundBrushProperty);
    }

    /// <summary>
    /// Gets or sets the state of charge, or <c>null</c> if it is not known.
    /// </summary>
    /// <remarks>
    /// CAUTION: the fuel gauge computes this value and it can give more than
    /// 100. The drawn bar is clamped, thus it stays in the body.
    /// </remarks>
    public int? Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool IsCharging
    {
        get => GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the body, the terminal, the bolt, and the
    /// sign of a charge that is not known.
    /// </summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the bar of the charge.
    /// </summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the page, for the line around the bolt only.
    /// </summary>
    public IBrush? GroundBrush
    {
        get => GetValue(GroundBrushProperty);
        set => SetValue(GroundBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        double scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);

        if (!double.IsFinite(scale) || scale <= 0)
        {
            return;
        }

        Matrix place = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(
            (Bounds.Width - (DesignWidth * scale)) / 2,
            (Bounds.Height - (DesignHeight * scale)) / 2);

        using (context.PushTransform(place))
        {
            DrawGlyph(context);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        Math.Min(availableSize.Width, DesignWidth),
        Math.Min(availableSize.Height, DesignHeight));

    private static Geometry MakeBolt()
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(20, 4), isFilled: true);
            path.LineTo(new Point(13, 12));
            path.LineTo(new Point(18, 12));
            path.LineTo(new Point(16, 18));
            path.LineTo(new Point(23, 10));
            path.LineTo(new Point(18, 10));
            path.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static void DrawUnknown(DrawingContext context, IBrush stroke)
    {
        // The default family comes from FontManagerOptions, which the software
        // sets at the start. Raspberry Pi OS Lite can supply no font of the
        // system, thus a family that the system gives is not a safe selection.
        FormattedText text = new(
            "?",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontManager.Current.DefaultFontFamily, FontStyle.Normal, FontWeight.SemiBold),
            UnknownSize,
            stroke);

        // The design gives the centre and the baseline of the glyph, as an SVG
        // text element does. DrawText takes the top left corner.
        context.DrawText(
            text,
            new Point(UnknownCentreX - (text.Width / 2), UnknownBaseline - text.Baseline));
    }

    private void DrawGlyph(DrawingContext context)
    {
        IBrush? stroke = Stroke;

        if (stroke is not null)
        {
            // A new pen for each frame, and no field that keeps it. A brush
            // that the view model gives can change its colour and keep its
            // identity, thus a pen that is kept can show the colour of before.
            IImmutableBrush immutable = stroke.ToImmutable();

            context.DrawRectangle(
                null,
                new ImmutablePen(immutable, BodyStroke),
                new RoundedRect(BodyRect, BodyRadius));

            context.DrawRectangle(immutable, null, new RoundedRect(TerminalRect, TerminalRadius));
        }

        int? percent = Percent;

        if (percent is null)
        {
            // CAUTION: the bolt and this sign are at the same location of the
            // 42 by 22 box, thus the two together are not clear. A charge that
            // is not known is the more important fact, and the bolt goes away.
            if (stroke is not null)
            {
                DrawUnknown(context, stroke);
            }

            return;
        }

        DrawCharge(context, percent.Value);

        if (IsCharging && stroke is not null)
        {
            DrawBolt(context, stroke);
        }
    }

    private void DrawCharge(DrawingContext context, int percent)
    {
        IBrush? fill = Fill;
        double width = ChargeFullWidth * (Math.Clamp(percent, 0, 100) / 100.0);

        if (fill is null || width <= 0)
        {
            return;
        }

        context.DrawRectangle(
            fill,
            null,
            new RoundedRect(new Rect(ChargeX, ChargeY, width, ChargeHeight), ChargeRadius));
    }

    private void DrawBolt(DrawingContext context, IBrush stroke)
    {
        IBrush? ground = GroundBrush;

        // The bolt is on top of the bar of the charge, and the two brushes can
        // be almost the same colour.
        IPen? edge = ground is null ? null : new ImmutablePen(ground.ToImmutable(), BoltStroke);

        context.DrawGeometry(stroke, edge, Bolt);
    }
}
