// Shared declarations of the orb shaders (DESIGN 2a, Techniques): an HLSL port of the macOS OrbShaderSource.
// Canvas coordinates are points with y down; every color is straight alpha in, premultiplied out.

cbuffer Uniforms : register(b0)
{
    float4 viewport;  // width, height (points), scale (pixels per point), clip radius (points; 0 none; < 0 keep outside)
    float4 transform; // content scale about (y, z) (the canvas center), clip feather (points)
    float4 globals;   // opacity of the whole orb, unused x3
};

cbuffer PassParams : register(b2)
{
    int4 pass_mode;   // x: the mode of a liquid or led pass
};

static const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, -1), float2(1, 1), float2(-1, 1) };
static const float orb_tau = 6.28318530718;

SamplerState linear_clamp : register(s0);

float4 orb_clip_position(float2 p)
{
    float2 center = transform.yz;
    float2 scaled = center + (p - center) * transform.x;
    return float4(scaled.x / viewport.x * 2.0 - 1.0, 1.0 - scaled.y / viewport.y * 2.0, 0, 1);
}

float orb_clip_coverage(float2 canvas)
{
    if (viewport.w == 0.0)
    {
        return 1.0;
    }

    float aa = 1.0 / viewport.z;
    float d = length(canvas - transform.yz) - abs(viewport.w);
    if (viewport.w < 0.0)
    {
        return saturate(0.5 + d / aa);
    }

    if (transform.w > 0.0)
    {
        return saturate(-d / transform.w);
    }

    return saturate(0.5 - d / aa);
}

float4 orb_output(float3 premultiplied, float alpha)
{
    float opacity = globals.x;
    return float4(premultiplied * opacity, alpha * opacity);
}
