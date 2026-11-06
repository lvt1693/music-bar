//! name: Lava Lamp
//! author: MusicBar
//! kind: visualizer
//! description: Soft wax blobs rise from a glowing pool, melt into each other and swell as the music gets louder.

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

// How much one blob adds to the wax here: it drifts up from the pool, cools near the top and sinks back.
float blob(float2 px, float seed, float cellW, float H, float grow)
{
    float cell = floor(seed / 2.0);
    float r1 = hash1(seed * 1.71 + 0.3);
    float r2 = hash1(seed * 2.93 + 1.1);
    float r3 = hash1(seed * 4.17 + 2.7);
    float x = (cell + 0.2 + 0.6 * r1) * cellW + 16.0 * sin(time * (0.18 + 0.1 * r2) + r3 * 6.28);
    float phase = frac(time / (16.0 + 10.0 * r2) + r3);
    float y = H * (-0.25 + 1.05 * (0.5 - 0.5 * cos(phase * 6.2832)));
    float r = H * (0.13 + 0.09 * r1) * grow;
    float2 d = (px - float2(x, y)) * float2(1.0, 1.2 - 0.25 * sin(phase * 6.2832));
    return r * r / (dot(d, d) + 1.0);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;
    float cellW = 120.0;
    float grow = (0.8 + 0.55 * level) * (1.0 + 0.12 * beat) * (0.85 + 0.35 * spectrum(abs(uv.x - 0.5) * 2.0));

    // The wax field: the pool along the screen edge and the blobs of this cell and its neighbours.
    float home = floor(px.x / cellW);
    float field = 1.25 * exp(-px.y / (H * (0.07 + 0.03 * sin(px.x * 0.01 + time * 0.3))));
    [unroll] for (int i = 0; i < 6; i++)
    {
        float cell = home + floor(i / 2.0) - 1.0;
        field += blob(px, cell * 2.0 + fmod(i, 2.0), cellW, H, grow);
    }

    // Anti-aliased surface; the width is capped because the field is steep inside the blobs.
    float edge = clamp(fwidth(field) * 0.9, 0.02, 0.35);
    float wax = smoothstep(1.0 - edge, 1.0 + edge, field);
    float heat = smoothstep(1.2, 7.0, field) * 0.75;
    float h = px.y / H;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // The lamp's glow around the wax.
    layer(acc, cover, lerp(colorA.rgb, colorB.rgb, 0.3), smoothstep(0.3, 1.0, field) * (1.0 - wax) * 0.28);

    // The wax: warm at the bottom, a little cooler as it rises, hot in the middle, with a lit rim.
    float3 c = lerp(colorA.rgb, colorB.rgb, saturate(h * 1.1) * 0.45);
    c = lerp(c, lerp(colorC.rgb, float3(1, 1, 1), 0.25), heat);
    float rim = exp(-abs(field - 1.0) * 5.0) * 0.35;
    layer(acc, cover, c + rim, wax * (0.82 + 0.12 * level));

    return paint(acc / max(cover, 0.001), cover);
}
