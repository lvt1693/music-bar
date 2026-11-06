//! name: Pixel Rainbow Run
//! author: MusicBar
//! kind: progress
//! description: A chunky 8-bit rainbow streams to the playhead behind a pixel star, and pixel sparkles pop on every beat.

// A steady random number for a cell.
float hash2(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// The six stripes, red on top to violet at the bottom, in flat 8-bit colors.
float3 stripe(float k)
{
    float3 c = float3(1.00, 0.24, 0.27);
    c = lerp(c, float3(1.00, 0.60, 0.14), step(0.5, k));
    c = lerp(c, float3(1.00, 0.89, 0.24), step(1.5, k));
    c = lerp(c, float3(0.32, 0.90, 0.38), step(2.5, k));
    c = lerp(c, float3(0.27, 0.62, 1.00), step(3.5, k));
    c = lerp(c, float3(0.60, 0.40, 1.00), step(4.5, k));
    return c;
}

// The stepped wave the trail rides on, travelling toward the playhead; taller when the song is loud.
float lift(float x)
{
    float seg = floor(x / 3.0);
    return floor((0.5 + 0.5 * sin(seg * 0.9 - time * 7.0)) * (1.0 + 1.2 * level) + 0.5);
}

// Lays a color over what is there (premultiplied light, and how much of the pixel it covers).
void layer(inout float3 acc, inout float cover, float3 rgb, float a)
{
    a = saturate(a);
    acc = rgb * a + acc * (1.0 - a);
    cover = a + cover * (1.0 - a);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // Everything snaps to chunky pixels: c counts them along the bar and up from the screen edge.
    float p = max(2.0, floor(size.y / 12.0));
    float2 c = floor(float2(uv.x * size.x, rise(uv) * size.y) / p);
    float rows = floor(size.y / p);
    float head = floor(progress * size.x / p);
    float base = max(1.0, floor(rows * 0.16));

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // A dotted guide shows the rest of the song.
    float mid = base + 3.0;
    float guide = step(head + 3.0, c.x) * step(abs(c.y - mid), 0.0) * step(frac(c.x / 4.0), 0.1);
    layer(acc, cover, colorB.rgb, guide * 0.22);

    // Sparkles drift back a pixel at a time; a few twinkle, and on a beat a crowd of them flashes.
    float2 sc = float2(c.x + floor(time * 8.0), c.y);
    float2 cell = floor(sc / 6.0);
    float2 local = sc - cell * 6.0;
    float r = hash2(cell);
    float2 at = float2(1.0 + floor(hash2(cell + 7.1) * 4.0), 1.0 + floor(hash2(cell + 3.7) * 4.0));
    float2 e = abs(local - at);
    float arm = 1.0 + step(0.55, beat);
    float shape = step(min(e.x, e.y), 0.0) * step(max(e.x, e.y), arm) * (1.0 - 0.45 * step(0.5, max(e.x, e.y)));
    float twinkle = pow(saturate(sin(time * 2.6 + r * 40.0)), 6.0);
    float lit = saturate(twinkle * 0.65 + beat * step(r, 0.82) * 1.3);
    float near = saturate(1.0 - abs(c.x - head) * p / 650.0) * step(c.x, head + 12.0);
    float3 sparkColor = r < 0.6 ? colorA.rgb : (r < 0.72 ? colorB.rgb : colorC.rgb);
    layer(acc, cover, lerp(sparkColor, float3(1, 1, 1), 0.35), shape * step(0.4, r) * lit * near);

    // The rainbow itself, from the start of the song to the playhead, fading a little as it ages.
    float k = base + 5.0 + lift(c.x) - c.y;
    float band = step(0.0, k) * step(k, 5.0) * step(c.x, head);
    float age = saturate((head - c.x) * p / 900.0);
    float3 color = stripe(k) * (1.0 + 0.12 * beat);
    layer(acc, cover, color, band * lerp(0.95, 0.42, age));

    // The runner at the head: a pixel star (a 3 x 3 heart and four arms) whose arms shoot out on the beat.
    float2 star = float2(head + 2.0, base + 3.0 + lift(head));
    float2 d = abs(c - star);
    float reach = floor(beat * 2.2 + 0.6 * level);
    float arms = step(min(d.x, d.y), 0.0) * step(max(d.x, d.y), 2.0 + reach);
    float heart = step(max(d.x, d.y), 1.0);
    float2 px = float2(uv.x * size.x, rise(uv) * size.y);
    float halo = exp(-length(px - (star + 0.5) * p) / (5.0 + 7.0 * beat));
    layer(acc, cover, colorC.rgb, halo * (0.4 + 0.35 * beat));
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.2), arms * (1.0 - 0.35 * step(2.5, max(d.x, d.y))));
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.65), heart);
    layer(acc, cover, float3(1, 1, 1), step(d.x + d.y, 0.0));

    return paint(acc / max(cover, 0.001), cover);
}
