// The led style's per-pixel layers (DESIGN 2.1), ported from spec/prototypes/led-effects.html: mode 0 the corona and warp
// streaks outside the rim (behind the body), mode 1 the silk core (over the body), mode 2 the tip flare and the new-peak
// flash, gold ring and rays (over the gauge). Coordinates are the prototype's: orb units with the rim at r = 0.5, y up.
// The parameters mirror OrbLedUniforms; colors compose in the pass like the prototype's acc (glow adds light in dark and
// lays on like ink on porcelain) and leave premultiplied.

cbuffer LedParams : register(b3)
{
    float4 led_frame; // center x, center y (points), S (points), alpha
    float4 led_heat;  // h, o, act, lvl
    float4 led_look;  // dark, silk brightness, ripple, waves move the cloth
    float4 led_silk;  // silk fold phase, silk noise z, light phase, clock
    float4 led_halo;  // corona phase, warp phase, corona amount, warp amount
    float4 led_peak;  // seconds since the new peak (>= 1.4 none), tier flash, tip flare amount, motion (rays, growing ring)
    float4 led_tip;   // tip angle (radians, clockwise on screen), value box y, unit box y, unused
    float4 led_waves[4];
    float4 led_quads[3];
};

static const float3 led_white_hot = float3(1.0, 0.969, 0.839);
static const int led_ring_wedges = 128;

struct LedOut
{
    float4 position : SV_Position;
    float2 canvas : TEXCOORD0;
    float body : TEXCOORD1;
};

float led_smooth(float e0, float e1, float x)
{
    float t = saturate((x - e0) / (e1 - e0));
    return t * t * (3.0 - 2.0 * t);
}

float led_ease_out(float t)
{
    float u = 1.0 - saturate(t);
    return 1.0 - u * u * u;
}

float3 led_heat_color(float h)
{
    float u = saturate(h) * (255.0 / 256.0) + 0.5 / 256.0;
    return palette.SampleLevel(linear_clamp, float2(u, 0.5), 0).rgb;
}

// Porcelain: deepen without muddying (gold stays gold, not khaki).
float3 led_ink(float3 c)
{
    return pow(abs(c), 1.55);
}

// 3D simplex noise: Ashima Arts / Stefan Gustavson (MIT).
float3 led_mod289(float3 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 led_mod289(float4 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 led_permute(float4 x)
{
    return led_mod289((x * 34.0 + 1.0) * x);
}

float4 led_inv_sqrt(float4 r)
{
    return 1.79284291400159 - 0.85373472095314 * r;
}

float led_noise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);
    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;
    i = led_mod289(i);
    float4 p = led_permute(led_permute(led_permute(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0))
        + i.x + float4(0.0, i1.x, i2.x, 1.0));
    float n_ = 0.142857142857;
    float3 ns = n_ * D.wyz - D.xzx;
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);
    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);
    float4 x = x_ * ns.x + ns.yyyy;
    float4 y = y_ * ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);
    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));
    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
    float3 p0 = float3(a0.xy, h.x);
    float3 p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z);
    float3 p3 = float3(a1.zw, h.w);
    float4 norm = led_inv_sqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;
    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

float led_hash11(float n)
{
    return frac(sin(n * 127.1) * 43758.5453);
}

// Dark glass: light adds up; porcelain: colors are laid on like ink.
void led_glow(inout float4 acc, float3 c, float a, bool dark)
{
    a = saturate(a);
    if (dark)
    {
        acc.rgb += c * a;
        acc.a = max(acc.a, a * 0.35);
    }
    else
    {
        acc.rgb = c * a + acc.rgb * (1.0 - a);
        acc.a = a + acc.a * (1.0 - a);
    }
}

void led_paint(inout float4 acc, float3 c, float a)
{
    a = saturate(a);
    acc.rgb = c * a + acc.rgb * (1.0 - a);
    acc.a = a + acc.a * (1.0 - a);
}

