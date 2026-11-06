//! name: Phosphor VU
//! author: MusicBar
//! kind: visualizer
//! description: A retro CRT level meter climbs in from both ends in glowing phosphor segments, scanlines and all.

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

// One meter row between heights y0 and y1 (pixels), lit up to v (0 to 1 of the way to the middle).
// dEnd is how far this pixel is from the nearer end of the bar. Returns the lit segments, the unlit
// ones (with the afterglow of segments that were lit a moment ago), the bloom, and the bright tip.
float4 meter(float dEnd, float y, float y0, float y1, float v, float halfW)
{
    float pitch = 12.0;
    float n = floor(dEnd / pitch);
    float local = dEnd - n * pitch;
    float at = (n + 0.5) * pitch / halfW;
    float segX = smoothstep(1.0, 2.0, local) * smoothstep(11.0, 10.0, local);
    float segY = smoothstep(y0 - 0.6, y0 + 0.6, y) * smoothstep(y1 + 0.6, y1 - 0.6, y);
    float seg = segX * segY;
    float lit = step(at, v);
    float tip = lit * step(v - pitch / halfW, at);
    float beyond = max((at - v) * halfW, 0.0);
    float outside = max(max(y0 - y, y - y1), 0.0);
    float bloom = exp(-max(dEnd - v * halfW, 0.0) / 6.0) * exp(-outside / 2.2);
    float ghost = 0.07 + 0.32 * exp(-beyond / 30.0);
    return float4(seg * lit, seg * (1.0 - lit) * ghost, bloom, seg * tip);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float H = size.y;
    float halfW = size.x * 0.5;
    float dEnd = min(px.x, size.x - px.x);
    float at = dEnd / halfW;

    // Two channels: one leaning on the bass, one on the mids, both kicked by the beat.
    float vTop = saturate(0.08 + 0.62 * level + 0.22 * spectrum(0.05) + 0.08 * beat);
    float vBottom = saturate(0.06 + 0.56 * level + 0.32 * spectrum(0.4) + 0.08 * beat);
    float4 top = meter(dEnd, px.y, H * 0.58, H * 0.8, vTop, halfW);
    float4 bottom = meter(dEnd, px.y, H * 0.2, H * 0.42, vBottom, halfW);

    // The first color through most of the scale, the accent in the hot zone near the middle.
    float hot = smoothstep(0.6, 0.8, at);
    float3 c = lerp(colorA.rgb, colorC.rgb, hot);

    // CRT: every other line dimmer, a slow shimmer rolling along, and a faint flicker.
    float scanRow = floor(uv.y * size.y);
    float scan = 0.62 + 0.38 * step(frac(scanRow * 0.5), 0.25);
    float flicker = 0.96 + 0.04 * sin(time * 11.0);
    float sheen = 1.0 + 0.15 * exp(-abs(frac(at * 0.5 - time * 0.08) - 0.5) * 18.0);
    float crt = scan * flicker * sheen;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // The scale between the two rows, with ticks every four segments.
    float ys = H * 0.5;
    float tickAt = dEnd - floor(dEnd / 48.0) * 48.0;
    float majorAt = dEnd - floor(dEnd / 192.0) * 192.0;
    float scale = smoothstep(1.0, 0.0, abs(px.y - ys)) * 0.12
                + step(tickAt, 1.5) * step(abs(px.y - ys), 1.6) * 0.3
                + step(majorAt, 1.5) * step(abs(px.y - ys), 3.2) * 0.25;
    layer(acc, cover, colorB.rgb, scale * crt);

    // Phosphor bloom around the lit run, soaking into the gaps between segments.
    layer(acc, cover, c, max(top.z, bottom.z) * 0.3 * flicker);

    // Unlit segments, barely there, still glowing where the level has just fallen back.
    layer(acc, cover, lerp(colorB.rgb, c, 0.5), (top.y + bottom.y) * scan);

    // The lit segments, then the bright tips.
    layer(acc, cover, c * crt * (1.0 + 0.2 * beat), (top.x + bottom.x) * 0.95 * (0.75 + 0.25 * scan));
    layer(acc, cover, lerp(c, float3(1, 1, 1), 0.55), (top.w + bottom.w) * (0.55 + 0.45 * beat));

    return paint(acc / max(cover, 0.001), cover);
}
