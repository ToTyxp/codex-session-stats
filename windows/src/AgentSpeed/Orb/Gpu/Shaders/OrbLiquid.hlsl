// The liquid style's volume shader (DESIGN 2a.3): light spill, back wave, body, spherical shade, inner glow, readout scrim,
// energy currents and the new-peak gold band in one pass (mode 0), and the meniscus (mode 1), all evaluated per pixel from
// the surface y(x). The parameters mirror OrbLiquidUniforms.

cbuffer LiquidParams : register(b1)
{
    float4 lq_frame;         // center x, center y, S, glass radius (points)
    float4 lq_surface;       // mean level y_L, wave amplitude, chop amplitude, back wave on
    float4 lq_phases;        // φ1, φ2, φ3, φ4 (radians)
    float4 lq_ripples[4];    // impact x, front 0.42 S τ, amplitude (points), active
    float4 lq_liquid;        // L (rgb), front alpha
    float4 lq_deep;          // darkened L (rgb), alpha at the bottom
    float4 lq_back;          // back wave color, alpha
    float4 lq_spill;         // spill color, alpha at the surface (0: none)
    float4 lq_glow;          // inner glow color, alpha at its center (0: none)
    float4 lq_glow_geom;     // center x, center y, radius, additive
    float4 lq_shade;         // spherical shade color, alpha at the glass
    float4 lq_current;       // filament color, alpha (0: none)
    float4 lq_current_geom;  // unused, blur σ (points), drift phase, additive
    float4 lq_meniscus;      // color, alpha
    float4 lq_meniscus_glow; // color, alpha (0: none)
    float4 lq_meniscus_geom; // width (points), glow σ (points), additive, unused
    float4 lq_gold;          // band center y, alpha, half height (points), additive
    float4 lq_flags;         // spill additive, scrim strength (0: none), body gradient top y, body gradient bottom y
};

float orb_liquid_wave_y(float x, bool back)
{
    float s = lq_frame.z;
    float xr = x - lq_frame.x;
    float offset = back ? 1.7 : 0.0;
    float a = lq_surface.y * (back ? 0.75 : 1.0);
    float y = lq_surface.x - (back ? 0.008 * s : 0.0);
    if (a > 0.0)
    {
        y += a * (0.55 * sin(xr / (0.9 * s) * orb_tau + lq_phases.x + offset)
                  + 0.30 * sin(xr / (0.47 * s) * orb_tau - lq_phases.y + offset * 1.3)
                  + 0.15 * sin(xr / (0.23 * s) * orb_tau + lq_phases.z + offset * 0.7));
    }

    if (lq_surface.z > 0.0)
    {
        y += lq_surface.z * sin(xr / (0.11 * s) * orb_tau + lq_phases.w + offset)
            * (0.7 + 0.3 * sin(xr / (0.05 * s) + lq_phases.w * 1.7));
    }

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 ripple = lq_ripples[i];
        if (ripple.w > 0.0)
        {
            float u = abs(x - ripple.x) - ripple.y;
            float k = u / (0.075 * s);
            y -= ripple.z * exp(-k * k) * cos(orb_tau * u / (0.1 * s)) * (back ? 0.6 : 1.0);
        }
    }

    return y;
}

// The liquid surface height (points, y down) at canvas x; used by the volume and to split the readout.
float orb_liquid_surface_y(float x)
{
    return orb_liquid_wave_y(x, false);
}

struct LiquidOut
{
    float4 position : SV_Position;
    float2 canvas : TEXCOORD0;
};

LiquidOut VsLiquid(uint vid : SV_VertexID)
{
    float extent = lq_frame.w + 2.0;
    float2 p = lq_frame.xy + corners[vid] * extent;
    LiquidOut output;
    output.position = orb_clip_position(p);
    output.canvas = p;
    return output;
}

void orb_liquid_over(inout float3 color, inout float alpha, float3 rgb, float a)
{
    a = saturate(a);
    color = rgb * a + color * (1.0 - a);
    alpha = a + alpha * (1.0 - a);
}

// Light adds up on dark glass (color adds, coverage accumulates) and blends normally on porcelain.
void orb_liquid_light(inout float3 color, inout float alpha, float3 rgb, float a, bool additive)
{
    if (additive)
    {
        a = saturate(a);
        color += rgb * a;
        alpha = a + alpha * (1.0 - a);
    }
    else
    {
        orb_liquid_over(color, alpha, rgb, a);
    }
}