float led_box(float2 p, float2 b, float radius)
{
    float2 d = abs(p) - b + radius;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

float led_fold(float phase)
{
    return sin(phase + 0.25 * sin(phase));
}

float led_bell(float x)
{
    return exp(-x * x);
}

// The satin height field: long diagonal folds, gently fanned and warped by one simplex noise.
float led_silk_height(float2 p, float t, float rip, float wave)
{
    float clock = led_silk.w;
    float X = dot(p, float2(0.80, 0.60));
    float Y = dot(p, float2(-0.60, 0.80)) * (1.0 + 0.45 * X)
        + 0.03 * led_noise(float3(p * 1.1, led_silk.y)) + 0.025 * sin(X * 5.0 + t * 0.3) + 0.03 * sin(X * 1.6 - t * 0.17);
    float height = 0.5 * sin(Y * 6.0 + 0.25 * sin(X * 3.3 + t * 0.2) + t * 0.15)
        + 0.6 * (0.7 + 0.3 * sin(X * 2.8 + 1.0 + t * 0.1)) * led_fold(Y * 16.5 + 0.35 * sin(X * 4.1 - t * 0.35) + t * 0.6)
        + 0.22 * (0.5 + 0.5 * sin(X * 3.6 - t * 0.25 + 2.0)) * led_fold(Y * 27.0 + 0.4 * sin(X * 4.7 + t * 0.4) - t * 0.8 + 2.0);
    if (rip > 0.0)
    {
        float packet = max(sin(Y * 4.0 - clock * 2.6 + 1.3 * sin(X * 2.7)), 0.0);
        height += rip * 0.05 * packet * packet * sin(Y * 44.0 + X * 6.0 - clock * 16.0);
    }

    height += wave * 0.11 * sin(length(p) * 36.0 - clock * 8.0);
    return height;
}

float led_wave_field(float r)
{
    float field = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 w = led_waves[i];
        if (w.w > 0.5)
        {
            float rr = 0.1 + 0.4 * led_ease_out(w.x);
            float k = (r - rr) / 0.05;
            field += w.y * (1.0 - w.x) * exp(-k * k);
        }
    }

    return field;
}

