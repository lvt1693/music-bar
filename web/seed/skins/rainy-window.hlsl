//! name: Rainy Window
//! author: MusicBar
//! kind: visualizer
//! description: Raindrops slide down the glass over soft, warm city lights that glow gently with the music.

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

// One out-of-focus light per cell, drifting slowly along the street behind the glass.
float4 bokeh(float2 px, float cellW, float drift, float rMin, float rMax, float seed)
{
    float x = px.x + time * drift;
    float cell = floor(x / cellW);
    float r = lerp(rMin, rMax, hash1(cell * 3.1 + seed));
    float at = r + (cellW - 2.0 * r) * hash1(cell * 7.7 + seed);
    float cy = size.y * lerp(0.2, 0.8, hash1(cell * 1.9 + seed));
    float d = length(float2(x - cell * cellW - at, px.y - cy));
    float disc = smoothstep(r, r - 1.6, d) * (0.62 + 0.38 * smoothstep(r * 0.35, r, d));
    float pick = hash1(cell * 5.3 + seed);
    float3 c = pick < 0.5 ? colorA.rgb : (pick < 0.82 ? colorB.rgb : colorC.rgb);
    float on = step(0.22, hash1(cell * 9.1 + seed));
    float breathe = 0.78 + 0.22 * sin(time * 0.5 + pick * 20.0);
    return float4(c, disc * on * breathe);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, uv.y * size.y);   // pixels, y going down the screen
    float s = size.y / 40.0;
    float glow = 0.7 + 0.35 * level + 0.25 * spectrum(abs(uv.x - 0.5) * 2.0);

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // A warm haze rising from the street.
    float h = rise(uv);
    layer(acc, cover, lerp(colorA.rgb, float3(1.0, 0.72, 0.45), 0.45), (1.0 - h) * (1.0 - h) * 0.08);

    // City lights, far and near, blurred by the glass.
    float4 far = bokeh(px, 58.0, 3.0, 5.0 * s, 10.0 * s, 0.0);
    layer(acc, cover, far.rgb, far.a * 0.34 * glow);
    float4 nearLight = bokeh(px, 150.0, 6.0, 13.0 * s, 21.0 * s, 17.0);
    layer(acc, cover, nearLight.rgb, nearLight.a * 0.2 * glow);

    // Beads of water resting on the glass, catching the light.
    float cellSize = 11.0 * s;
    float2 dc = floor(px / cellSize);
    float2 dl = px - dc * cellSize;
    float dr = hash2(dc);
    float rad = (0.9 + 1.5 * hash2(dc + 2.3)) * s;
    float2 dp = rad + (cellSize - 2.0 * rad) * float2(hash2(dc + 5.1), hash2(dc + 8.7));
    float2 dd = dl - dp;
    float dlen = length(dd);
    float bead = step(0.7, dr) * (0.65 + 0.35 * sin(time * 0.25 + dr * 40.0));
    float body = smoothstep(rad, rad - 0.7, dlen);
    float rim = body * smoothstep(-0.2 * rad, rad, dd.y);
    float shine = exp(-length(dd + float2(0.35, 0.4) * rad) / (0.3 * s + 0.25));
    float3 wet = lerp(colorC.rgb, float3(1, 1, 1), 0.6);
    layer(acc, cover, wet, bead * (body * 0.12 + rim * 0.3));
    layer(acc, cover, float3(1, 1, 1), bead * shine * body * 0.5);

    // Now and then a drop lets go and slides down, leaving a wet trail.
    float laneW = 96.0;
    float lane = floor(px.x / laneW);
    float lr = hash1(lane * 4.7 + 1.0);
    float cycle = time / (6.0 + 5.0 * lr) + lr * 5.0;
    float ci = floor(cycle);
    float f = frac(cycle);
    float active = step(0.45, hash1(lane * 3.3 + ci * 1.7));
    float yd = -6.0 * s + (size.y + 12.0 * s) * f * f;
    float x0 = lane * laneW + 18.0 + 60.0 * hash1(lane * 2.2 + ci);
    float xd = x0 + sin(yd * 0.25) * 1.2 * s;
    float rr = (2.1 + 1.0 * lr) * s;
    float2 q = (px - float2(xd, yd)) * float2(1.0, 0.8);
    float slider = smoothstep(rr, rr - 0.8, length(q)) * active;
    float sliderShine = exp(-length(q + float2(0.3, 0.45) * rr) / (0.35 * s + 0.25)) * slider;
    float tx = x0 + sin(px.y * 0.25) * 1.2 * s;
    float wetTrail = exp(-abs(px.x - tx) / (0.45 * s)) * step(px.y, yd) * exp(-(yd - px.y) / (16.0 * s)) * active;
    layer(acc, cover, wet, wetTrail * 0.22);
    layer(acc, cover, wet, slider * (0.25 + 0.3 * smoothstep(-rr, rr, q.y)));
    layer(acc, cover, float3(1, 1, 1), sliderShine * 0.85);

    return paint(acc / max(cover, 0.001), cover);
}
