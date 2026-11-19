using System.Collections.Generic;
using UnityEngine;

// Todo o audio e sintetizado por codigo: tiros, explosoes, interface e a trilha ambiente.
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
            case "rifle":
                return Make(id, 0.2f, t => Noise() * Mathf.Exp(-t * 38f) * 0.7f + Sweep(t, 220f, 60f, 0.2f) * Mathf.Exp(-t * 26f) * 0.6f);
            case "shotgun":
                return Make(id, 0.45f, t =>
                {
                    lp += (Noise() - lp) * 0.35f;
                    return lp * Mathf.Exp(-t * 11f) * 1.3f + Sweep(t, 140f, 40f, 0.45f) * Mathf.Exp(-t * 14f) * 0.7f;
                });
            case "sniper":
                // Estampido seco + estrondo grave + eco do disparo no vale.
                return Make(id, 1.4f, t =>
                {
                    lp += (Noise() - lp) * 0.5f;
                    float crack = Noise() * Mathf.Exp(-t * 70f) * 1.2f;
                    float boom = Sweep(t, 160f, 38f, 0.5f) * Mathf.Exp(-t * 9f) * 0.9f;
                    float echo = t > 0.22f ? lp * Mathf.Exp(-(t - 0.22f) * 4.5f) * 0.35f : 0f;
                    return crack + boom + lp * Mathf.Exp(-t * 14f) * 0.8f + echo;
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
                    float s = Sweep(t, 2200f, 180f, 0.7f);
                    float sq = s > 0f ? 0.5f : -0.5f;
                    return (s * 0.5f + sq * 0.25f) * Mathf.Exp(-t * 6f) + Noise() * Mathf.Exp(-t * 50f) * 0.5f;
                });
            case "rocket":
                return Make(id, 0.5f, t =>
                {
                    lp += (Noise() - lp) * 0.12f;
                    return lp * Mathf.Exp(-t * 6f) * 1.6f + Sweep(t, 90f, 240f, 0.5f) * Mathf.Exp(-t * 8f) * 0.4f;
                });
            case "explosion":
                return Make(id, 1.1f, t =>
                {
                    lp += (Noise() - lp) * (0.25f * Mathf.Exp(-t * 3f) + 0.02f);
                    return lp * Mathf.Exp(-t * 4f) * 2.2f + Sweep(t, 70f, 30f, 1.1f) * Mathf.Exp(-t * 5f) * 0.8f;
                });
            case "hit":
                return Make(id, 0.06f, t => Mathf.Sin(TAU * 1300f * t) * Mathf.Exp(-t * 60f) * 0.6f);
            case "headshot":
                return Make(id, 0.12f, t => (Mathf.Sin(TAU * 1900f * t) + Mathf.Sin(TAU * 2850f * t) * 0.5f) * Mathf.Exp(-t * 35f) * 0.5f);
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
            case "pickup":
                return Make(id, 0.3f, t => Mathf.Sin(TAU * (t < 0.1f ? 523f : (t < 0.2f ? 659f : 784f)) * t) * Mathf.Exp(-t * 6f) * 0.4f);
            case "empty":
                return Make(id, 0.04f, t => Noise() * Mathf.Exp(-t * 150f) * 0.4f);
            case "reload":
                return Make(id, 0.35f, t =>
                {
                    float a = Noise() * Mathf.Exp(-t * 90f) * 0.5f;
                    float u = t - 0.22f;
                    float b = u > 0f ? Noise() * Mathf.Exp(-u * 70f) * 0.6f : 0f;
                    return a + b;
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

    static void Spawn(string id, Vector3 pos, float vol, float pitch, float spatial)
    {
        AudioClip clip = Get(id);
        GameObject g = new GameObject("sfx_" + id);
        g.transform.position = pos;
        AudioSource a = g.AddComponent<AudioSource>();
        a.clip = clip;
        a.volume = vol * Settings.sfx;
        a.pitch = pitch * Random.Range(0.96f, 1.04f);
        a.spatialBlend = spatial;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = 3f;
        a.maxDistance = 75f;
        a.Play();
        Object.Destroy(g, clip.length / Mathf.Max(0.1f, a.pitch) + 0.1f);
    }

    public static void At(string id, Vector3 pos, float vol = 1f, float pitch = 1f) { Spawn(id, pos, vol, pitch, 1f); }
    public static void UI(string id, float vol = 1f, float pitch = 1f) { Spawn(id, Vector3.zero, vol, pitch, 0f); }
}