// Silk core: a key light and a rim light in the speed palette glide over the satin.
void led_silk_core(inout float4 acc, float2 p, float r, bool dark)
{
    float fade = led_smooth(0.475, 0.385, r);
    if (fade <= 0.0)
    {
        return;
    }

    float h = led_heat.x;
    float o = led_heat.y;
    float act = led_heat.z;
    float t = led_silk.x;
    float light = led_silk.z;
    float rip = led_look.z;
    float wave_field = led_look.w > 0.5 ? led_wave_field(r) : 0.0;
    float2 sp = p - p / max(r, 1e-3) * 0.03 * wave_field;
    const float e = 0.008;
    const float2 ac = float2(-0.6, 0.8);
    const float2 al = float2(0.8, 0.6);
    float h0 = led_silk_height(sp, t, rip, wave_field);
    float hA = led_silk_height(sp + ac * e, t, rip, wave_field);
    float hB = led_silk_height(sp - ac * e, t, rip, wave_field);
    float hL = led_silk_height(sp + al * e, t, rip, wave_field);
    float2 g = ac * (hA - hB) / (2.0 * e) + al * (hL - h0) / e;
    float3 n = normalize(float3(-g * 0.05, 1.0));
    float crest = led_smooth(-0.15, 0.75, -(hA - 2.0 * h0 + hB) / (e * e) * 0.006);

    float tx = min(led_box(p - float2(0.0, led_tip.y), float2(0.27, 0.07), 0.07), led_box(p - float2(0.0, led_tip.z), float2(0.13, 0.06), 0.06));
    float text_mask = lerp(0.3, 1.0, led_smooth(-0.02, 0.1, tx));

    float az = 2.2 + 0.3 * sin(light * 0.35);
    float2 ld = float2(cos(az), sin(az));
    float sl = dot(n.xy, ld);
    float ao = 0.2 + 0.8 * led_smooth(-1.6, 1.1, h0);
    float body = pow(abs(led_smooth(-0.12, 0.5, sl)), 1.6) * ao;
    float sheen = led_bell((sl - 0.3) / 0.11) * ao;
    float glint = led_bell((sl - 0.33) / 0.045) * (0.35 + 0.65 * crest) * ao;
    float rim = led_bell((sl + 0.42) / 0.1) * (0.4 + 0.6 * ao);

    float X = dot(p, float2(0.8, 0.6));
    float peak = led_peak.x;
    float pk = peak < 1.4 ? 1.0 - led_smooth(0.7, 1.4, peak) : 0.0;
    float sweep = pk * led_bell((X - lerp(-0.55, 0.6, led_ease_out(peak / 1.2))) / 0.13);
    float amp = led_look.y * (1.0 + 0.3 * led_peak.y + 0.5 * wave_field + 2.2 * sweep + 0.4 * pk);
    float k = fade * text_mask * amp;

    float glide = 0.05 * sin(X * 3.2 - light * 0.8);
    float spread = lerp(0.08, 1.0, act);
    float hot_k = saturate(led_smooth(0.0, 0.3, o) + sweep);
    const float3 gold = float3(1.0, 0.84, 0.45);
    float3 c_body = led_heat_color(h + (-0.22 + 0.3 * led_smooth(-0.1, 0.5, sl) + glide) * spread);
    float3 c_sheen0 = led_heat_color(h + (0.06 + glide) * spread);
    float3 c_sheen = lerp(c_sheen0, lerp(gold, led_white_hot, 0.35), hot_k);
    float3 c_glint = lerp(c_sheen, led_white_hot, 0.55 + 0.3 * hot_k);
    float3 c_rim = led_heat_color(h + (-0.32 - glide) * spread);

    if (dark)
    {
        led_paint(acc, float3(0.008, 0.01, 0.016), 0.7 * fade);
        led_glow(acc, c_body, body * 0.32 * k, true);
        led_glow(acc, c_sheen, sheen * 0.75 * k, true);
        led_glow(acc, c_glint, glint * 0.7 * k, true);
        led_glow(acc, c_rim, rim * 0.35 * k, true);
        led_glow(acc, lerp(gold, led_white_hot, 0.4), sweep * (0.12 + 0.6 * body + 0.8 * sheen) * fade * text_mask, true);
    }
    else
    {
        float tm = 0.4 + 0.6 * text_mask;
        led_paint(acc, float3(0.99, 0.985, 0.978), 0.55 * fade);
        led_paint(acc, float3(0.66, 0.67, 0.72), (pow(abs(led_smooth(0.15, -0.45, sl)), 1.3) * 0.38 + (1.0 - ao) * 0.12) * fade * tm);
        led_glow(acc, led_ink(c_rim), rim * 0.22 * k, false);
        led_glow(acc, lerp(led_ink(c_sheen0), float3(0.96, 0.66, 0.16), hot_k), sheen * (0.3 + 0.15 * hot_k) * k, false);
        led_paint(acc, float3(1.0, 1.0, 1.0), (glint * 0.75 + sheen * 0.35) * fade * tm);
        led_glow(acc, led_ink(gold), sweep * (0.1 + 0.4 * body) * fade * tm, false);
    }
}

