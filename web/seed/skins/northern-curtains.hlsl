//! name: Northern Curtains
//! author: MusicBar
//! kind: visualizer
//! description: Folded aurora curtains ripple along the bar, rising and brightening with the spectrum.

float hash1(float n)
{
    n = frac(n * 0.1031);
    n *= n + 33.33;
    n *= n + n;
    return frac(n);
}

float hash2(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// Smooth random values along a line.
float noise1(float x)
{
    float i = floor(x);
    float f = frac(x);
    return lerp(hash1(i), hash1(i + 1.0), f * f * (3.0 - 2.0 * f));
}

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

// One curtain: a wavy bright hem near the screen edge, rays above it, as tall as the music it stands on.
float4 curtain(float2 px, float H, float seed, float drift)
{
    float xs = px.x + 40.0 * sin(px.x * 0.0035 + time * 0.22 + seed);
    float hem = H * (0.1 + 0.06 * sin(xs * 0.011 + time * 0.4 + seed) + 0.035 * sin(xs * 0.029 - time * 0.7));
    float sp = spectrum(abs(xs / size.x - 0.5) * 2.0);
    float tall = H * (0.3 + 0.75 * sp) * (0.9 + 0.2 * beat);
    float t = (px.y - hem) / tall;
    float body = smoothstep(-0.1, 0.05, t) * pow(saturate(1.0 - t), 1.7) * (1.0 + 0.8 * exp(-max(t, 0.0) * 10.0));
    float rays = 0.35 + 0.65 * pow(saturate(noise1(xs * 0.09 + time * drift + seed) * 0.6 + noise1(xs * 0.31 - time * drift * 1.8 + seed * 2.0) * 0.4), 1.4);
    float folds = 0.25 + 0.75 * smoothstep(0.2, 0.8, noise1(xs * 0.0065 + time * 0.05 + seed * 3.0));
    float3 c = lerp(colorA.rgb, colorB.rgb, saturate(t * 1.4));
    c = lerp(c, colorC.rgb, smoothstep(0.5, 1.0, t));
    return float4(c, body * rays * folds);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // A few faint stars in the dark above.
    float2 sc = floor(px / 6.0);
    float star = step(0.975, hash2(sc)) * smoothstep(1.2, 0.3, length(frac(px / 6.0) * 6.0 - 3.0));
    layer(acc, cover, float3(1, 1, 1), star * (0.25 + 0.2 * sin(time * 1.7 + hash2(sc + 2.0) * 20.0)) * smoothstep(0.35, 0.9, px.y / H));

    // A dimmer curtain behind, the main one in front.
    float4 back = curtain(px + float2(310.0, 0.0), H * 0.85, 4.0, 0.35);
    layer(acc, cover, lerp(back.rgb, colorB.rgb, 0.4), back.a * (0.3 + 0.25 * level));
    float4 front = curtain(px, H, 0.0, 0.5);
    layer(acc, cover, front.rgb * (1.0 + 0.25 * beat), front.a * (0.5 + 0.45 * level));

    return paint(acc / max(cover, 0.001), cover);
}
