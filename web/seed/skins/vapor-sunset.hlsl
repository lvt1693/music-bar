//! name: Vapor Sunset
//! author: MusicBar
//! kind: visualizer
//! description: A striped retro sun sinks into a neon perspective grid, and the bass pumps the sun.

float hash2(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);   // pixels: along the bar, up from the screen edge
    float H = size.y;
    float cx = size.x * 0.5;
    float hy = H * 0.36;                                    // the horizon
    float bass = saturate(spectrum(0.02) * 0.75 + level * 0.3);

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // The sky: a low haze along the horizon, and a few faint stars higher up.
    float sky = step(hy, px.y);
    float haze = exp(-(px.y - hy) / (H * 0.22)) * exp(-abs(px.x - cx) / (size.x * 0.45));
    layer(acc, cover, lerp(colorB.rgb, colorA.rgb, 0.5), sky * haze * (0.16 + 0.12 * bass));
    float2 sc = floor(px / 5.0);
    float twinkle = 0.5 + 0.5 * sin(time * 2.0 + hash2(sc + 1.3) * 30.0);
    float star = step(0.965, hash2(sc)) * smoothstep(1.3, 0.3, length(frac(px / 5.0) * 5.0 - 2.5)) * twinkle;
    layer(acc, cover, float3(1, 1, 1), star * sky * smoothstep(hy + 4.0, H, px.y) * 0.55);

    // The sun: radius pumped by the bass, sliced by slits that widen toward the horizon and drift down.
    float R = H * (0.62 + 0.2 * bass + 0.06 * beat);
    float2 sunAt = float2(cx, hy - R * 0.12);
    float dist = length(px - sunAt);
    float v = saturate((px.y - hy) / (sunAt.y + R - hy));   // 0 at the horizon, 1 at the sun's top
    float slitWidth = lerp(3.2, 0.0, saturate(v / 0.62)) * (H / 40.0);
    float period = 5.0 * (H / 40.0);
    float slit = step(frac((px.y + time * 4.0) / period) * period, slitWidth);
    float disc = smoothstep(R + 0.8, R - 0.8, dist) * sky * (1.0 - slit);
    float3 warm = lerp(colorC.rgb, float3(1.0, 0.92, 0.62), 0.35);
    float3 sunColor = lerp(colorA.rgb, warm, smoothstep(0.0, 1.0, v));
    float halo = exp(-max(dist - R, 0.0) / (R * 0.45)) * step(R, dist) * sky;
    layer(acc, cover, colorA.rgb, halo * (0.22 + 0.25 * bass));
    layer(acc, cover, sunColor * (1.0 + 0.25 * beat), disc * 0.92);

    // The floor: a perspective grid rushing toward you, fogged near the horizon.
    float z = max(hy - px.y, 0.0);
    float floorMask = step(px.y, hy);
    float depth = 16.0 * (H / 40.0) / (z + 1.5);
    float across = frac(depth + time * 0.9);
    float wDepth = fwidth(depth);
    float acrossLine = (1.0 - smoothstep(0.0, wDepth * 1.3, min(across, 1.0 - across))) * saturate(1.4 - wDepth * 2.5);
    float X = (px.x - cx) * 0.1 / (z + 2.5);
    float alongF = abs(frac(X) - 0.5);
    float wX = fwidth(X);
    float alongLine = (1.0 - smoothstep(0.0, wX * 1.3, alongF)) * saturate(1.4 - wX * 2.5);
    float fog = pow(saturate(z / hy), 1.3);
    float grid = saturate(acrossLine * fog + alongLine * (0.3 + 0.7 * fog));
    float gridGlow = (0.42 + 0.22 * level + 0.22 * beat) * floorMask;
    layer(acc, cover, colorB.rgb * (1.0 + 0.3 * beat), grid * gridGlow);

    // The sun's reflection shimmering on the floor.
    float shimmer = step(frac((px.y + time * 6.0) / 3.0), 0.5) * exp(-abs(px.x - cx) / (R * 0.55)) * exp(-z / (hy * 0.7));
    layer(acc, cover, sunColor, shimmer * floorMask * (0.25 + 0.25 * bass));

    // The horizon line, glowing.
    float horizonGlow = exp(-abs(px.y - hy) / 1.2);
    layer(acc, cover, lerp(colorA.rgb, float3(1, 1, 1), 0.3), horizonGlow * (0.55 + 0.3 * beat) * (0.45 + 0.55 * exp(-abs(px.x - cx) / (size.x * 0.35))));

    return paint(acc / max(cover, 0.001), cover);
}