// Corona (flame tongues licking off the rim, fading out by 0.74 S) and warp streaks rushing into the orb. The corona's slow
// body noise comes from the ring's vertices, so a pixel evaluates three noises.
void led_halo_layers(inout float4 acc, float2 p, float r, float body, bool dark)
{
    float h = led_heat.x;
    float o = led_heat.y;
    float3 hc = led_heat_color(h);
    float corona = led_halo.z;
    if (corona > 0.001 && r > 0.47 && r < 0.74)
    {
        float2 dir = p / max(r, 1e-4);
        float rise = (r - 0.5) * 9.0 - led_halo.x;
        float warp = led_noise(float3(dir * 3.0, rise * 0.5));
        float ridge = 1.0 - abs(led_noise(float3(dir * 7.0 + warp * 0.35, rise)));
        ridge = pow(abs(saturate(ridge)), 5.0);
        float reach = 0.02 + (0.06 * h + 0.12 * o) * pow(abs(saturate(body)), 1.4) * 1.7;
        float d = (r - 0.499) / reach;
        float fall = exp(-d * 2.4) * led_smooth(0.49, 0.503, r) * led_smooth(0.74, 0.64, r);
        float tongues = led_smooth(0.45, 0.8, 0.5 + 0.5 * led_noise(float3(dir * 4.5, rise * 0.35)));
        float flame = ridge * fall * lerp(0.12, 1.25, tongues) * (0.45 + 0.7 * body);
        float base = exp(-max(r - 0.5, 0.0) / 0.012) * led_smooth(0.49, 0.503, r) * 0.35;
        float3 fc = lerp(led_white_hot, hc, saturate(d * 0.9));
        led_glow(acc, dark ? fc : led_ink(hc), (flame + base) * corona * 0.95, dark);
    }

    float warp_amount = led_halo.w;
    if (warp_amount > 0.001 && r > 0.5 && r < 0.74)
    {
        float ang = degrees(atan2(-p.y, p.x));
        if (ang < 0.0)
        {
            ang += 360.0;
        }

        const float bins = 96.0;
        float bin = floor(ang / 360.0 * bins);
        float seed = led_hash11(bin + 3.0);
        if (seed < 0.18 + 0.42 * h)
        {
            float speed = 0.55 + seed * 0.9;
            float head = frac(seed * 7.3 + led_halo.y * speed);
            float rh = lerp(0.74, 0.51, head);
            float len = (0.02 + 0.08 * h + 0.07 * o) * (0.55 + seed);
            float along = (r - rh) / len;
            float across = radians(abs(frac(ang / 360.0 * bins) - 0.5) * 360.0 / bins) * r;
            float streak = led_smooth(0.0018 + 0.001 * h, 0.0, across) * pow(abs(led_smooth(1.0, 0.0, along)), 1.6) * step(0.0, along);
            float fade = led_smooth(0.0, 0.3, head) * led_smooth(1.0, 0.82, head);
            float3 wc = lerp(hc, led_white_hot, 0.45);
            led_glow(acc, dark ? wc : led_ink(hc), streak * fade * warp_amount * (dark ? 0.8 : 0.75), dark);
        }
    }
}

