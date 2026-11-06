//! name: Heartbeat
//! author: MusicBar
//! kind: progress
//! description: A glowing heart-monitor trace sweeps up to the playhead, and every beat kicks a fresh spike at the tip.

// One pulse of the trace, u from 0 to 1: a small bump, the tall spike, and a slow wave after it.
float pulse(float u)
{
    float2 p = float2(u - 0.18, 0.035);
    float2 q = float2(u - 0.355, 0.012);
    float2 r = float2(u - 0.38, 0.011);
    float2 s = float2(u - 0.405, 0.014);
    float2 t = float2(u - 0.62, 0.05);
    return 0.10 * exp(-p.x * p.x / (p.y * p.y))
         - 0.12 * exp(-q.x * q.x / (q.y * q.y))
         + 1.00 * exp(-r.x * r.x / (r.y * r.y))
         - 0.26 * exp(-s.x * s.x / (s.y * s.y))
         + 0.20 * exp(-t.x * t.x / (t.y * t.y));
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
    float head = progress * size.x;
    float baseY = H * 0.3;
    float period = 150.0;
    float amp = H * 0.5 * (0.78 + 0.3 * level);
    float played = step(px.x, head);

    // The live spike at the tip: it shoots up on a beat and settles as the beat fades.
    float kickAt = head - 12.0;
    float2 k = float2(px.x - kickAt, px.x - kickAt - 5.0);
    float kick = beat * (0.95 * exp(-k.x * k.x / 9.0) - 0.3 * exp(-k.y * k.y / 6.0));

    // The trace and its slope, so the line keeps its width on the steep spike.
    float e = 0.5 / period;
    float u = frac(px.x / period);
    float y0 = pulse(u) + kick;
    float kickSlope = beat * (-0.95 * exp(-k.x * k.x / 9.0) * 2.0 * k.x / 9.0 + 0.3 * exp(-k.y * k.y / 6.0) * 2.0 * k.y / 6.0);
    float slope = (pulse(u + e) - pulse(u - e) + kickSlope) * amp;
    float traceY = baseY + amp * y0;
    float dist = abs(px.y - traceY) / sqrt(1.0 + slope * slope);
    float core = smoothstep(1.5, 0.4, dist);
    float bloom = exp(-dist / 3.5);

    // A phosphor sweep runs along the played part every couple of seconds and leaves an afterglow.
    float sweep = frac(time / 2.4) * size.x;
    float behind = sweep - px.x;
    float afterglow = behind >= 0.0 ? exp(-behind / 380.0) : exp(-(behind + size.x) / 380.0);
    float bright = 0.32 + 0.68 * afterglow;

    float3 acc = float3(0, 0, 0);
    float cover = 0;

    // A faint monitor grid over the played part.
    float2 cell = floor(px);
    float minor = max(step(frac(cell.x / 12.0), 0.01), step(frac(cell.y / 12.0), 0.01));
    float major = step(frac(cell.x / 60.0), 0.001);
    layer(acc, cover, colorB.rgb, played * (minor * 0.05 + major * 0.06));

    // A dashed baseline for the rest of the song.
    float dash = step(head + 6.0, px.x) * step(frac(px.x / 8.0), 0.5) * smoothstep(1.2, 0.2, abs(px.y - baseY));
    layer(acc, cover, colorB.rgb, dash * 0.2);

    // The trace: a soft glow, and the line, brightest just behind the sweep.
    layer(acc, cover, lerp(colorA.rgb, colorB.rgb, 0.35), bloom * played * bright * 0.4);
    float3 lineColor = lerp(colorA.rgb, float3(1, 1, 1), 0.25 * afterglow);
    layer(acc, cover, lineColor, core * played * bright);

    // The sweep's bright point running along the trace.
    float2 sweepDot = float2(sweep, baseY + amp * pulse(frac(sweep / period)));
    float spot = exp(-length(px - sweepDot) / 3.0) * step(sweep, head);
    layer(acc, cover, lerp(colorA.rgb, float3(1, 1, 1), 0.6), spot * 0.8);

    // The playhead: a bright dot that swells on the beat.
    float2 tip = float2(head, baseY + amp * (pulse(frac(head / period)) + beat * 0.05));
    float tipD = length(px - tip);
    layer(acc, cover, colorC.rgb, exp(-tipD / (4.0 + 6.0 * beat)) * (0.5 + 0.4 * beat));
    layer(acc, cover, lerp(colorC.rgb, float3(1, 1, 1), 0.7), smoothstep(2.6 + 1.5 * beat, 1.4 + 1.5 * beat, tipD));

    return paint(acc / max(cover, 0.001), cover);
}
