// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace GemmaTranslator.Views.Behaviors;

/// <remarks>
/// The thread follows its newest turn, and a finger on the panel takes that
/// follow away. The state is on the ScrollViewer, because the view model must
/// not hold a control.
/// </remarks>
public static class ConversationScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsEnabled",
            typeof(ConversationScroll));

    public static readonly AttachedProperty<bool> IsPinnedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsPinned",
            typeof(ConversationScroll),
            defaultValue: true);

    public static readonly AttachedProperty<int> PinRequestProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, int>(
            "PinRequest",
            typeof(ConversationScroll));

    public static readonly AttachedProperty<ICommand?> JumpToEndCommandProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ICommand?>(
            "JumpToEndCommand",
            typeof(ConversationScroll));

    private static readonly AttachedProperty<Follower?> FollowerProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Follower?>(
            "Follower",
            typeof(ConversationScroll));

    static ConversationScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer, bool>(OnIsEnabled);
        PinRequestProperty.Changed.AddClassHandler<ScrollViewer, int>(OnPinRequest);
    }

    public static bool GetIsEnabled(ScrollViewer view) => view.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer view, bool value)
        => view.SetValue(IsEnabledProperty, value);

    public static bool GetIsPinned(ScrollViewer view) => view.GetValue(IsPinnedProperty);

    public static void SetIsPinned(ScrollViewer view, bool value)
        => view.SetValue(IsPinnedProperty, value);

    /// <summary>
    /// A count, and not a value. Each increase takes the thread to its newest
    /// turn.
    /// </summary>
    public static int GetPinRequest(ScrollViewer view) => view.GetValue(PinRequestProperty);

    public static void SetPinRequest(ScrollViewer view, int value)
        => view.SetValue(PinRequestProperty, value);

    public static ICommand? GetJumpToEndCommand(ScrollViewer view)
        => view.GetValue(JumpToEndCommandProperty);

    public static void SetJumpToEndCommand(ScrollViewer view, ICommand? value)
        => view.SetValue(JumpToEndCommandProperty, value);

    private static void OnIsEnabled(
        ScrollViewer view,
        AvaloniaPropertyChangedEventArgs<bool> args)
    {
        view.GetValue(FollowerProperty)?.Detach();

        Follower? follower = args.NewValue.GetValueOrDefault()
            ? new Follower(view)
            : null;

        view.SetValue(FollowerProperty, follower);
        view.SetValue(
            JumpToEndCommandProperty,
            follower is null ? null : new RelayCommand(follower.Pin));
    }

    private static void OnPinRequest(
        ScrollViewer view,
        AvaloniaPropertyChangedEventArgs<int> args)
        => view.GetValue(FollowerProperty)?.Pin();

    private sealed class Follower
    {
        // The design. A finger that moves the thread more than 60 pixels away
        // from the bottom gives it to the person who reads it.
        private const double Slack = 60;

        // One frame takes this part of the distance that is left. At 60 Hz the
        // move is about 95 % complete after 0.2 s.
        private const double GlideStep = 0.25;

        private static readonly TimeSpan GlideFrame = TimeSpan.FromMilliseconds(16);

        private readonly ScrollViewer _view;
        private readonly IDisposable _extent;
        private bool _dragging;
        private DispatcherTimer? _glide;

        public Follower(ScrollViewer view)
        {
            _view = view;

            // The follow comes from the extent and not from a change of the
            // collection. An offset that this class writes immediately after a
            // turn goes in the thread is too small: the new bubble has no
            // measurement at that moment, thus the offset stays in the extent
            // of the bubbles before it. The extent changes as a result
            // of the layout, thus the bubble has its measurement when this
            // comes. The same signal covers a bubble that becomes taller when
            // its translation replaces "Translating…".
            _extent = view
                .GetObservable(ScrollViewer.ExtentProperty)
                .Subscribe(new AnonymousObserver<Size>(_ => Follow()));

            // handledEventsToo is necessary. ScrollContentPresenter is the
            // child that moves the content, and it marks each of these events
            // handled. A handler without this flag sees nothing at all. That
            // is the trap: the events come, and the handler does not see them.
            view.AddHandler(
                InputElement.ScrollGestureEvent,
                OnDrag,
                RoutingStrategies.Bubble,
                handledEventsToo: true);

            view.AddHandler(
                InputElement.ScrollGestureEndedEvent,
                OnDragEnded,
                RoutingStrategies.Bubble,
                handledEventsToo: true);

            // The wheel is the second source. The touch recognizer gives
            // nothing for a mouse, thus this is the path on the Windows host.
            view.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnWheel,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        public void Detach()
        {
            StopGlide();

            _extent.Dispose();

            _view.RemoveHandler(InputElement.ScrollGestureEvent, OnDrag);
            _view.RemoveHandler(InputElement.ScrollGestureEndedEvent, OnDragEnded);
            _view.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        }

        public void Pin()
        {
            _dragging = false;
            SetIsPinned(_view, true);
            Glide();
        }

        private void Follow()
        {
            if (_dragging || _glide is not null || !GetIsPinned(_view))
            {
                return;
            }

            _view.ScrollToEnd();
        }

        private void OnDrag(object? sender, ScrollGestureEventArgs e)
        {
            _dragging = true;
            StopGlide();
            Weigh();
        }

        private void OnDragEnded(object? sender, ScrollGestureEndedEventArgs e)
        {
            _dragging = false;
            Weigh();
        }

        private void OnWheel(object? sender, PointerWheelEventArgs e) => Weigh();

        // ScrollContentPresenter is a child of this control and it moved the
        // content before this runs. Thus the offset here is the one that a
        // person sees.
        private void Weigh() => SetIsPinned(_view, End() - _view.Offset.Y <= Slack);

        private double End() => Math.Max(0, _view.Extent.Height - _view.Viewport.Height);

        // The design asks for a smooth move. A Transitions entry on
        // OffsetProperty also makes the offset that a finger writes go slowly,
        // and the content then follows the finger late. This timer moves the
        // offset of this class only.
        private void Glide()
        {
            StopGlide();

            _glide = new DispatcherTimer { Interval = GlideFrame };
            _glide.Tick += (_, _) => Step();
            _glide.Start();
        }

        private void StopGlide()
        {
            _glide?.Stop();
            _glide = null;
        }

        private void Step()
        {
            try
            {
                double target = End();
                double next = _view.Offset.Y + ((target - _view.Offset.Y) * GlideStep);

                if (Math.Abs(target - next) < 1)
                {
                    StopGlide();
                    _view.ScrollToEnd();
                    return;
                }

                _view.SetCurrentValue(ScrollViewer.OffsetProperty, _view.Offset.WithY(next));
            }
#pragma warning disable CA1031 // An error out of a Tick has no catch and the process stops.
            catch (Exception)
#pragma warning restore CA1031
            {
                // The move stops and the thread stays where it is. The journal
                // gets no line, because a behaviour has no logger; the cost is
                // a jump control that a touch does not answer.
                StopGlide();
            }
        }
    }
}