// The four-pointed star on the leading segment, and a new peak's disc flash, gold ring and 24 rays.
void led_flare_layers(inout float4 acc, float2 p, float r, bool dark)
{
    float h = led_heat.x;
    float o = led_heat.y;
    float3 hc = led_heat_color(h);
    float flare = led_peak.z;
    if (flare > 0.001)
    {
        float ta = led_tip.x;
        float2 tp = float2(cos(ta), -sin(ta)) * 0.41;
        float2 d = p - tp;
        float2 tang = float2(-sin(ta), -cos(ta));
        float2 rad = normalize(tp);
        float star = exp(-abs(dot(d, tang)) / (0.05 + 0.16 * o)) * exp(-abs(dot(d, rad)) / 0.0032)
            + exp(-abs(dot(d, rad)) / (0.035 + 0.09 * o)) * exp(-abs(dot(d, tang)) / 0.0028);
        led_glow(acc, dark ? led_white_hot : led_ink(hc), star * flare * (dark ? 0.9 : 0.4), dark);
    }

    float age = led_peak.x;
    if (age < 1.4)
    {
        bool motion = led_peak.w > 0.5;
        float f = exp(-age * 4.5);
        led_glow(acc, dark ? led_white_hot : float3(1.0, 0.69, 0.13), f * (dark ? 0.75 : 0.35) * led_smooth(0.6, 0.15, r), dark);
        float ring_r = motion ? 0.5 + 0.24 * led_ease_out(age / 1.4) : 0.54;
        float rk = (r - ring_r) / (0.012 + 0.02 * age);
        led_glow(acc, float3(1.0, 0.78, 0.3), exp(-rk * rk) * (1.0 - age / 1.4), dark);
        if (motion && age < 1.3 && r > 0.49)
        {
            float k = age / 1.3;
            float aa = 1.0 / (led_frame.z * viewport.z);
            float ang = atan2(-p.y, p.x) - age * 0.4;
            float slot = 6.28318530718 / 24.0;
            float rel = ang - slot * round(ang / slot);
            float across = abs(sin(rel)) * r;
            float along = cos(rel) * r;
            float r1 = 0.55 + 0.19 * led_ease_out(min(1.0, k * 1.4));
            float half_width = 0.007 * (1.0 - k * 0.5);
            float cover = saturate(0.5 + (half_width - across) / aa) * saturate(0.5 + (along - 0.5) / aa) * saturate(0.5 + (r1 - along) / aa);
            float u = saturate((along - 0.5) / max(r1 - 0.5, 1e-4));
            float a = u < 0.3 ? u / 0.3 : 1.0 - (u - 0.3) / 0.7;
            float3 c = u < 0.3 ? lerp(float3(1.0, 0.85, 0.35), float3(1.0, 0.8, 0.3), u / 0.3) : lerp(float3(1.0, 0.8, 0.3), float3(1.0, 0.7, 0.2), (u - 0.3) / 0.7);
            led_glow(acc, c, cover * a * 0.75 * (1.0 - k), dark);
        }
    }
}

LedOut VsLed(uint vid : SV_VertexID)
{
    float s = led_frame.z;
    float2 center = led_frame.xy;
    float2 corner = corners[vid % 6];
    float2 p;
    float body = 0.0;
    if (pass_mode.x == 0)
    {
        // The corona ring: wedge vid / 6 between two angles (prototype radians, y up), from inside 0.47 S to past 0.74 S.
        float step_angle = 6.28318530718 / led_ring_wedges;
        float theta = (float(vid / 6) + corner.x * 0.5 + 0.5) * step_angle;
        float radius = corner.y < 0.0 ? 0.46 * s : led_quads[0].x / cos(step_angle * 0.5) + 2.0 / viewport.z;
        float2 dir = float2(cos(theta), sin(theta));
        p = center + float2(dir.x, -dir.y) * radius;
        body = 0.5 + 0.5 * led_noise(float3(dir * 2.2, led_halo.x * 0.35));
    }
    else
    {
        float4 quad = led_quads[pass_mode.x];
        p = lerp(quad.xy, quad.zw, corner * 0.5 + 0.5);
    }

    LedOut output;
    output.position = orb_clip_position(p);
    output.canvas = p;
    output.body = body;
    return output;
}

float4 PsLed(LedOut input) : SV_Target
{
    float s = led_frame.z;
    float2 p = float2(input.canvas.x - led_frame.x, led_frame.y - input.canvas.y) / s;
    float r = length(p);
    bool dark = led_look.x > 0.5;
    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    if (pass_mode.x == 0)
    {
        led_halo_layers(acc, p, r, input.body, dark);
    }
    else if (pass_mode.x == 1)
    {
        led_silk_core(acc, p, r, dark);
    }
    else
    {
        led_flare_layers(acc, p, r, dark);
    }

    float alpha = led_frame.w;
    if (max(acc.a, max(acc.r, max(acc.g, acc.b))) * alpha <= 0.0005)
    {
        discard;
    }

    return orb_output(acc.rgb * alpha, saturate(acc.a) * alpha);
}
