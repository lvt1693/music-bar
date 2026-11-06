//! name: Code Rain
//! author: MusicBar
//! kind: visualizer
//! description: Columns of tiny glyphs rain down the bar, with longer trails and more columns as the music gets louder.

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

void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float u = max(2.0, floor(size.y / 20.0));            // one dot of a glyph
    float cw = 4.0 * u;                                   // a glyph: 3 dots wide and a gap
    float ch = 5.0 * u;                                   // 4 dots tall and a gap
    float2 px = float2(uv.x * size.x, uv.y * size.y);     // pixels, y going down the screen
    float col = floor(px.x / cw);
    float r1 = hash1(col * 1.37 + 0.5);
    float r2 = hash1(col * 2.71 + 9.1);

    // Each column falls at its own pace, then waits a while above the bar before falling again.
    float speed = (2.4 + 3.2 * r1) * ch;
    float period = size.y + 22.0 * ch + 90.0 * r2 * u;
    float headY = frac(time * speed / period + r1 * 7.0) * period;
    float trail = (5.0 + 9.0 * r2) * ch * (0.55 + 0.75 * level);

    // Which glyph this pixel is in, and how far behind the falling head it is.
    float row = floor(px.y / ch);
    float behind = floor(headY / ch) - row;
    float fade = saturate(1.0 - behind * ch / trail) * step(0.0, behind);
    float isHead = step(abs(behind), 0.0);

    // The glyph: a 3 x 4 grid of dots, redrawn now and then.
    float2 local = px - float2(col * cw, row * ch);
    float2 dotAt = floor(local / u);
    float inGlyph = step(dotAt.x, 2.0) * step(dotAt.y, 3.0);
    float rate = 0.4 + 3.0 * hash2(float2(col, row) + 4.2) + 4.0 * isHead;
    float g = floor(time * rate + hash2(float2(row, col)) * 10.0);
    g -= floor(g / 97.0) * 97.0;
    float bit = step(0.42, hash2(float2(dotAt.x + dotAt.y * 3.0 + g * 13.0, col * 7.0 + row * 3.0)));

    // Loud music wakes more columns; the bass lights the middle, the treble the ends.
    float awake = smoothstep(r1 - 0.18, r1 + 0.18, 0.4 + 0.6 * level);
    float lift = 0.7 + 0.5 * spectrum(abs(col * cw / size.x - 0.5) * 2.0);
    float glyph = inGlyph * bit * awake * lift;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // A soft bloom around each falling head.
    float bloom = exp(-abs(px.y - headY) / (ch * 0.9)) * exp(-abs(px.x - (col + 0.375) * cw) / (cw * 0.55));
    layer(acc, cover, colorA.rgb, bloom * awake * (0.22 + 0.2 * beat));

    // The trail, bright at the head and fading back into the dark.
    float3 trailColor = lerp(colorA.rgb, colorB.rgb, saturate(behind * ch / trail));
    layer(acc, cover, trailColor, glyph * pow(fade, 1.3) * (1.0 - isHead) * 0.9);
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.7), glyph * isHead * (0.9 + 0.1 * beat));

    // Fast drizzle between the columns when the song is loud.
    float lane = hash1(col * 5.3 + 2.0);
    float dropY = frac(time * (1.6 + lane) + lane * 9.0) * (size.y + 40.0) - 20.0;
    float streak = smoothstep(14.0, 0.0, dropY - px.y) * step(px.y, dropY) * step(0.55, lane);
    float inGap = step(3.0 * u, local.x) * step(local.x, 3.0 * u + 1.0);
    layer(acc, cover, colorB.rgb, streak * inGap * level * 0.4);

    return paint(acc / max(cover, 0.001), cover);
}
