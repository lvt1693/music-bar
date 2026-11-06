//! name: Lightstick Ocean
//! author: MusicBar
//! kind: visualizer
//! description: Rows of concert lightsticks sway in a wave across the crowd and jump and flare together on the beat.

float hash1(float n)
{
    n = frac(n * 0.1031);
    n *= n + 33.33;
    n *= n + n;
    return frac(n);
}

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

// One lightstick held up at the hand, swaying on the crowd's wave, its tip glowing.
void stick(inout float3 acc, inout float cover, float2 px, float j, float seed, float spacing,
           float handY, float len, float tipR, float bright)
{
    float r1 = hash1(j * 1.31 + seed);
    float r2 = hash1(j * 2.77 + seed * 1.7);
    float hx = (j + 0.5 + 0.3 * (r1 - 0.5)) * spacing;
    float sway = (0.2 + 0.3 * level) * sin(time * 2.6 - hx * 0.012 + r1 * 0.9);
    float2 hand = float2(hx, handY + beat * 2.2 * (size.y / 40.0));
    float2 tip = hand + float2(sin(sway), cos(sway)) * len;

    float2 pa = px - hand;
    float2 ba = tip - hand;
    float t = saturate(dot(pa, ba) / dot(ba, ba));
    float dStick = length(pa - ba * t);
    float dTip = length(px - tip);

    float3 c = r2 < 0.8 ? colorA.rgb : (r2 < 0.93 ? colorB.rgb : colorC.rgb);
    layer(acc, cover, c, smoothstep(1.0, 0.25, dStick) * (0.15 + 0.4 * t) * bright);
    layer(acc, cover, c, exp(-dTip / (tipR * 2.1)) * (0.4 + 0.4 * beat) * bright);
    layer(acc, cover, lerp(c, float3(1, 1, 1), 0.55 + 0.3 * beat), smoothstep(tipR + 0.6, tipR - 0.6, dTip) * bright);
}

// A row of the crowd: the nearest two sticks to this pixel (one is enough for the small far rows).
void crowd(inout float3 acc, inout float cover, float2 px, float seed, float spacing, float handY, float len, float tipR, float bright, bool both)
{
    float j = floor(px.x / spacing);
    if (both)
        stick(acc, cover, px, j + (frac(px.x / spacing) > 0.5 ? 1.0 : -1.0), seed, spacing, handY, len, tipR, bright);
    stick(acc, cover, px, j, seed, spacing, handY, len, tipR, bright);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;
    float s = H / 40.0;
    float sp = 0.55 + 0.6 * spectrum(abs(uv.x - 0.5) * 2.0);

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // The glow of thousands of sticks hanging over the crowd.
    layer(acc, cover, colorA.rgb, exp(-px.y / (H * 0.3)) * (0.05 + 0.06 * beat + 0.04 * level));

    // Back rows small and dim, front row big and bright.
    crowd(acc, cover, px, 3.0, 15.0, H * 0.42, H * 0.2, 1.0 * s, 0.5 * sp, false);
    crowd(acc, cover, px, 11.0, 21.0, H * 0.2, H * 0.3, 1.5 * s, 0.75 * sp, true);
    crowd(acc, cover, px, 23.0, 30.0, -H * 0.06, H * 0.46, 2.2 * s, 1.0 * sp, true);

    return paint(acc / max(cover, 0.001), cover);
}
