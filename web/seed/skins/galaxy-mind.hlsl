//! name: Galaxy Mind
//! author: MusicBar
//! kind: visualizer
//! description: A cosmic glow that grows more enlightened the louder it gets, from faint nebula to stars, light rays and a blazing core.

float hash2(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// Smooth random clouds.
float noise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), f.x), lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), f.x), f.y);
}

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;
    float dx = px.x - size.x * 0.5;

    // How enlightened: each stage fades in as the music gets louder.
    float e = saturate(level * 0.85 + 0.15 * beat);
    float nebulaOn = smoothstep(0.0, 0.3, e);
    float starsOn = smoothstep(0.2, 0.55, e);
    float raysOn = smoothstep(0.45, 0.8, e);
    float blaze = smoothstep(0.72, 1.0, e);
    float reach = 220.0 + size.x * 0.5 * pow(e, 1.3);

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // Nebula: slow clouds in the palette, spreading out from the middle as the mind expands.
    float n = noise2(float2(px.x / 95.0 + time * 0.04, px.y / 24.0)) * 0.65 + noise2(float2(px.x / 38.0 - time * 0.07, px.y / 12.0 + 3.0)) * 0.35;
    float spread = exp(-abs(dx) / reach);
    float3 cloud = lerp(colorB.rgb, colorA.rgb, smoothstep(0.3, 0.8, n));
    layer(acc, cover, cloud, smoothstep(0.35, 0.85, n) * spread * (0.15 + 0.3 * nebulaOn));

    // Stars: a few always, many more once the mind opens up.
    float2 sc = floor(px / 6.0);
    float r = hash2(sc);
    float starHere = step(0.955 - 0.05 * starsOn, r) * smoothstep(1.2, 0.2, length(frac(px / 6.0) * 6.0 - 3.0));
    float twinkle = 0.5 + 0.5 * sin(time * (1.5 + 3.0 * hash2(sc + 5.0)) + r * 40.0);
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.6), starHere * twinkle * (0.25 + 0.6 * starsOn) * (0.4 + 0.6 * spread));

    // Rays fanning out from a point just past the screen edge.
    float2 from = float2(dx, px.y + H * 0.7);
    float angle = atan2(from.x, from.y);
    float rays = pow(saturate(0.5 + 0.5 * sin(angle * 46.0 + time * 0.25 + 2.0 * sin(angle * 7.0 - time * 0.2))), 7.0);
    float rayFade = exp(-length(from * float2(1.0, 4.0)) / (reach * 0.9));
    layer(acc, cover, lerp(colorA.rgb, colorC.rgb, 0.5), rays * rayFade * raysOn * 0.55);

    // The core: a glow along the edge, wider and whiter the louder it gets.
    float2 core = float2(dx / (40.0 + reach * 0.9), (px.y + 2.0) / (H * (0.34 + 0.36 * e)));
    float glow = exp(-length(core) * 1.6);
    layer(acc, cover, lerp(colorC.rgb, float3(1.0, 0.97, 0.9), blaze * 0.7), glow * (0.42 + 0.48 * e));

    // On a beat, a ring of light rushes out from the core.
    float ringR = (1.0 - beat) * reach * 0.9 + 10.0;
    float ringD = length(float2(dx, (px.y + 2.0) * 7.0));
    layer(acc, cover, lerp(colorA.rgb, float3(1, 1, 1), 0.4), exp(-abs(ringD - ringR) / 4.0) * beat * starsOn * 0.6);

    return paint(acc / max(cover, 0.001), cover);
}
