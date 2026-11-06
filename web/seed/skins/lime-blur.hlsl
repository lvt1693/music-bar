//! name: lime blur
//! author: MusicBar
//! kind: progress
//! description: One flat slab of lime up to the playhead with a soft out-of-focus edge, deliberately minimal and a little lo-fi.

float hash2(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;
    float head = progress * size.x;

    // The slab: low along the screen edge, so the lyrics keep the dark above it.
    float top = H * (0.4 + 0.05 * level);
    float soft = 2.5 + 2.5 * beat;
    float body = smoothstep(top + soft, top - soft, px.y);

    // The playhead is out of focus, and blurs a little more on every beat.
    float blur = 18.0 + 26.0 * beat + 10.0 * level;
    float played = smoothstep(head + blur, head - blur * 0.6, px.x);

    // A haze leaks above the slab, as if the picture were slightly overexposed.
    float haze = exp(-max(px.y - top, 0.0) / 5.0) * step(top, px.y) * played * 0.22;

    // Grain, fixed in place, like a cheap print.
    float grain = 0.97 + 0.06 * hash2(floor(px / 2.0));
    float3 lime = colorA.rgb * grain * (1.0 + 0.08 * beat);

    // The rest of the song: the same slab, barely there.
    float rest = body * (1.0 - played) * 0.08;
    float alpha = max(body * played * 0.93, rest) + haze;
    return paint(lime, alpha);
}
