using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb.Hero;

internal sealed partial class HeroPainter
{
    private readonly Dictionary<int, Pen> _bandPens = new();
    private Brush? _bandBrush;

    /// <summary>
    /// One vehicle: speed marks, contrails, far limbs, parts in order, then near limbs and the head. With
    /// <paramref name="band"/> only the strokes are drawn again in that brush (the idle light band).
    /// </summary>
    private void DrawActor(DrawingContext crisp, DrawingContext? glow, Vehicle vehicle, ActorPlacement placement, in StartupFx fx,
        double alphaScale, bool ghost, Brush? band)
    {
        var alpha = placement.Alpha * alphaScale * _paint.Presence;
        if (alpha <= 0.002)
        {
            return;
        }

        if (band is not null && !ReferenceEquals(band, _bandBrush))
        {
            _bandBrush = band;
            _bandPens.Clear();
        }

        var motion = _stage.Motion(vehicle, placement, fx);
        var unsprung = Matrix.Identity;
        unsprung.Translate(motion.OffsetX, motion.OffsetY);
        var sprung = Matrix.Identity;
        if (motion.PitchDegrees != 0)
        {
            sprung.RotateAt(motion.PitchDegrees, motion.Pivot.X, motion.Pivot.Y);
        }

        sprung.Translate(motion.OffsetX, motion.SprungY);

        var faded = alpha < 0.999;
        if (faded)
        {
            crisp.PushOpacity(alpha);
            glow?.PushOpacity(alpha);
        }

        if (!ghost && band is null)
        {
            DrawSpeedMarks(crisp, vehicle, motion);
            DrawContrails(crisp, vehicle, motion, fx);
        }

        var rigged = _stage.PoseRig(vehicle, fx, _pose);
        DrawLayer(crisp, false, vehicle, motion, unsprung, sprung, fx, rigged, band);
        if (glow is not null && !ghost && band is null)
        {
            DrawLayer(glow, true, vehicle, motion, unsprung, sprung, fx, rigged, null);
        }

        if (faded)
        {
            crisp.Pop();
            glow?.Pop();
        }
    }

    private void DrawLayer(DrawingContext dc, bool glow, Vehicle vehicle, in ActorMotion motion, Matrix unsprung, Matrix sprung,
        in StartupFx fx, bool rigged, Brush? band)
    {
        if (rigged)
        {
            Use(dc, unsprung);
            for (var i = 0; i < _pose.FarCount; i++)
            {
                DrawLimb(dc, glow, vehicle, i, band);
            }
        }

        var parts = vehicle.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            DrawPart(dc, glow, vehicle, i, parts[i], motion, unsprung, sprung, fx, rigged, band);
        }

        if (!glow && band is null)
        {
            DrawShockDiamonds(dc, vehicle, sprung, fx);
            DrawFieldPulse(dc, vehicle, motion, sprung, fx);
        }

        if (rigged)
        {
            Use(dc, unsprung);
            for (var i = _pose.FarCount; i < _pose.Count; i++)
            {
                DrawLimb(dc, glow, vehicle, i, band);
            }

            if (_pose.HasHead)
            {
                var head = _pose.Head;
                var rider = _art.Role(PartRole.Rider);
                StrokeCircle(dc, glow, new Point(head.X, head.Y), head.Radius, _partColor, Alpha(rider, PartRole.Rider, 1, glow),
                    _w0 * rider.Weight, BandPen(band, _w0 * rider.Weight));
            }
        }

