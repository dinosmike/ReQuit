using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReQuit.Controls;

public partial class TimerDigitGroup : UserControl
{
    private int _value;
    private bool _isAnimating;
    private bool _isDragging;
    private bool _pendingPointer;
    private System.Windows.Point _downPosInBorder;
    private double _lastDragY;
    private double _dragAccum;

    private const double DragStepPx = 40.0;
    private const double ClickDragThresholdPx = 5.0;
    private const double TickAnimMs = 300;
    private const double InputAnimMs = 180;
    private const double DefaultCellHeight = 136;
    /// <summary>Совпадает с CornerRadius у DigitBorder в XAML — обрезка содержимого по скруглению.</summary>
    private const double DigitBorderCornerRadius = 20;

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(int), typeof(TimerDigitGroup),
            new PropertyMetadata(59));

    public int MaxValue
    {
        get => (int)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(TimerDigitGroup),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty UnitTextProperty =
        DependencyProperty.Register(nameof(UnitText), typeof(string), typeof(TimerDigitGroup),
            new PropertyMetadata("", OnUnitTextChanged));

    public string UnitText
    {
        get => (string)GetValue(UnitTextProperty);
        set => SetValue(UnitTextProperty, value);
    }

    public static readonly DependencyProperty ShutdownActionProperty =
        DependencyProperty.Register(nameof(ShutdownAction), typeof(bool), typeof(TimerDigitGroup),
            new PropertyMetadata(true, OnShutdownActionChanged));

    public bool ShutdownAction
    {
        get => (bool)GetValue(ShutdownActionProperty);
        set => SetValue(ShutdownActionProperty, value);
    }

    public int Value => _value;

    public event Action? ValueChanged;

    public TimerDigitGroup()
    {
        InitializeComponent();
        ApplyDigitTheme();
        DigitBorder.SizeChanged += (_, _) => UpdateDigitBorderClip();
        Loaded += (_, _) => UpdateDigitBorderClip();
    }

    /// <summary>
    /// WPF не обрезает дочерние элементы Border по CornerRadius — градиенты/цифры дают прямоугольник за скруглением.
    /// </summary>
    private void UpdateDigitBorderClip()
    {
        if (DigitBorder.ActualWidth <= 0 || DigitBorder.ActualHeight <= 0)
            return;

        DigitBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, DigitBorder.ActualWidth, DigitBorder.ActualHeight),
            DigitBorderCornerRadius,
            DigitBorderCornerRadius);
    }

    private static void OnUnitTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimerDigitGroup g)
            g.UnitLabel.Text = (string)e.NewValue;
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimerDigitGroup g)
            g.UpdateInteractiveState();
    }

    private static void OnShutdownActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TimerDigitGroup g)
            return;
        g.ApplyDigitTheme();
        g.ResetBorderBrush();
        g.ResetArrowStrokeIdle();
        if (g.IsMouseOver && !g.IsReadOnly)
        {
            g.ApplyHoverChrome();
            g.FadeArrows(0.7);
        }
    }

    private void UpdateInteractiveState()
    {
        DigitBorder.Cursor = IsReadOnly ? Cursors.Arrow : Cursors.SizeNS;
        if (IsReadOnly)
        {
            FadeArrows(0);
            ResetBorderBrush();
        }
    }

    private void ApplyDigitTheme()
    {
        var brush = ShutdownAction
            ? (Brush)FindResource("Rs.Danger")
            : (Brush)FindResource("Rs.Text.Primary");
        CurrentTensText.Foreground = brush;
        IncomingTensText.Foreground = brush;
        CurrentOnesText.Foreground = brush;
        IncomingOnesText.Foreground = brush;
    }

    private void ResetArrowStrokeIdle()
    {
        if (ShutdownAction)
        {
            UpArrow.Stroke = System.Windows.Media.Brushes.White;
            DownArrow.Stroke = System.Windows.Media.Brushes.White;
        }
        else if (TryFindResource("Rs.Accent") is Brush a)
        {
            UpArrow.Stroke = a;
            DownArrow.Stroke = a;
        }
    }

    private void ApplyHoverChrome()
    {
        if (ShutdownAction)
        {
            DigitBorder.BorderBrush = System.Windows.Media.Brushes.White;
            UpArrow.Stroke = System.Windows.Media.Brushes.White;
            DownArrow.Stroke = System.Windows.Media.Brushes.White;
        }
        else
        {
            DigitBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0xD4, 0xC3));
            if (TryFindResource("Rs.Accent") is Brush a)
            {
                UpArrow.Stroke = a;
                DownArrow.Stroke = a;
            }
        }
    }

    /// <summary>
    /// Animate the digit rolling to a new value. Called by the countdown timer.
    /// Does NOT fire ValueChanged to prevent circular updates.
    /// </summary>
    public void AnimateToValue(int newValue, bool slideDown = true)
    {
        newValue = Wrap(newValue);
        int prev = _value;
        if (newValue == prev && !_isAnimating) return;

        if (_isAnimating)
            SnapFinish();

        _value = newValue;
        RunSlideAnimation(prev, newValue, slideDown, TickAnimMs);
    }

    /// <summary>
    /// Set value instantly without animation. Does NOT fire ValueChanged.
    /// </summary>
    public void SetValueSilent(int newValue)
    {
        if (_isAnimating)
            SnapFinish();

        _value = Wrap(newValue);
        ApplyDigitDisplay(_value);
    }

    private void ApplyDigitDisplay(int v)
    {
        int tens = v / 10;
        int ones = v % 10;
        CurrentTensText.Text = tens.ToString();
        CurrentOnesText.Text = ones.ToString();
        CurrentTensTransform.Y = 0;
        IncomingTensTransform.Y = 0;
        CurrentOnesTransform.Y = 0;
        IncomingOnesTransform.Y = 0;
        IncomingTensText.Visibility = Visibility.Collapsed;
        IncomingOnesText.Visibility = Visibility.Collapsed;
    }

    private void StepFromInput(int delta, bool slideDown)
    {
        int prev = _value;
        int newVal = Wrap(_value + delta);
        if (newVal == prev) return;

        if (_isAnimating)
            SnapFinish();

        _value = newVal;
        RunSlideAnimation(prev, newVal, slideDown, InputAnimMs);
        ValueChanged?.Invoke();
    }

    private void RunSlideAnimation(int from, int to, bool slideDown, double durationMs)
    {
        _isAnimating = true;

        int fromTens = from / 10;
        int fromOnes = from % 10;
        int toTens = to / 10;
        int toOnes = to % 10;
        bool tensChanges = fromTens != toTens;

        double h = DigitBorder.ActualHeight > 0 ? DigitBorder.ActualHeight : DefaultCellHeight;
        double inStart = slideDown ? -h : h;
        double curEnd = slideDown ? h : -h;

        var dur = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (tensChanges)
        {
            CurrentTensText.Text = fromTens.ToString();
            IncomingTensText.Text = toTens.ToString();
            CurrentOnesText.Text = fromOnes.ToString();
            IncomingOnesText.Text = toOnes.ToString();
            IncomingTensText.Visibility = Visibility.Visible;
            IncomingOnesText.Visibility = Visibility.Visible;

            IncomingTensTransform.Y = inStart;
            CurrentTensTransform.Y = 0;
            IncomingOnesTransform.Y = inStart;
            CurrentOnesTransform.Y = 0;

            var animCurT = new DoubleAnimation(0, curEnd, dur) { EasingFunction = ease };
            var animIncT = new DoubleAnimation(inStart, 0, dur) { EasingFunction = ease };
            var animCurO = new DoubleAnimation(0, curEnd, dur) { EasingFunction = ease };
            var animIncO = new DoubleAnimation(inStart, 0, dur) { EasingFunction = ease };

            int capturedTo = to;
            animIncO.Completed += (_, _) => OnSlideComplete(capturedTo);

            CurrentTensTransform.BeginAnimation(TranslateTransform.YProperty, animCurT);
            IncomingTensTransform.BeginAnimation(TranslateTransform.YProperty, animIncT);
            CurrentOnesTransform.BeginAnimation(TranslateTransform.YProperty, animCurO);
            IncomingOnesTransform.BeginAnimation(TranslateTransform.YProperty, animIncO);
        }
        else
        {
            CurrentTensText.Text = toTens.ToString();
            IncomingTensText.Visibility = Visibility.Collapsed;
            CurrentTensTransform.Y = 0;
            IncomingTensTransform.Y = 0;

            CurrentOnesText.Text = fromOnes.ToString();
            IncomingOnesText.Text = toOnes.ToString();
            IncomingOnesText.Visibility = Visibility.Visible;

            IncomingOnesTransform.Y = inStart;
            CurrentOnesTransform.Y = 0;

            var animCurO = new DoubleAnimation(0, curEnd, dur) { EasingFunction = ease };
            var animIncO = new DoubleAnimation(inStart, 0, dur) { EasingFunction = ease };

            int capturedTo = to;
            animIncO.Completed += (_, _) => OnSlideComplete(capturedTo);

            CurrentOnesTransform.BeginAnimation(TranslateTransform.YProperty, animCurO);
            IncomingOnesTransform.BeginAnimation(TranslateTransform.YProperty, animIncO);
        }
    }

    private void OnSlideComplete(int capturedTo)
    {
        CurrentTensTransform.BeginAnimation(TranslateTransform.YProperty, null);
        IncomingTensTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CurrentOnesTransform.BeginAnimation(TranslateTransform.YProperty, null);
        IncomingOnesTransform.BeginAnimation(TranslateTransform.YProperty, null);

        ApplyDigitDisplay(capturedTo);
        _isAnimating = false;
    }

    private void SnapFinish()
    {
        CurrentTensTransform.BeginAnimation(TranslateTransform.YProperty, null);
        IncomingTensTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CurrentOnesTransform.BeginAnimation(TranslateTransform.YProperty, null);
        IncomingOnesTransform.BeginAnimation(TranslateTransform.YProperty, null);

        if (IncomingTensText.Visibility == Visibility.Visible)
            CurrentTensText.Text = IncomingTensText.Text;
        if (IncomingOnesText.Visibility == Visibility.Visible)
            CurrentOnesText.Text = IncomingOnesText.Text;

        CurrentTensTransform.Y = 0;
        IncomingTensTransform.Y = 0;
        CurrentOnesTransform.Y = 0;
        IncomingOnesTransform.Y = 0;
        IncomingTensText.Visibility = Visibility.Collapsed;
        IncomingOnesText.Visibility = Visibility.Collapsed;
        _isAnimating = false;
    }

    private int Wrap(int v)
    {
        if (v < 0) return MaxValue;
        if (v > MaxValue) return 0;
        return v;
    }

    /// <summary>Точки «прилипания» к десяткам: 0, 10, … и при необходимости <see cref="MaxValue"/> (например 24 для часов).</summary>
    private IReadOnlyList<int> GetSnapPoints()
    {
        var list = new List<int>();
        for (int x = 0; x <= MaxValue; x += 10)
            list.Add(x);
        if (MaxValue % 10 != 0 && (list.Count == 0 || list[^1] != MaxValue))
            list.Add(MaxValue);
        return list;
    }

    private int SnapNext(int v)
    {
        var pts = GetSnapPoints();
        foreach (var p in pts)
        {
            if (p > v)
                return p;
        }

        return pts[0];
    }

    private int SnapPrev(int v)
    {
        var pts = GetSnapPoints();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            if (pts[i] < v)
                return pts[i];
        }

        return pts[^1];
    }

    private void SnapByHalf(bool upperHalf)
    {
        int target = upperHalf ? SnapNext(_value) : SnapPrev(_value);
        if (target == _value)
            return;

        if (_isAnimating)
            SnapFinish();

        int old = _value;
        _value = target;
        bool slideDown = target < old;
        RunSlideAnimation(old, target, slideDown, InputAnimMs);
        ValueChanged?.Invoke();
    }

    // ── Mouse drag ──────────────────────────────────────────

    private void OnDigitMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly) return;
        e.Handled = true;

        if (_value == 0)
            return;

        if (_isAnimating)
            SnapFinish();

        int old = _value;
        _value = 0;
        RunSlideAnimation(old, 0, slideDown: true, InputAnimMs);
        ValueChanged?.Invoke();
    }

    private void OnDigitMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly) return;

        _pendingPointer = true;
        _isDragging = false;
        _downPosInBorder = e.GetPosition(DigitBorder);
        _lastDragY = e.GetPosition(this).Y;
        _dragAccum = 0;
        DigitBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnDigitMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pendingPointer && !_isDragging)
            return;

        if (_pendingPointer)
        {
            var pos = e.GetPosition(DigitBorder);
            var d = pos - _downPosInBorder;
            if (d.Length > ClickDragThresholdPx)
            {
                _pendingPointer = false;
                _isDragging = true;
                _lastDragY = e.GetPosition(this).Y;
                _dragAccum = 0;
            }
            else
            {
                return;
            }
        }

        if (!_isDragging)
            return;

        double y = e.GetPosition(this).Y;
        double delta = y - _lastDragY;
        _lastDragY = y;
        _dragAccum += delta;

        while (_dragAccum >= DragStepPx)
        {
            _dragAccum -= DragStepPx;
            StepFromInput(-1, slideDown: true);
        }
        while (_dragAccum <= -DragStepPx)
        {
            _dragAccum += DragStepPx;
            StepFromInput(+1, slideDown: false);
        }
    }

    private void OnDigitMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pendingPointer && !_isDragging)
            return;

        if (_pendingPointer)
        {
            _pendingPointer = false;
            double h = DigitBorder.ActualHeight > 0 ? DigitBorder.ActualHeight : DefaultCellHeight;
            bool upper = _downPosInBorder.Y < h * 0.5;
            DigitBorder.ReleaseMouseCapture();
            e.Handled = true;
            SnapByHalf(upper);
            return;
        }

        _isDragging = false;
        _dragAccum = 0;
        DigitBorder.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnDigitMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_pendingPointer || _isDragging)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        _pendingPointer = false;
        DigitBorder.ReleaseMouseCapture();
    }

    // ── Mouse wheel ─────────────────────────────────────────

    private void OnDigitMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsReadOnly) return;

        if (e.Delta > 0)
            StepFromInput(+1, slideDown: false);
        else
            StepFromInput(-1, slideDown: true);

        e.Handled = true;
    }

    // ── Hover effects ───────────────────────────────────────

    private void OnGroupMouseEnter(object sender, MouseEventArgs e)
    {
        if (IsReadOnly) return;
        ApplyHoverChrome();
        FadeArrows(0.7);
    }

    private void OnGroupMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        ResetBorderBrush();
        ResetArrowStrokeIdle();
        FadeArrows(0);
    }

    private void FadeArrows(double target)
    {
        var dur = TimeSpan.FromMilliseconds(180);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        UpArrow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(target, dur) { EasingFunction = ease });
        DownArrow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(target, dur) { EasingFunction = ease });
    }

    private void ResetBorderBrush()
    {
        if (TryFindResource("Rs.Border") is Brush b)
            DigitBorder.BorderBrush = b;
    }
}
