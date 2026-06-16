// Drum hit interaction for VR.
// Detects when a VR hand / controller anchor intersects the drum, squashes the
// drum with an animation curve and springs it back, cycles the drum through its
// own bright color gradient over time, and fires a one-shot burst on the
// Central Particle System tinted with the drum's current color.

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DrumHitInteraction : MonoBehaviour
{
    [Header("Hit Detection")]
    [Tooltip("VR hand / controller anchors that can strike the drum (e.g. LeftHandAnchor, RightHandAnchor). " +
             "If left empty, the script auto-finds anchors named below at startup.")]
    public Transform[] hitSources;

    [Tooltip("Names searched in the scene when 'Hit Sources' is empty.")]
    public string[] autoFindAnchorNames = { "LeftHandAnchor", "RightHandAnchor" };

    [Tooltip("How close (meters) a hand must get to the drum surface to count as a hit. " +
             "Roughly the radius of a fist/controller tip.")]
    public float contactRadius = 0.07f;

    [Tooltip("Minimum seconds between two registered hits, GLOBAL across all hands/anchors. " +
             "Prevents overlapping anchors (e.g. HandAnchor + HandAnchorDetached) from double-firing.")]
    public float hitCooldown = 0.1f;

    [Header("Squash Animation")]
    [Tooltip("Total seconds for the squash-and-return.")]
    public float squashDuration = 0.25f;

    [Tooltip("Width (X/Z) multiplier at full squash.")]
    public float squashWiden = 1.25f;

    [Tooltip("Height (Y) multiplier at full squash.")]
    public float squashFlatten = 0.6f;

    [Tooltip("Squash intensity over normalized time: 0 = rest, 1 = full squash. " +
             "Default punches in fast then eases back with a slight overshoot.")]
    public AnimationCurve squashCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.18f, 1f),
        new Keyframe(0.55f, -0.12f),
        new Keyframe(1f, 0f));

    [Header("Color")]
    [Tooltip("Bright colors the drum smoothly cycles through over time (loops back to the first). " +
             "Edit freely - any number of colors works.")]
    public Color[] gradientColors = DefaultColors();

    [Tooltip("Seconds to sweep once through all colors.")]
    public float gradientCycleDuration = 6f;

    [Tooltip("Emission glow multiplier. >1 makes the drum bloom brighter than its base color.")]
    [Range(0f, 4f)]
    public float emissionIntensity = 1.4f;

    [Header("Particles")]
    [Tooltip("Central Particle System to burst on hit. If null, auto-finds 'Central Particle System'.")]
    public ParticleSystem hitParticles;

    [Tooltip("Particle count for a hit that lands between beats (low bass energy).")]
    [Range(1, 500)]
    public int minParticlesPerHit = 120;

    [Tooltip("Particle count for a hit that lands on a strong beat (high bass energy).")]
    [Range(1, 500)]
    public int maxParticlesPerHit = 300;

    [Header("Beat Response")]
    [Tooltip("Reads live bass/beat energy. If null, auto-finds RhythmVisualizatorPro. " +
             "Used only to scale particle count - not for color.")]
    public RhythmVisualizatorPro rhythmVisualizator;

    [Tooltip("Bass energy (RhythmVisualizatorPro.RhythmAverage) at/below which a hit counts as " +
             "'not on a beat' -> min particles. 1.5 matches RVP's own beat threshold.")]
    public float beatLow = 1.5f;

    [Tooltip("Bass energy at/above which a hit counts as a strong beat -> max particles. " +
             "Tune to your track (enable Log Hit Energy to calibrate).")]
    public float beatHigh = 3.5f;

    [Tooltip("Curve sharpness. 1 = linear. >1 keeps off-beat hits near the minimum and makes the " +
             "count spike only near a real beat (the steep 'on-beat' jump).")]
    [Range(1f, 6f)]
    public float beatSharpness = 2.5f;

    [Tooltip("Log each hit's bass energy and resulting particle count, to help calibrate beatHigh.")]
    public bool logHitEnergy = false;

    // --- internals ---
    Collider m_Collider;
    CapsuleCollider m_Capsule;            // used for squash-independent detection if present
    Renderer m_Renderer;
    Material m_Material;
    Vector3 m_RestScale;
    Color m_CurrentColor = Color.white;  // latest color written to the drum this frame
    float m_SquashTimer = -1f;            // < 0 means not animating
    readonly List<bool> m_WasInside = new List<bool>();
    float m_LastHitTime = -999f;          // global: time of the last registered hit (any anchor)

    static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int s_EmissionColor = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        m_Collider = GetComponent<Collider>();
        m_Capsule = m_Collider as CapsuleCollider;
        m_Renderer = GetComponent<Renderer>();
        if (m_Renderer != null)
            m_Material = m_Renderer.material;   // per-instance material (drum already uses an instance)
        m_RestScale = transform.localScale;

        // Guard against the color list being cleared / serialized empty.
        if (gradientColors == null || gradientColors.Length == 0)
            gradientColors = DefaultColors();
    }

    // Single source of truth for the bright default palette (used by the field
    // initializer AND the Awake guard, so runtime is never white).
    static Color[] DefaultColors()
    {
        return new Color[]
        {
            new Color(1.00f, 0.20f, 0.25f), // hot pink-red
            new Color(1.00f, 0.55f, 0.00f), // orange
            new Color(1.00f, 0.90f, 0.10f), // amber
            new Color(0.25f, 1.00f, 0.35f), // green
            new Color(0.10f, 0.85f, 1.00f), // cyan
            new Color(0.45f, 0.30f, 1.00f), // violet
        };
    }

    void Start()
    {
        if (hitParticles == null)
        {
            var go = GameObject.Find("Central Particle System");
            if (go != null)
                hitParticles = go.GetComponent<ParticleSystem>();
        }

        if (rhythmVisualizator == null)
            rhythmVisualizator = FindObjectOfType<RhythmVisualizatorPro>();

        if (hitSources == null || hitSources.Length == 0)
            AutoFindHitSources();

        int n = hitSources != null ? hitSources.Length : 0;
        for (int i = 0; i < n; i++)
            m_WasInside.Add(false);
    }

    void AutoFindHitSources()
    {
        var found = new List<Transform>();
        foreach (var anchorName in autoFindAnchorNames)
        {
            var go = GameObject.Find(anchorName);
            if (go != null)
                found.Add(go.transform);
        }
        hitSources = found.ToArray();
    }

    void Update()
    {
        UpdateColor();
        DetectHits();
        UpdateSquash();
    }

    // Smoothly cycle through gradientColors over time, looping the last back to the first.
    Color GetDrumColor()
    {
        int n = gradientColors != null ? gradientColors.Length : 0;
        if (n == 0) return Color.white;
        if (n == 1) return gradientColors[0];

        float u = gradientCycleDuration > 0f
            ? Mathf.Repeat(Time.time / gradientCycleDuration, 1f)
            : 0f;
        float scaled = u * n;                 // 0 .. n
        int i = Mathf.FloorToInt(scaled) % n;
        int j = (i + 1) % n;
        float f = scaled - Mathf.Floor(scaled);
        return Color.Lerp(gradientColors[i], gradientColors[j], f);
    }

    void UpdateColor()
    {
        Color c = GetDrumColor();
        c.a = 1f;
        m_CurrentColor = c;

        if (m_Material != null)
        {
            m_Material.SetColor(s_BaseColor, c);
            m_Material.SetColor(s_EmissionColor, c * emissionIntensity);
        }
    }

    void DetectHits()
    {
        if (hitSources == null)
            return;

        for (int i = 0; i < hitSources.Length; i++)
        {
            Transform src = hitSources[i];
            if (src == null)
            {
                m_WasInside[i] = false;
                continue;
            }

            bool inside = DistanceToRestSurface(src.position) <= contactRadius;

            if (inside && !m_WasInside[i] && Time.time - m_LastHitTime >= hitCooldown)
            {
                m_LastHitTime = Time.time;
                OnHit();
            }

            m_WasInside[i] = inside;
        }
    }

    // Distance from a world point to the drum's collider surface, evaluated at the drum's
    // REST scale (using live position/rotation, which don't change during the squash).
    // This keeps the detection surface fixed so the squash deformation can't move it under
    // a stationary hand (which would otherwise self-trigger repeatedly).
    float DistanceToRestSurface(Vector3 worldPoint)
    {
        if (m_Capsule == null)
        {
            // Non-capsule collider: fall back to the live collider (may shift with squash).
            return Vector3.Distance(m_Collider.ClosestPoint(worldPoint), worldPoint);
        }

        float s = m_RestScale.x;   // drum scales uniformly
        Vector3 center = transform.position + transform.rotation * Vector3.Scale(m_Capsule.center, m_RestScale);
        float radius = m_Capsule.radius * s;
        float height = Mathf.Max(m_Capsule.height * s, radius * 2f);

        Vector3 axisLocal = m_Capsule.direction == 0 ? Vector3.right
                          : m_Capsule.direction == 2 ? Vector3.forward
                          : Vector3.up;
        Vector3 axis = transform.rotation * axisLocal;

        float half = height * 0.5f - radius;          // distance from center to each cap sphere
        Vector3 a = center + axis * half;
        Vector3 b = center - axis * half;

        return Mathf.Max(DistancePointSegment(worldPoint, a, b) - radius, 0f);
    }

    static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
        t = Mathf.Clamp01(t);
        return Vector3.Distance(p, a + ab * t);
    }

    // Public so it can be wired to other triggers (e.g. a beat) if desired.
    public void OnHit()
    {
        m_SquashTimer = 0f;          // (re)start the squash animation
        EmitParticles();
    }

    void UpdateSquash()
    {
        if (m_SquashTimer < 0f)
            return;

        m_SquashTimer += Time.deltaTime;
        float t = squashDuration > 0f ? m_SquashTimer / squashDuration : 1f;

        if (t >= 1f)
        {
            transform.localScale = m_RestScale;
            m_SquashTimer = -1f;
            return;
        }

        float k = squashCurve.Evaluate(t);     // 0 = rest, 1 = full squash
        float widen = Mathf.LerpUnclamped(1f, squashWiden, k);
        float flatten = Mathf.LerpUnclamped(1f, squashFlatten, k);
        transform.localScale = new Vector3(
            m_RestScale.x * widen,
            m_RestScale.y * flatten,
            m_RestScale.z * widen);
    }

    void EmitParticles()
    {
        if (hitParticles == null)
            return;

        // Tint only THIS hit's particles with the drum color. RVP keeps ownership of the
        // Central Particle System's color for its own automatic on-beat bursts.
        var main = hitParticles.main;
        main.startColor = m_CurrentColor;
        hitParticles.Emit(ComputeParticleCount());
    }

    // Particle count scales with how "on-beat" the hit is. Off-beat hits land near
    // minParticlesPerHit; a strong beat spikes toward maxParticlesPerHit. The Pow()
    // curve keeps weak signals flat and makes the rise steep only near a real beat.
    int ComputeParticleCount()
    {
        if (rhythmVisualizator == null)
            return minParticlesPerHit;

        float energy = rhythmVisualizator.RhythmAverage;
        float p = Mathf.Clamp01(Mathf.InverseLerp(beatLow, beatHigh, energy));
        float shaped = Mathf.Pow(p, beatSharpness);
        int count = Mathf.RoundToInt(Mathf.Lerp(minParticlesPerHit, maxParticlesPerHit, shaped));

        if (logHitEnergy)
            Debug.Log($"[Drum] beat energy {energy:F2} (p={p:F2}) -> {count} particles");

        return count;
    }

    void OnDisable()
    {
        // Make sure the drum is left at rest if disabled mid-squash.
        if (m_SquashTimer >= 0f)
        {
            transform.localScale = m_RestScale;
            m_SquashTimer = -1f;
        }
    }
}
