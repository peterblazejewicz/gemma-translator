// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified. It replaces frontend/src/components/Visualizer.jsx.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GemmaTranslator.Controls;

/// <summary>
/// A row of vertical bars that shows the level of the audio of one lane.
/// </summary>
/// <remarks>
/// <para>
/// Each lane gets one instance. Upstream puts the two lanes on two canvas
/// elements and moves one loop between them.
/// </para>
/// <para>
/// CAUTION: this control holds no timer and it does not move by itself. The
/// view model gives a new <see cref="Levels"/> for each frame. Thus the same
/// values always make the same image.
/// </para>
/// <para>
/// This is necessary because the software has no test project. Only an
/// offscreen PNG shows the user interface. A control that moves by itself
/// gives a different image at each start, and no image is then proof.
/// </para>
/// </remarks>
public sealed class AudioVisualizer : Control
{
    // The design gives these values in pixels of the display, and not as a
    // part of the area. They stay the same if the strip changes its height.
    private const double PadX = 24;
    private const double PadY = 10;
    private const double Gap = 3;
    private const float Radius = 2f;

    // The height of each bar while the lane does not record.
    private const double IdleHeight = 6;
    private const double IdleOpacity = 0.4;

    // A level of 1.0 makes a bar of 72 % of the height of the content.
    private const double ActiveScale = 0.72;

    // A lane that records and hears nothing must not go empty. Each bar keeps
    // this height, thus a person sees that the microphone is on.
    private const double MinActiveHeight = 2;

    // A bar of less than one pixel makes a grey area and not a row of bars.
    //
    // The strip is one half of the 1280 pixels of the display, thus the
    // content is 640 - 48 = 592 pixels. The design permits 64 bars, which
    // gives (592 - 63 x 3) / 64 = 6.3 pixels for each bar. The count at which
    // a bar becomes 1 pixel is 148, thus 64 bars are not near the limit.
    private const double MinBarWidth = 1;

    /// <summary>Defines the <see cref="Levels"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> LevelsProperty =
        AvaloniaProperty.Register<AudioVisualizer, IReadOnlyList<double>?>(nameof(Levels));

    /// <summary>Defines the <see cref="IsActive"/> property.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<AudioVisualizer, bool>(nameof(IsActive));

    /// <summary>Defines the <see cref="ActiveBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> ActiveBrushProperty =
        AvaloniaProperty.Register<AudioVisualizer, IBrush?>(nameof(ActiveBrush));

    /// <summary>Defines the <see cref="IdleBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> IdleBrushProperty =
        AvaloniaProperty.Register<AudioVisualizer, IBrush?>(nameof(IdleBrush));

    static AudioVisualizer()
    {
        AffectsRender<AudioVisualizer>(
            LevelsProperty,
            IsActiveProperty,
            ActiveBrushProperty,
            IdleBrushProperty);
    }

    /// <summary>
    /// Gets or sets one value for each bar, from 0.0 to 1.0.
    /// </summary>
    /// <remarks>
    /// The count of the bars is the count of these values. A value of
    /// <c>null</c>, or a count of 0, draws nothing. The control clamps each
    /// value to the range.
    /// </remarks>
    public IReadOnlyList<double>? Levels
    {
        get => GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that says if this lane records.
    /// </summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the bars while the lane records.
    /// </summary>
    public IBrush? ActiveBrush
    {
        get => GetValue(ActiveBrushProperty);
        set => SetValue(ActiveBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the bars while the lane does not record.
    /// </summary>
    public IBrush? IdleBrush
    {
        get => GetValue(IdleBrushProperty);
        set => SetValue(IdleBrushProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<double>? levels = Levels;

        if (levels is null || levels.Count == 0)
        {
            return;
        }

        double width = Bounds.Width - (2 * PadX);
        double height = Bounds.Height - (2 * PadY);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        int count = levels.Count;
        double barWidth = (width - (Gap * (count - 1))) / count;

        if (barWidth < MinBarWidth)
        {
            return;
        }

        double bottom = PadY + height;

        if (IsActive)
        {
            DrawActive(context, levels, barWidth, bottom, height);
        }
        else
        {
            DrawIdle(context, count, barWidth, bottom, height);
        }
    }

    /// <summary>Gives the left edge of one bar.</summary>
    private static double LeftOf(int index, double barWidth) =>
        PadX + (index * (barWidth + Gap));

    private void DrawActive(
        DrawingContext context,
        IReadOnlyList<double> levels,
        double barWidth,
        double bottom,
        double height)
    {
        IBrush? brush = ActiveBrush;

        if (brush is null)
        {
            return;
        }

        for (int i = 0; i < levels.Count; i++)
        {
            double level = Math.Clamp(levels[i], 0, 1);

            // Math.Clamp throws if the minimum is more than the maximum, and a
            // strip that is less than 2 pixels high makes that condition.
            double bar = Math.Min(Math.Max(level * ActiveScale * height, MinActiveHeight), height);

            context.FillRectangle(
                brush,
                new Rect(LeftOf(i, barWidth), bottom - bar, barWidth, bar),
                Radius);
        }
    }

    private void DrawIdle(
        DrawingContext context,
        int count,
        double barWidth,
        double bottom,
        double height)
    {
        IBrush? brush = IdleBrush;

        if (brush is null)
        {
            return;
        }

        double bar = Math.Min(IdleHeight, height);

        using (context.PushOpacity(IdleOpacity))
        {
            for (int i = 0; i < count; i++)
            {
                context.FillRectangle(
                    brush,
                    new Rect(LeftOf(i, barWidth), bottom - bar, barWidth, bar),
                    Radius);
            }
        }
    }
}
