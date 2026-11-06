//! name: Disco Fever
//! author: MusicBar
//! kind: visualizer
//! description: Mirror-ball light spots glide across the bar and burst into starry flashes on every beat.

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

// One reflection of the mirror ball per cell, sliding along as the ball turns.
float4 spots(float2 px, float2 cellSize, float speed, float spotSize, float seed)
{
    float2 q = float2(px.x - time * speed, px.y + sin(px.x * 0.004 + time * 0.3) * 2.0);
    float2 cell = floor(q / cellSize);
    float2 local = q - cell * cellSize;
    float r = hash2(cell + seed);
    float2 at = cellSize * float2(0.3 + 0.4 * hash2(cell + seed + 1.7), 0.3 + 0.4 * hash2(cell + seed + 4.3));
    float2 d = local - at;
    float sz = spotSize * (0.7 + 0.6 * hash2(cell + seed + 9.1)) * (1.0 + 0.35 * beat);
    float facet = max(abs(d.x) * 0.8, abs(d.y));
    float core = smoothstep(sz + 0.7, sz - 0.5, facet);
    float halo = exp(-length(d) / (sz * 1.7));
    float twinkle = 0.2 + 0.8 * pow(0.5 + 0.5 * sin(time * 4.5 + r * 60.0), 3.0);
    float present = step(0.58 - 0.28 * level, r);
    float flash = beat * step(0.74, r);
    float flare = (exp(-abs(d.y) / 0.55) * exp(-abs(d.x) / (2.0 + 7.0 * beat))
                 + exp(-abs(d.x) / 0.55) * exp(-abs(d.y) / (1.5 + 4.0 * beat))) * flash;
    float a = present * (core * (0.3 + 0.7 * twinkle) + halo * 0.28 * twinkle) + flash * (core + halo * 0.5) + flare * 0.6;
    float pick = frac(r * 7.13);
    float3 c = pick < 0.4 ? colorA.rgb : (pick < 0.75 ? colorB.rgb : colorC.rgb);
    return float4(lerp(c, float3(1, 1, 1), 0.25 + 0.45 * flash), saturate(a));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float s = size.y / 40.0;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // Two colored beams sweeping slowly over the floor, brighter on the beat.
    float b1 = exp(-abs(px.x - (0.5 + 0.42 * sin(time * 0.33)) * size.x) / (size.x * 0.07));
    float b2 = exp(-abs(px.x - (0.5 + 0.42 * sin(time * 0.33 + 2.6)) * size.x) / (size.x * 0.07));
    float fall = 0.55 + 0.45 * (1.0 - rise(uv));
    layer(acc, cover, colorA.rgb, b1 * fall * (0.07 + 0.08 * beat + 0.05 * level));
    layer(acc, cover, colorB.rgb, b2 * fall * (0.07 + 0.08 * beat + 0.05 * level));

    // The floor lights up along the screen edge on a beat.
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.2), exp(-px.y / (4.0 * s)) * beat * 0.22);

    // Far spots, small and slow; near spots, bigger and quicker.
    float4 far = spots(px, float2(17.0, size.y / 3.0), 21.0, 0.9 * s, 0.0);
    layer(acc, cover, far.rgb, far.a * 0.55);
    float4 nearSpots = spots(px, float2(31.0, size.y / 2.0), 40.0, 1.7 * s, 31.0);
    layer(acc, cover, nearSpots.rgb, nearSpots.a);

    return paint(acc / max(cover, 0.001), cover);
}