        EndRun();
    }

    private void DrawLimb(DrawingContext dc, bool glow, Vehicle vehicle, int slot, Brush? band)
    {
        var limb = _pose[slot];
        var roleKind = limb.Ink == LimbInk.Crank ? PartRole.Crank : PartRole.Rider;
        var role = _art.Role(roleKind);
        var factor = limb.Ink == LimbInk.Far ? 0.42 : 1;
        var width = _w0 * role.Weight;
        StrokeGeometry(dc, glow, _shapes.Limb(vehicle.Index, slot, limb.Points), _partColor, Alpha(role, roleKind, factor, glow), width,
            BandPen(band, width));
    }

    private void DrawPart(DrawingContext dc, bool glow, Vehicle vehicle, int index, VehiclePart part, in ActorMotion motion, Matrix unsprung,
        Matrix sprung, in StartupFx fx, bool rigged, Brush? band)
    {
        var role = _art.Role(part.Role);
        var frame = part.Sprung ? sprung : unsprung;
        if (part.Role == PartRole.Rider && rigged && vehicle.Pedal is not null)
        {
            var offset = _pose.RiderOffset;
            frame.Prepend(new Matrix(1, 0, 0, 1, offset.X, offset.Y));
        }

        if (part.Role is PartRole.Flame or PartRole.FlameCore)
        {
            if (band is null)
            {
                DrawFlame(dc, glow, vehicle, index, part, role, frame, fx);
            }

            return;
        }

        var matrix = frame;
        var factor = 1.0;
        var blur = 0.0;
        if (part.Spin is { } spin)
        {
            var spinMatrix = Matrix.Identity;
            spinMatrix.RotateAt(_stage.SpinAngle(vehicle, spin) * 180 / Math.PI, spin.Pivot.X, spin.Pivot.Y);
            matrix.Prepend(spinMatrix);
            if (part.Role == PartRole.Spoke)
            {
                blur = _stage.SpokeBlur(spin);
                factor *= 1 - blur;
            }
        }

        switch (part.Role)
        {
            case PartRole.Light:
                factor *= _stage.LightFactor(fx);
                break;
            case PartRole.Glow:
                factor *= fx.Hover * (0.5 + 0.5 * _stage.Band) * _stage.Wake;
                matrix.Prepend(new Matrix(1, 0, 0, 1, 0, motion.Hover / 2));
                break;
            case PartRole.Shock when part.Path is { } cone:
                var (scale, opacity) = _stage.ShockCone(fx);
                var shock = Matrix.Identity;
                shock.ScaleAt(1, scale, cone.Start.X, cone.Start.Y);
                matrix.Prepend(shock);
                factor *= opacity;
                break;
        }

        var color = role.Hot ? _partHot : _partColor;
        var alpha = Alpha(role, part.Role, factor, glow);
        var width = _w0 * role.Weight;
        if (alpha > 0.004)
        {
            Use(dc, matrix);
            if (part.Circle is { } c)
            {
                StrokeCircle(dc, glow, new Point(c.X, c.Y), c.Radius, color, alpha, width, BandPen(band, width));
            }
            else if (_shapes.Part(vehicle.Index, index) is { } geometry)
            {
                StrokeGeometry(dc, glow, geometry, color, alpha, width, BandPen(band, width));
            }
        }

        if (blur > 0.01 && !glow && band is null && part.Spin is { } wheel && _shapes.SpokeArcs(vehicle.Index, index) is { } arcs)
        {
            var arcMatrix = Matrix.Identity;
            arcMatrix.RotateAt(_stage.SpokeArcAngle * 180 / Math.PI, wheel.Pivot.X, wheel.Pivot.Y);
            arcMatrix.Append(frame);
            Use(dc, arcMatrix);
            StrokeGeometry(dc, false, arcs, _partColor, 0.5 * blur, _w0 * 0.45);
        }
    }

    private void DrawFlame(DrawingContext dc, bool glow, Vehicle vehicle, int index, VehiclePart part, RoleStyle role, Matrix frame,
        in StartupFx fx)
    {
        if (part.Nozzle is not { } nozzle || _shapes.Part(vehicle.Index, index) is not { } geometry)
        {
            return;
        }

        var (length, width) = _stage.FlameScale(fx);
        if (length < 0.02 || (_hc && part.Role == PartRole.FlameCore))
        {
            return;
        }

        var degrees = Math.Atan2(nozzle.Direction.Y, nozzle.Direction.X) * 180 / Math.PI;
        var local = Matrix.Identity;
        local.Translate(-nozzle.At.X, -nozzle.At.Y);
        local.Rotate(-degrees);
        local.Scale(length, width);
        local.Rotate(degrees);
        local.Translate(nozzle.At.X, nozzle.At.Y);
        local.Append(frame);
        Use(dc, local);
        if (part.Role == PartRole.FlameCore)
        {
            dc.DrawGeometry(_ink.Brush(_core, role.FillOpacity), null, geometry);
        }
        else
        {
            if (!_hc)
            {
                dc.DrawGeometry(_ink.Brush(_color, role.FillOpacity), null, geometry);
            }

            StrokeGeometry(dc, glow, geometry, _partHot, Alpha(role, part.Role, 1, glow), _w0 * role.Weight);
        }
    }

    /// <summary>Trail marks behind the vehicle; with Off and Reduce Motion, 1–3 static marks by band progress.</summary>
    private void DrawSpeedMarks(DrawingContext dc, Vehicle vehicle, in ActorMotion motion)
    {
        var wake = _stage.Wake;
        var ground = _stage.Ground;
        if (_animated ? wake <= 0.05 || ground <= 4 : wake <= 0.5)
        {
            return;
        }

        var trail = vehicle.Emitter(EmitterKind.Trail);
        if (trail is null)
        {
            return;
        }

        var t = _stage.Time;
        var b = _stage.Band;
        var h = _stage.Heat;
        var width = _w0 * 0.5;
        var count = _animated ? trail.Lanes.Count : Math.Min(trail.Lanes.Count, _stage.StaticMarks);
        var transform = Matrix.Identity;
        transform.Translate(motion.OffsetX, motion.OffsetY + (_animated ? motion.Float : 0));
        Use(dc, transform);
        for (var i = 0; i < count; i++)
        {
            var x = trail.At.X - 1.5 - i % 2 * 2.5;
            var y = trail.At.Y + trail.Lanes[i];
            if (_animated)
            {
                var length = trail.Length * (0.45 + 0.55 * b + 0.25 * h) * (1 + 0.18 * Math.Sin(2 * Math.PI * (5.3 + 1.7 * i) * t + i))
                    * Math.Clamp(ground / 20, 0, 1);
                if (length > 0.05)
                {
                    dc.DrawLine(_ink.Fade(_color, 0.55 * wake, width, new Point(x, y), new Point(x - length, y)),
                        new Point(x, y), new Point(x - length, y));
                }
            }
            else if (_ink.Pen(_color, 0.5, width) is { } pen)
            {
                dc.DrawLine(pen, new Point(x, y), new Point(x - trail.Length * 0.7, y));
            }
        }

        EndRun();
    }

    private void DrawContrails(DrawingContext dc, Vehicle vehicle, in ActorMotion motion, in StartupFx fx)
    {
        var wake = _stage.Wake;
        if (wake <= 0.02 || _hc)
        {
            return;
        }

        var t = _stage.Time;
        var distance = _stage.Distance;
        var floatAmplitude = vehicle.Motion.Float;
        for (var e = 0; e < vehicle.Emitters.Count; e++)
        {
            var emitter = vehicle.Emitters[e];
            if (emitter.Kind != EmitterKind.Contrail)
            {
                continue;
            }

            var length = (emitter.Weak ? 22 : 46) * fx.Contrail * (_animated ? 1 : 0.8);
            if (length < 1)
            {
                continue;
            }

            var ax = emitter.At.X - 2;
            var ay = emitter.At.Y;
            for (var k = 0; k < _contrail.Length; k++)
            {
                var d = length * k / (_contrail.Length - 1);
                var drift = _animated && floatAmplitude > 0
                    ? floatAmplitude * Math.Sin(2 * Math.PI * 0.32 * (t - d / 90)) * (0.3 + 0.7 * wake)
                    : 0;
                var ripple = _animated ? 0.35 * Math.Sin((ax - d + distance * 0.05) / 7) * (d / 40) : 0;
                _contrail[k] = new PointD(ax - d, ay + drift + ripple);
            }

            var alpha = (emitter.Weak ? 0.26 : 0.55) * (0.5 + 0.5 * _stage.Band) * wake;
            var transform = Matrix.Identity;
            transform.Translate(motion.OffsetX, motion.OffsetY);
            Use(dc, transform);
            dc.DrawGeometry(null, _ink.Fade(_color, alpha, _w0 * (emitter.Weak ? 0.45 : 0.8), new Point(ax, ay), new Point(ax - length, ay)),
                _shapes.Contrail(vehicle.Index, e, _contrail));
        }

        EndRun();
    }

    /// <summary>Three shock diamonds behind each afterburner (Standard and Max, b &gt; 0.35).</summary>
    private void DrawShockDiamonds(DrawingContext dc, Vehicle vehicle, Matrix sprung, in StartupFx fx)
    {
        var b = _stage.Band;
        if (_level < EffectsIntensity.Standard || b <= 0.35 || !_animated || _hc)
        {
            return;
        }

        var color = _dark ? _core : _hot;
        var length = _stage.SteadyFlameLength(fx);
        var t = _stage.Time;
        foreach (var emitter in vehicle.Emitters)
        {
            if (emitter.Kind != EmitterKind.Afterburner)
            {
                continue;
            }

            for (var i = 0; i < 3; i++)
            {
                var d = i switch { 0 => 3.0, 1 => 5.8, _ => 8.6 };
                var size = 1.0 - i * 0.18;
                var alpha = 0.75 * (b - 0.35) / 0.65 * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 19 * t + i));
                var matrix = new Matrix(size, 0, 0, size, emitter.At.X + emitter.Direction.X * d * length, emitter.At.Y + emitter.Direction.Y * d * length);
                matrix.Append(sprung);
                if (_ink.Pen(color, alpha, _w0 * 0.35 / size) is { } pen)
                {
                    Use(dc, matrix);
                    dc.DrawGeometry(null, pen, _shapes.Diamond);
                }
            }
        }
    }

    /// <summary>The maglev's bright 14-unit pulse travelling along the hover gap from nose to tail at 0.5 G.</summary>
    private void DrawFieldPulse(DrawingContext dc, Vehicle vehicle, in ActorMotion motion, Matrix sprung, in StartupFx fx)
    {
        if (vehicle.Motion.Hover <= 0 || !_animated || _stage.Wake <= 0.05 || _hc)
        {
            return;
        }

        VehiclePart? gap = null;
        foreach (var part in vehicle.Parts)
        {
            if (part.Role == PartRole.Glow && part.Path is not null)
            {
                gap = part;
                break;
            }
        }

        if (gap?.Path is not { } path)
        {
            return;
        }

        var x0 = path.Start.X;
        var y = path.Start.Y;
        var x1 = path.ControlBounds.Right;
        var span = x1 - x0;
        var position = x1 - _stage.Distance * 0.5 % (span + 20);
        var from = Math.Max(x0, position - 14);
        var to = Math.Min(x1, position);
        if (to <= from)
        {
            return;
        }

        var matrix = new Matrix(1, 0, 0, 1, 0, motion.Hover / 2);
        matrix.Append(sprung);
        Use(dc, matrix);
        dc.DrawLine(_ink.Fade(_hot, 0.9 * fx.Hover, _w0 * 0.7, new Point(position, y), new Point(position - 14, y)),
            new Point(from, y), new Point(to, y));
    }

    /// <summary>Stroke opacity of a role (crisp) or its glow share (glow source), times a part factor.</summary>
    private double Alpha(RoleStyle role, PartRole kind, double factor, bool glow)
    {
        double opacity;
        if (glow)
        {
            opacity = Math.Min(1, role.Glow);
        }
        else
        {
            opacity = role.Opacity;
            if (_stage.Options.IncreasedContrast && kind is PartRole.Detail or PartRole.Window or PartRole.Rim or PartRole.Spoke)
            {
                opacity = Math.Max(opacity, 0.9);
            }
        }

        return Math.Clamp(opacity * factor, 0, 1);
    }

    /// <summary>A pen in the idle light band's brush (cached per width for the current band), or null without a band.</summary>
    private Pen? BandPen(Brush? band, double width)
    {
        if (band is null)
        {
            return null;
        }

        var key = (int)Math.Round(width * 100);
        if (!_bandPens.TryGetValue(key, out var pen))
        {
            pen = Freeze(new Pen(band, key / 100.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
            _bandPens[key] = pen;
        }

        return pen;
    }
}
