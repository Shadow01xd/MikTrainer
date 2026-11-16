using System.ComponentModel;

namespace MiTrainer.App.Controls;

/// <summary>Interruptor animado. No cambia solo al hacer clic: avisa con ToggleRequested y el dueño fija Checked.</summary>
sealed class ToggleSwitch : PaintedControl
{
    const float AnimationStep = 0.2f;

    readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    bool _checked;
    float _position; // 0 = apagado, 1 = encendido

    public ToggleSwitch()
    {
        Size = new Size(48, 26);
        Cursor = Cursors.Hand;
        _animation.Tick += (_, _) => Animate();
    }

    public event EventHandler? ToggleRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _animation.Start();
        }
    }

    void Animate()
    {
        float target = _checked ? 1 : 0;
        _position += Math.Sign(target - _position) * AnimationStep;
        if (Math.Abs(target - _position) < AnimationStep)
        {
            _position = target;
            _animation.Stop();
        }
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        ToggleRequested?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        var track = Theme.Blend(Theme.ToggleOff, Theme.Accent, _position);
        if (Hover) track = Theme.Blend(track, Color.White, 0.08f);
        FillRounded(g, Bounds0, Height / 2f, track);

        float knob = Height - 8;
        g.FillEllipse(Brushes.White, 4 + _position * (Width - Height), 4, knob, knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