float4 PsLiquid(LiquidOut input) : SV_Target
{
    float2 p = input.canvas;
    float2 c = lq_frame.xy;
    float s = lq_frame.z;
    float radius = lq_frame.w;
    float aa = 1.0 / viewport.z;
    float glass = saturate((radius - length(p - c)) / aa + 0.5);
    if (glass <= 0.0)
    {
        discard;
    }

    float3 color = float3(0.0, 0.0, 0.0);
    float alpha = 0.0;
    float y_surface = orb_liquid_wave_y(p.x, false);

    if (pass_mode.x == 1)
    {
        // The meniscus: the surface stroked, with its glow.
        float slope = orb_liquid_wave_y(p.x + 0.5, false) - orb_liquid_wave_y(p.x - 0.5, false);
        float d = abs(p.y - y_surface) / sqrt(1.0 + slope * slope);
        float half_width = lq_meniscus_geom.x * 0.5;
        if (lq_meniscus_glow.a > 0.0)
        {
            float sigma = max(lq_meniscus_geom.y, 0.001);
            float x = max(d - half_width, 0.0) / sigma;
            orb_liquid_light(color, alpha, lq_meniscus_glow.rgb, lq_meniscus_glow.a * exp(-0.5 * x * x), lq_meniscus_geom.z > 0.5);
        }

        float core = saturate((half_width - d) / aa + 0.5);
        orb_liquid_over(color, alpha, lq_meniscus.rgb, lq_meniscus.a * core);
        color *= glass;
        alpha *= glass;
        if (alpha <= 0.0005 && max(color.r, max(color.g, color.b)) <= 0.0005)
        {
            discard;
        }

        return orb_output(color, alpha);
    }

    float y_level = lq_surface.x;
    // Light spilling up into the glass from the surface.
    if (lq_spill.a > 0.0)
    {
        float t = (p.y - (y_level - 0.22 * s)) / (0.22 * s);
        if (t >= 0.0 && t <= 1.0)
        {
            orb_liquid_light(color, alpha, lq_spill.rgb, lq_spill.a * t, lq_flags.x > 0.5);
        }
    }

    // Back wave.
    if (lq_surface.w > 0.5)
    {
        float y_back = orb_liquid_wave_y(p.x, true);
        float back_cover = saturate((p.y - y_back) / aa + 0.5);
        orb_liquid_over(color, alpha, lq_back.rgb, lq_back.a * back_cover);
    }

    // Liquid body, spherical shade and inner glow under the front surface.
    float cover = saturate((p.y - y_surface) / aa + 0.5);
    if (cover > 0.0)
    {
        float t = saturate((p.y - lq_flags.z) / max(lq_flags.w - lq_flags.z, 1.0));
        orb_liquid_over(color, alpha, lerp(lq_liquid.rgb, lq_deep.rgb, t), lerp(lq_liquid.a, lq_deep.a, t) * cover);
        float2 shade_center = c + float2(-0.06, -0.08) * s;
        float inner = 0.35 * radius;
        float shade = saturate((length(p - shade_center) - inner) / max(radius - inner, 0.001));
        orb_liquid_over(color, alpha, lq_shade.rgb, lq_shade.a * shade * cover);
        if (lq_glow.a > 0.0)
        {
            float d = length(p - lq_glow_geom.xy) / max(lq_glow_geom.z, 0.001);
            orb_liquid_light(color, alpha, lq_glow.rgb, lq_glow.a * max(0.0, 1.0 - d) * cover, lq_glow_geom.w > 0.5);
        }
    }

    // Readout scrim (dark): an elliptical darkening behind the readout.
    if (lq_flags.y > 0.0)
    {
        float k = length((p - (c + float2(0.0, 0.1 * s))) / float2(0.36 * s, 0.26 * s));
        if (k < 1.0)
        {
            float a = k < 0.55 ? lerp(0.35, 0.30, k / 0.55) : lerp(0.30, 0.0, (k - 0.55) / 0.45);
            orb_liquid_over(color, alpha, float3(0.0, 0.0, 0.0), a * lq_flags.y);
        }
    }

    // Energy currents: three blurred filaments drifting inside the liquid.
    if (lq_current.a > 0.0 && cover > 0.0)
    {
        float bottom = c.y + radius;
        float u = (p.x - (c.x - radius)) / (2.0 * radius);
        float sigma = max(lq_current_geom.y, 0.001);
        [unroll]
        for (int k = 0; k < 3; k++)
        {
            float depth = 0.25 + 0.25 * float(k);
            float phase = lq_current_geom.z * (0.35 + 0.2 * float(k)) + 2.1 * float(k);
            float y = y_level + (bottom - y_level) * depth + 0.03 * s * sin(u * 7.0 + phase) + 0.015 * s * sin(u * 13.0 - phase * 1.7);
            float slope = (0.03 * s * 7.0 * cos(u * 7.0 + phase) + 0.015 * s * 13.0 * cos(u * 13.0 - phase * 1.7)) / (2.0 * radius);
            float d = abs(p.y - y) / sqrt(1.0 + slope * slope);
            float half_width = (0.018 - 0.004 * float(k)) * s * 0.5;
            float profile = 1.0 - smoothstep(max(half_width - 1.5 * sigma, 0.0), half_width + 1.5 * sigma, d);
            orb_liquid_light(color, alpha, lq_current.rgb, lq_current.a * (1.0 - 0.25 * float(k)) * profile * cover, lq_current_geom.w > 0.5);
        }
    }

    // New-peak gold band rising through the liquid.
    if (lq_gold.y > 0.0)
    {
        float d = abs(p.y - lq_gold.x) / max(lq_gold.z, 0.001);
        if (d < 1.0)
        {
            orb_liquid_light(color, alpha, float3(250.0, 204.0, 21.0) / 255.0, lq_gold.y * (1.0 - d), lq_gold.w > 0.5);
        }
    }

    color *= glass;
    alpha *= glass;
    if (alpha <= 0.0005 && max(color.r, max(color.g, color.b)) <= 0.0005)
    {
        discard;
    }

    return orb_output(color, alpha);
}
