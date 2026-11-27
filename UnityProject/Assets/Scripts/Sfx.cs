using System.Collections.Generic;
using UnityEngine;

// Tiros e explosoes sao gravacoes reais (Resources/Sfx, CC0: The Free Firearm Sound Library);
// o resto (interface, mecanica, trilha) e sintetizado por codigo. Sem o arquivo gravado, cai no sintetizado.
public static class Sfx
{
    const int SR = 44100;
    const float TAU = 6.2831853f;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly System.Random rng = new System.Random(7);

    static float Noise() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

    // Seno com varredura linear de f0 ate f1 ao longo de dur segundos.
    static float Sweep(float t, float f0, float f1, float dur)
    {
        return Mathf.Sin(TAU * (f0 * t + (f1 - f0) * t * t / (2f * dur)));
    }

    static AudioClip Make(string name, float secs, System.Func<float, float> f)
    {
        int n = Mathf.Max(64, (int)(secs * SR));
        float[] d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
        int fade = Mathf.Min(400, n);
        for (int i = 0; i < fade; i++) d[n - 1 - i] *= i / (float)fade;
        AudioClip c = AudioClip.Create(name, n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    public static AudioClip Get(string id)
    {
        AudioClip c;
        if (clips.TryGetValue(id, out c) && c != null) return c;
        c = Build(id);
        clips[id] = c;
        return c;
    }

    static AudioClip Build(string id)
    {
        float lp = 0f;
        switch (id)
        {
            case "startup":
                // Acorde de abertura quente, sintetico e sem ruido: entrada lenta,
                // pequena queda de afinacao e cauda longa para soar como um pad.
                return Make(id, 2.2f, t =>
                {
                    float attack = Mathf.SmoothStep(0f, 1f, t / 0.32f);
                    float release = Mathf.Exp(-Mathf.Max(0f, t - 0.62f) * 1.55f);
                    float env = attack * release;
                    float chord = Sweep(t, 73.6f, 72.4f, 2.2f) * 0.30f
                                + Sweep(t, 146.8f, 144.4f, 2.2f) * 0.28f
                                + Sweep(t, 174.6f, 171.8f, 2.2f) * 0.22f
                                + Sweep(t, 220.0f, 216.5f, 2.2f) * 0.18f
                                + Sweep(t, 293.7f, 289.0f, 2.2f) * 0.10f;
                    return chord * env * 0.62f;
                });
            case "rifle":
                // 3 camadas: (1) punch grave no peito, (2) estalo mecanico do ferrolho, (3) cauda acustica / eco urbano.
                return Make(id, 0.42f, t =>
                {
                    lp += (Noise() - lp) * 0.42f;
                    float punch = Sweep(t, 165f, 44f, 0.14f) * Mathf.Exp(-t * 28f) * 0.72f;
                    float mech = Clack(t, 0.002f, 2400f, 95f, 0.52f) + Noise() * Mathf.Exp(-t * 65f) * 0.45f;
                    float tail = lp * Mathf.Exp(-t * 15f) * 0.45f
                               + (t > 0.042f ? lp * Mathf.Exp(-(t - 0.042f) * 11f) * 0.25f : 0f)
                               + (t > 0.105f ? lp * Mathf.Exp(-(t - 0.105f) * 8f) * 0.16f : 0f);
                    return punch + mech + tail;
                });
            case "shotgun":
                return Make(id, 0.58f, t =>
                {
                    lp += (Noise() - lp) * 0.32f;
                    float punch = Sweep(t, 130f, 35f, 0.25f) * Mathf.Exp(-t * 16f) * 0.85f;
                    float mech = Clack(t, 0.003f, 1800f, 75f, 0.55f) + Noise() * Mathf.Exp(-t * 40f) * 0.6f;
                    float tail = lp * Mathf.Exp(-t * 11f) * 0.65f
                               + (t > 0.05f ? lp * Mathf.Exp(-(t - 0.05f) * 8.5f) * 0.3f : 0f)
                               + (t > 0.13f ? lp * Mathf.Exp(-(t - 0.13f) * 6.5f) * 0.18f : 0f);
                    return punch + mech + tail;
                });
            case "sniper":
                // 3 camadas: punch subgrave + estalo seco do ferrolho + cauda acustica / eco longo.
                return Make(id, 1.4f, t =>
                {
                    lp += (Noise() - lp) * 0.48f;
                    float punch = Sweep(t, 150f, 34f, 0.35f) * Mathf.Exp(-t * 11f) * 0.9f;
                    float mech = Clack(t, 0.001f, 2900f, 85f, 0.6f) + Noise() * Mathf.Exp(-t * 75f) * 0.85f;
                    float tail = lp * Mathf.Exp(-t * 12f) * 0.7f
                               + (t > 0.06f ? lp * Mathf.Exp(-(t - 0.06f) * 6.5f) * 0.35f : 0f)
                               + (t > 0.20f ? lp * Mathf.Exp(-(t - 0.20f) * 4.5f) * 0.25f : 0f);
                    return punch + mech + tail;
                });
            case "heli":
                // Uma volta de rotor em loop: pulsos de ar das pas + turbina.
                return Make(id, 1f, t =>
                {
                    lp += (Noise() - lp) * 0.12f;
                    float blade = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(TAU * 22f * t), 3f);
                    return lp * blade * 2.4f + Mathf.Sin(TAU * 88f * t) * 0.08f + Mathf.Sin(TAU * 440f * t) * 0.02f;
                });
            case "engine":
                return Make(id, 0.5f, t => (Saw(70f, t) * 0.25f + Saw(140.5f, t) * 0.12f) * (0.7f + 0.3f * Mathf.Sin(TAU * 35f * t)));
            case "beep":
                return Make(id, 0.12f, t => Mathf.Sin(TAU * 2100f * t) * Mathf.Exp(-t * 18f) * 0.6f);
            case "radio":
                // Chiado curto de squelch + duplo bip tatico de comunicacao.
                return Make(id, 0.24f, t =>
                {
                    lp += (Noise() - lp) * 0.35f;
                    float squelch = (t < 0.04f || t > 0.19f) ? lp * 0.45f : lp * 0.08f;
                    float b1 = t >= 0.04f && t < 0.10f ? Mathf.Sin(TAU * 1850f * t) * 0.5f : 0f;
                    float b2 = t >= 0.12f && t < 0.18f ? Mathf.Sin(TAU * 2250f * t) * 0.55f : 0f;
                    return squelch + b1 + b2;
                });
            case "roar":
                return Make(id, 1.6f, t =>
                {
                    lp += (Noise() - lp) * 0.2f;
                    float env = Mathf.Clamp01(t / 0.15f) * Mathf.Exp(-Mathf.Max(0f, t - 0.5f) * 2.4f);
                    float f = 95f - 30f * t + 12f * Mathf.Sin(TAU * 9f * t);
                    return (Saw(f, t) * 0.5f + Saw(f * 1.51f, t) * 0.3f + lp * 1.6f) * env * (0.75f + 0.25f * Mathf.Sin(TAU * 27f * t));
                });
            case "rail":
                return Make(id, 0.7f, t =>
                {
                    lp += (Noise() - lp) * 0.38f;
                    float punch = Sweep(t, 140f, 48f, 0.18f) * Mathf.Exp(-t * 18f) * 0.55f;
                    float s = Sweep(t, 2200f, 180f, 0.7f);
                    float sq = s > 0f ? 0.5f : -0.5f;
                    float mech = (s * 0.45f + sq * 0.22f) * Mathf.Exp(-t * 7f) + Clack(t, 0.002f, 2800f, 90f, 0.4f);
                    float tail = lp * Mathf.Exp(-t * 10f) * 0.35f + (t > 0.05f ? lp * Mathf.Exp(-(t - 0.05f) * 7f) * 0.2f : 0f);
                    return punch + mech + tail;
                });
            case "rocket":
                return Make(id, 0.55f, t =>
                {
                    lp += (Noise() - lp) * 0.14f;
                    float punch = Sweep(t, 95f, 36f, 0.28f) * Mathf.Exp(-t * 10f) * 0.75f;
                    float mech = Clack(t, 0.002f, 1400f, 65f, 0.45f) + Sweep(t, 90f, 240f, 0.5f) * Mathf.Exp(-t * 8f) * 0.4f;
                    float tail = lp * Mathf.Exp(-t * 6f) * 1.3f;
                    return punch + mech + tail;
                });
            case "flyby":
                // Estalo supersonico + assobio direcional de bala passando perto (< 2.0m).
                return Make(id, 0.16f, t =>
                {
                    float crack = Clack(t, 0f, 3400f, 110f, 0.55f) + Noise() * Mathf.Exp(-t * 95f) * 0.65f;
                    float whiz = Sweep(t, 2600f, 680f, 0.14f) * Mathf.Exp(-t * 24f) * 0.55f;
                    return crack + whiz;
                });
            case "explosion":
                return Make(id, 1.1f, t =>
                {
                    lp += (Noise() - lp) * (0.25f * Mathf.Exp(-t * 3f) + 0.02f);
                    return lp * Mathf.Exp(-t * 4f) * 2.2f + Sweep(t, 70f, 30f, 1.1f) * Mathf.Exp(-t * 5f) * 0.8f;
                });
            case "hit":
                return Make(id, 0.06f, t => Noise() * Mathf.Exp(-t * 110f) * 0.5f + Mathf.Sin(TAU * 1150f * t) * Mathf.Exp(-t * 70f) * 0.55f);
            case "headshot":
                return Make(id, 0.12f, t => (Mathf.Sin(TAU * 2100f * t) * 0.55f + Mathf.Sin(TAU * 3150f * t) * 0.45f) * Mathf.Exp(-t * 45f) + Clack(t, 0.005f, 2600f, 80f, 0.4f));
            case "kill":
                return Make(id, 0.3f, t => Mathf.Sin(TAU * (t < 0.1f ? 660f : 990f) * t) * Mathf.Exp(-t * 9f) * 0.5f);
            case "hurt":
                return Make(id, 0.15f, t => Sweep(t, 300f, 120f, 0.15f) * Mathf.Exp(-t * 20f) * 0.7f);
            case "death":
                return Make(id, 0.6f, t => Sweep(t, 500f, 50f, 0.6f) * Mathf.Exp(-t * 5f) * 0.5f + Noise() * Mathf.Exp(-t * 14f) * 0.3f);
            case "jump":
                return Make(id, 0.15f, t => Sweep(t, 260f, 520f, 0.15f) * Mathf.Exp(-t * 18f) * 0.4f);
            case "dash":
                return Make(id, 0.35f, t =>
                {
                    lp += (Noise() - lp) * (0.05f + t * 1.2f);
                    return lp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.35f)) * 1.2f;
                });
            case "pad":
                return Make(id, 0.4f, t => Sweep(t, 200f, 1200f, 0.4f) * Mathf.Exp(-t * 6f) * 0.45f);
            case "gate":
                // Portao de aco: estalo da trava, motor zumbindo enquanto corre e batida no fim.
                return Make(id, 0.95f, t =>
                {
                    lp += (Noise() - lp) * 0.08f;
                    float latch = Noise() * Mathf.Exp(-t * 90f) * 0.5f + Mathf.Sin(TAU * 1800f * t) * Mathf.Exp(-t * 60f) * 0.25f;
                    float run = t > 0.05f && t < 0.78f ? (Mathf.Sin(TAU * 92f * t) * 0.22f + Mathf.Sin(TAU * 184f * t) * 0.1f + lp * 0.9f) * Mathf.SmoothStep(0f, 1f, (t - 0.05f) / 0.1f) : 0f;
                    float te = t - 0.78f;
                    float thud = te > 0f ? (Noise() * Mathf.Exp(-te * 50f) * 0.6f + Mathf.Sin(TAU * 70f * te) * Mathf.Exp(-te * 18f) * 0.7f) : 0f;
                    return latch + run + thud;
                });
            case "pickup":
                return Make(id, 0.3f, t => Mathf.Sin(TAU * (t < 0.1f ? 523f : (t < 0.2f ? 659f : 784f)) * t) * Mathf.Exp(-t * 6f) * 0.4f);
            case "empty":
                // Percussor batendo em seco: clique metalico curto.
                return Make(id, 0.09f, t => (Noise() * Mathf.Exp(-t * 220f) * 0.5f + Mathf.Sin(TAU * 3400f * t) * Mathf.Exp(-t * 90f) * 0.35f + Mathf.Sin(TAU * 5900f * t) * Mathf.Exp(-t * 140f) * 0.2f));
            case "reload":
                // Tres tempos: carregador sai, carregador entra, ferrolho vai a frente.
                return Make(id, 1.15f, t =>
                {
                    lp += (Noise() - lp) * 0.4f;
                    float o = Clack(t, 0.00f, 2100f, 60f, 0.5f) + (t < 0.06f ? lp * 0.35f * (1f - t / 0.06f) : 0f);
                    o += Clack(t, 0.42f, 1500f, 45f, 0.75f) + Clack(t, 0.45f, 3100f, 80f, 0.3f);
                    o += Clack(t, 0.86f, 2600f, 70f, 0.6f) + Clack(t, 0.93f, 1800f, 50f, 0.85f);
                    float sl = t - 0.86f;
                    if (sl > 0f && sl < 0.07f) o += lp * 0.5f * Mathf.Sin(Mathf.PI * sl / 0.07f);
                    return o;
                });
            // Etapas da recarga em primeira pessoa (tocadas no momento certo da animacao).
            case "magout":
                return Make(id, 0.22f, t => { lp += (Noise() - lp) * 0.35f; return Clack(t, 0.00f, 2300f, 70f, 0.55f) + (t > 0.02f && t < 0.16f ? lp * 0.28f * Mathf.Sin(Mathf.PI * (t - 0.02f) / 0.14f) : 0f); });
            case "magin":
                return Make(id, 0.2f, t => Clack(t, 0.00f, 1500f, 45f, 0.8f) + Clack(t, 0.025f, 3200f, 85f, 0.35f));
            case "charge":
                return Make(id, 0.32f, t =>
                {
                    lp += (Noise() - lp) * 0.45f;
                    float o = Clack(t, 0.00f, 2700f, 75f, 0.5f) + Clack(t, 0.17f, 1900f, 50f, 0.9f);
                    if (t < 0.12f) o += lp * 0.45f * Mathf.Sin(Mathf.PI * t / 0.12f);
                    return o;
                });
            case "shell":
                return Make(id, 0.14f, t => Clack(t, 0.0f, 2900f, 90f, 0.45f) + Clack(t, 0.05f, 1300f, 60f, 0.5f));
            case "pump":
                return Make(id, 0.42f, t =>
                {
                    lp += (Noise() - lp) * 0.3f;
                    float o = Clack(t, 0.00f, 1700f, 55f, 0.7f) + Clack(t, 0.21f, 1400f, 45f, 0.9f);
                    if (t < 0.09f) o += lp * 0.5f * Mathf.Sin(Mathf.PI * t / 0.09f);
                    if (t > 0.13f && t < 0.21f) o += lp * 0.5f * Mathf.Sin(Mathf.PI * (t - 0.13f) / 0.08f);
                    return o;
                });
            case "cellout":
                return Make(id, 0.35f, t => Sweep(t, 900f, 220f, 0.35f) * Mathf.Exp(-t * 7f) * 0.22f + Clack(t, 0.0f, 2600f, 70f, 0.4f));
            case "cellin":
                return Make(id, 0.5f, t => Clack(t, 0.0f, 1600f, 50f, 0.7f) + (t > 0.05f ? Sweep(t, 300f, 1500f, 0.5f) * Mathf.Exp(-(t - 0.05f) * 5f) * 0.25f : 0f));
            case "rocketin":
                return Make(id, 0.4f, t => { lp += (Noise() - lp) * 0.2f; return (t < 0.25f ? lp * 0.4f * Mathf.Sin(Mathf.PI * t / 0.25f) : 0f) + Clack(t, 0.25f, 900f, 30f, 0.9f); });
            case "flesh":
                // Bala entrando na carne: baque abafado e molhado.
                return Make(id, 0.16f, t =>
                {
                    lp += (Noise() - lp) * 0.09f;
                    return lp * Mathf.Exp(-t * 30f) * 3.2f + Sweep(t, 190f, 70f, 0.16f) * Mathf.Exp(-t * 34f) * 0.6f;
                });
            case "ult":
                return Make(id, 0.9f, t => (Sweep(t, 110f, 880f, 0.9f) + Sweep(t, 165f, 1320f, 0.9f) * 0.5f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)) * 0.4f);
            case "step":
                return Make(id, 0.07f, t =>
                {
                    lp += (Noise() - lp) * 0.2f;
                    return lp * Mathf.Exp(-t * 60f) * 0.9f;
                });
            case "music":
                return BuildMusic();
            case "rain":
                return BuildRain();
        }
        return Make(id, 0.05f, t => 0f);
    }

    // Estalo de metal: ruido curto + ressonancia amortecida.
    static float Clack(float t, float at, float freq, float decay, float amp)
    {
        float u = t - at;
        if (u < 0f) return 0f;
        return (Noise() * Mathf.Exp(-u * decay * 3f) * 0.6f + Mathf.Sin(TAU * freq * u) * Mathf.Exp(-u * decay) * 0.5f + Mathf.Sin(TAU * freq * 2.37f * u) * Mathf.Exp(-u * decay * 1.6f) * 0.25f) * amp;
    }

    static float Saw(float f, float t) { return 2f * ((f * t) % 1f) - 1f; }

    // Trilha ambiente em loop (composicao propria), no clima de jogo de tiro tatico:
    // drone grave, pads escuros em Re menor, pulso de sub nos tempos fortes, arpejo com eco e
    // percussao discreta. A intensidade sobe e desce ao longo do loop para nao cansar.
    static AudioClip BuildMusic()
    {
        const float bpm = 84f;
        float beat = 60f / bpm, bar = beat * 4f;
        const int bars = 16;
        // Acordes (semitons acima de Re2), 4 compassos cada: Dm, Bb/D, Gm/D, A.
        int[][] chords = { new[] { 12, 15, 19 }, new[] { 12, 15, 20 }, new[] { 12, 17, 20 }, new[] { 14, 19, 23 } };
        int[] roots = { 0, -4, -7, -5 };
        int n = (int)(bars * bar * SR);
        float[] d = new float[n];
        int dly = (int)(beat * 0.75f * SR);
        float[] echo = new float[dly];
        int ep = 0;
        float lp = 0f, arpLp = 0f;
        const float xf = 0.8f;   // troca de acorde suave

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float barF = t / bar;
            int b = Mathf.Min(bars - 1, (int)barF);
            int ci = (b / 4) % 4, prev = (ci + 3) % 4;
            float inChord = t - (b / 4) * 4f * bar;
            float w = Mathf.SmoothStep(0f, 1f, inChord / xf);

            // Pad: serras levemente desafinadas, filtradas com abertura lenta.
            float pad = PadChord(chords[ci], t) * w + (w < 1f ? PadChord(chords[prev], t) * (1f - w) : 0f);
            float cut = 0.018f + 0.012f * Mathf.Sin(TAU * t / 11.4f) + 0.006f * Mathf.Sin(TAU * t / 3.7f);
            lp += (pad - lp) * cut;
            float padOut = lp * 0.33f;

            // Drone na tonica do acorde.
            float fr = 73.42f * Mathf.Pow(2f, roots[ci] / 12f), fp = 73.42f * Mathf.Pow(2f, roots[prev] / 12f);
            float drone = (Mathf.Sin(TAU * fr * t) * 0.24f + Mathf.Sin(TAU * fr * 2f * t) * 0.07f) * w
                        + (Mathf.Sin(TAU * fp * t) * 0.24f + Mathf.Sin(TAU * fp * 2f * t) * 0.07f) * (1f - w);

            // Intensidade: 0-3 so pad; 4-7 entra o pulso; 8-14 arpejo e percussao; 15 esvazia de volta.
            float tail = b == bars - 1 ? 1f - (barF - b) : 1f;
            float pulseOn = b >= 4 ? Mathf.Clamp01((barF - 4f) * 2f) * tail : 0f;
            float arpOn = b >= 8 ? Mathf.Clamp01((barF - 8f) * 1.5f) * tail : 0f;

            float tb = t % beat;
            int beatIdx = (int)(t / beat) % 4;
            float kick = 0f;
            if (pulseOn > 0f && (beatIdx == 0 || beatIdx == 2))
                kick = Mathf.Sin(TAU * (44f * tb + 1.8f * (1f - Mathf.Exp(-tb * 40f)))) * Mathf.Exp(-tb * 7f) * 0.42f * pulseOn;

            float t16 = t % (beat / 4f);
            float nz = Noise();
            int s16 = (int)(t / (beat / 4f)) % 4;
            float tick = arpOn > 0f ? (nz - lastNz) * Mathf.Exp(-t16 * 70f) * (s16 == 2 ? 0.06f : 0.025f) * arpOn : 0f;
            lastNz = nz;

            // Arpejo em colcheias sobre as notas do acorde, uma oitava acima, com eco.
            float t8 = t % (beat / 2f);
            int k8 = (int)(t / (beat / 2f)) % 8;
            int[] pat = { 0, 1, 2, 1, 0, 2, 1, 2 };
            float fa = 73.42f * Mathf.Pow(2f, (chords[ci][pat[k8]] + 12) / 12f);
            float tri = 1f - 4f * Mathf.Abs(((fa * t) % 1f) - 0.5f);
            float pluck = tri * Mathf.Exp(-t8 * 7f) * 0.16f * arpOn;
            arpLp += (pluck - arpLp) * 0.25f;
            float wet = echo[ep];
            echo[ep] = arpLp + wet * 0.38f;
            ep = (ep + 1) % dly;

            d[i] = padOut + drone + kick + tick + arpLp + wet * 0.5f;
        }
        // Normaliza e emenda o fim no comeco (sem clique na volta do loop).
        float peak = 0.001f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        float g = 0.85f / peak;
        int fade = SR / 2;
        for (int i = 0; i < n; i++) d[i] *= g;
        for (int i = 0; i < fade; i++) { float a = i / (float)fade; d[i] = d[i] * a + d[n - fade + i] * (1f - a); }
        float[] o = new float[n - fade];
        System.Array.Copy(d, 0, o, 0, n - fade);
        AudioClip c = AudioClip.Create("music", o.Length, 1, SR, false);
        c.SetData(o, 0);
        return c;
    }

    static float lastNz;

    static float PadChord(int[] notes, float t)
    {
        float sum = 0f;
        for (int k = 0; k < notes.Length; k++)
        {
            float f = 73.42f * Mathf.Pow(2f, notes[k] / 12f);
            sum += (Saw(f * 1.004f, t) + Saw(f * 0.996f, t)) * 0.5f;
        }
        return sum / notes.Length;
    }

    // Chuva continua em loop (ruido filtrado em duas faixas).
    static AudioClip BuildRain()
    {
        int n = SR * 4;
        float[] d = new float[n];
        float lo = 0f, hi = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = Noise();
            lo += (w - lo) * 0.04f;
            hi += (w - hi) * 0.45f;
            d[i] = Mathf.Clamp(lo * 1.6f + (w - hi) * 0.22f, -1f, 1f);
        }
        // Emenda suave entre o fim e o comeco.
        int x = 2000;
        for (int i = 0; i < x; i++)
        {
            float k = i / (float)x;
            d[n - x + i] = d[n - x + i] * (1f - k) + d[i] * k;
        }
        AudioClip c = AudioClip.Create("rain", n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    // Sons gravados (Resources/Sfx): cada id pode ter variacoes "id_1", "id_2"... sorteadas a cada toque.
    // Tiros tem tambem a versao "id_far_N", gravada a media distancia, usada quando o disparo esta longe.
    static readonly Dictionary<string, AudioClip[]> recorded = new Dictionary<string, AudioClip[]>();
    static readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();

    static AudioClip[] Recorded(string id)
    {
        AudioClip[] arr;
        if (recorded.TryGetValue(id, out arr)) return arr;
        List<AudioClip> l = new List<AudioClip>();
        for (int i = 1; i <= 6; i++)
        {
            AudioClip c = Resources.Load<AudioClip>("Sfx/" + id + "_" + i);
            if (c == null) break;
            l.Add(c);
        }
        arr = l.ToArray();
        recorded[id] = arr;
        return arr;
    }

    static AudioClip Pick(string id)
    {
        AudioClip[] arr = Recorded(id);
        if (arr.Length == 0) return Get(id);
        int k = Random.Range(0, arr.Length), last;
        // Nunca repete a mesma variacao duas vezes seguidas (rajada soa natural).
        if (arr.Length > 1 && lastPick.TryGetValue(id, out last) && last == k) k = (k + 1) % arr.Length;
        lastPick[id] = k;
        return arr[k];
    }

    const int PoolSize = 24;
    static AudioSource[] pool3D;
    static int poolCursor;
    static AudioSource uiSource;
    static GameObject poolRoot;

    static void EnsurePool()
    {
        if (poolRoot != null && uiSource != null && pool3D != null) return;
        poolRoot = new GameObject("SfxPool");
        if (Application.isPlaying) Object.DontDestroyOnLoad(poolRoot);
        uiSource = poolRoot.AddComponent<AudioSource>();
        uiSource.spatialBlend = 0f;
        uiSource.playOnAwake = false;
        uiSource.dopplerLevel = 0f;

        pool3D = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            GameObject child = new GameObject("sfx_3d_" + i);
            child.transform.SetParent(poolRoot.transform, false);
            AudioSource a = child.AddComponent<AudioSource>();
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.dopplerLevel = 0f;
            a.playOnAwake = false;
            pool3D[i] = a;
        }
        poolCursor = 0;
    }

    static AudioSource Acquire3D()
    {
        EnsurePool();
        for (int i = 0; i < PoolSize; i++)
        {
            int idx = (poolCursor + i) % PoolSize;
            if (!pool3D[idx].isPlaying)
            {
                poolCursor = (idx + 1) % PoolSize;
                return pool3D[idx];
            }
        }
        AudioSource fallback = pool3D[poolCursor];
        poolCursor = (poolCursor + 1) % PoolSize;
        return fallback;
    }

    // Carrega as gravacoes logo na abertura, para o primeiro tiro nao engasgar.
    public static void Warm()
    {
        EnsurePool();
        foreach (string id in new[] { "rifle", "shotgun", "sniper", "rocket", "explosion" }) { Recorded(id); Recorded(id + "_far"); }
        Get("rifle"); Get("shotgun"); Get("sniper"); Get("rail"); Get("rocket");
        Get("flyby"); Get("hit"); Get("headshot"); Get("flesh"); Get("reload"); Get("empty");
    }

    const float FarFrom = 38f;   // metros: daqui em diante toca a gravacao distante

    static void Spawn(string id, Vector3 pos, float vol, float pitch, float spatial)
    {
        EnsurePool();
        bool gun = id == "rifle" || id == "shotgun" || id == "sniper" || id == "rocket" || id == "explosion" || id == "rail";
        if (spatial > 0f && gun && Camera.main != null && Recorded(id + "_far").Length > 0
            && (Camera.main.transform.position - pos).sqrMagnitude > FarFrom * FarFrom) id += "_far";
        AudioClip clip = Pick(id);
        float finalPitch = pitch * Random.Range(0.95f, 1.05f);
        float finalVol = vol * Settings.sfx;

        if (spatial <= 0f)
        {
            uiSource.pitch = finalPitch;
            uiSource.PlayOneShot(clip, finalVol);
            return;
        }

        AudioSource a = Acquire3D();
        a.transform.position = pos;
        a.clip = clip;
        a.volume = finalVol;
        a.pitch = finalPitch;
        a.spatialBlend = 1f;
        a.rolloffMode = AudioRolloffMode.Logarithmic;
        a.minDistance = id == "flyby" ? 2f : (gun ? 5f : 2.5f);
        a.maxDistance = id == "flyby" ? 30f : (gun ? 140f : 75f);
        a.dopplerLevel = 0f;
        a.Play();
    }

    public static void At(string id, Vector3 pos, float vol = 1f, float pitch = 1f) { Spawn(id, pos, vol, pitch, 1f); }
    public static void UI(string id, float vol = 1f, float pitch = 1f) { Spawn(id, Vector3.zero, vol, pitch, 0f); }
    public static void Flyby(Vector3 pos) { Spawn("flyby", pos, 0.85f, 1f, 1f); }
}
