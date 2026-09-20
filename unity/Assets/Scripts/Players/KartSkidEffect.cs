using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Skid feedback when the kart hits or scrapes anything solid -- barrier
    /// walls or the other kart. Four parts:
    ///   • tells KartController to drop grip briefly so the kart really slides,
    ///   • tyre smoke off the rear wheels (and sparks against walls),
    ///   • a cartoon "poof" ring on the ground at the point of impact,
    ///   • black skid marks laid down by the rear wheels while sliding.
    /// The speed penalty for walls stays in TrackBarrier; this is feel + look.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartSkidEffect : MonoBehaviour
    {
        [Tooltip("Minimum impact speed (m/s) before a collision counts as a skid.")]
        public float minImpactSpeed = 2f;
        [Tooltip("How long grip stays reduced after an impact.")]
        public float skidDuration = 0.5f;
        // Two flat greys each puff steps through; opaque, like a drawn cloud.
        public Color smokeColor = new Color(0.92f, 0.92f, 0.94f);
        public Color smokeShade = new Color(0.72f, 0.72f, 0.78f);
        [Tooltip("Tyre smoke puffs per second per rear wheel while skidding. Few and big reads as cartoon; many and small reads as haze.")]
        public float smokeRatePerWheel = 3.5f;
        public Color sparkColor = new Color(1f, 0.9f, 0.3f);
        public Color sparkFade = new Color(1f, 0.55f, 0.1f);
        public Color skidMarkColor = new Color(0.05f, 0.05f, 0.05f, 1f);

        // Rear wheel positions in the kart cube's local space.
        private static readonly Vector3[] RearWheelLocalPositions =
        {
            new Vector3(-0.42f, -0.5f, -0.35f),
            new Vector3(0.42f, -0.5f, -0.35f),
        };

        private KartController kart;
        private Rigidbody rb;
        private ParticleSystem smoke;
        private ParticleSystem sparks;
        private ParticleSystem poof;
        private TrailRenderer[] skidMarks;
        private float smokeAccumulator;
        private Texture2D circleTexture, starTexture, ringTexture;
        private Material smokeMaterial, sparkMaterial, poofMaterial, markMaterial;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();

            circleTexture = KartParticles.HardCircle();
            starTexture = KartParticles.Star();
            ringTexture = KartParticles.Ring();
            smokeMaterial = KartParticles.CreateMaterial("KartSmoke", circleTexture);
            sparkMaterial = KartParticles.CreateMaterial("KartSparks", starTexture);
            poofMaterial = KartParticles.CreateMaterial("KartPoof", ringTexture);
            markMaterial = new Material(Shader.Find("Sprites/Default")) { name = "KartSkidMark", color = skidMarkColor };

            smoke = CreateSmoke();
            sparks = CreateSparks();
            poof = CreatePoof();

            skidMarks = new TrailRenderer[RearWheelLocalPositions.Length];
            for (int i = 0; i < skidMarks.Length; i++)
            {
                skidMarks[i] = CreateSkidMark($"SkidMark_{i}", RearWheelLocalPositions[i]);
            }
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { smokeMaterial, sparkMaterial, poofMaterial, markMaterial })
            {
                if (m != null) Destroy(m);
            }
            foreach (var t in new[] { circleTexture, starTexture, ringTexture })
            {
                if (t != null) Destroy(t);
            }
        }

        private void Update()
        {
            bool skidding = kart.IsSkidding && Mathf.Abs(kart.ForwardSpeed) > 1f;
            foreach (var trail in skidMarks)
            {
                trail.emitting = skidding;
            }

            // Tyre smoke comes off both rear wheels while sliding -- not the
            // contact point, which would put it all on the side that hit.
            if (skidding)
            {
                smokeAccumulator += smokeRatePerWheel * Time.deltaTime;
                while (smokeAccumulator >= 1f)
                {
                    smokeAccumulator -= 1f;
                    foreach (var wheel in RearWheelLocalPositions)
                    {
                        EmitSmoke(transform.TransformPoint(wheel));
                    }
                }
            }
            else
            {
                smokeAccumulator = 0f;
            }
        }

        private void EmitSmoke(Vector3 position)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position + Random.insideUnitSphere * 0.1f,
                // Slow and lazy: big puffs that drift up and hang behind the
                // wheel, rather than streaming away.
                velocity = (-transform.forward * 1.0f + Vector3.up * 0.6f + Random.insideUnitSphere * 0.4f) * Random.Range(0.7f, 1.2f),
            };
            smoke.Emit(emit, 1);
        }

        /// <summary>One flat ring on the ground at the impact that swells and vanishes.</summary>
        private void EmitPoof(Collision collision)
        {
            var contact = collision.GetContact(0);
            Vector3 ground = contact.point;
            ground.y = transform.position.y - transform.localScale.y * 0.5f + 0.05f; // kart's underside, above the road
            var emit = new ParticleSystem.EmitParams
            {
                position = ground,
                velocity = Vector3.zero,
                startSize = 1f,
                rotation = 0f,
            };
            poof.Emit(emit, 1);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (rb.isKinematic) return;
            if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

            kart.StartSkid(skidDuration);

            // A poof at the impact and a couple of puffs from each rear
            // wheel; sparks only against walls.
            EmitPoof(collision);
            foreach (var wheel in RearWheelLocalPositions)
            {
                for (int i = 0; i < 2; i++) EmitSmoke(transform.TransformPoint(wheel));
            }
            bool wall = collision.collider.GetComponent<MarioKart.World.TrackBarrier>() != null;
            if (wall) EmitSparks(collision, 8);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (rb.isKinematic || Mathf.Abs(kart.ForwardSpeed) < 3f) return;

            // Keep the slide alive while grinding, with a light trickle of
            // smoke/sparks so a long scrape stays visible without flooding.
            kart.StartSkid(0.15f); // Update() handles the smoke while skidding
            bool wall = collision.collider.GetComponent<MarioKart.World.TrackBarrier>() != null;
            if (wall) EmitSparks(collision, 1);
        }

        /// <summary>Sparks fly from the actual contact point, away from the wall.</summary>
        private void EmitSparks(Collision collision, int count)
        {
            var contact = collision.GetContact(0);
            Vector3 away = contact.normal;             // out of the surface we hit
            Vector3 backward = -transform.forward;     // trail behind the kart

            // Cosmetic jitter only. ADR 0004's WorldRandom rule is about
            // world *generation* staying deterministic; nothing here feeds
            // back into it, so UnityEngine.Random is fine.
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                emit.position = contact.point;
                emit.velocity = (away * 3f + backward * 5f + Vector3.up * 2f + Random.insideUnitSphere * 2f) * Random.Range(0.7f, 1.4f);
                sparks.Emit(emit, 1);
            }
        }

        // ------------------------------------------------------------------

        private ParticleSystem CreateSmoke()
        {
            var ps = KartParticles.CreateSystem(transform, "SkidSmoke", smokeMaterial, 128);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startColor = Color.white; // colour comes from the bands below
            main.gravityModifier = -0.05f; // drifts gently upward

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = KartParticles.Steps(smokeColor, smokeShade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.3f, peakAt: 0.15f, holdUntil: 0.6f);

            // Each puff tumbles slowly, like a drawn cloud.
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

            ps.Play();
            return ps;
        }

        private ParticleSystem CreateSparks()
        {
            var ps = KartParticles.CreateSystem(transform, "SkidSparks", sparkMaterial, 128);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
            main.startColor = Color.white;
            main.gravityModifier = 1f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = KartParticles.Steps(sparkColor, sparkFade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.PopAndShrink(overshoot: 1.4f, peakAt: 0.1f, holdUntil: 0.4f);

            // Chunky spinning stars rather than thin streaks.
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);

            ps.Play();
            return ps;
        }

        private ParticleSystem CreatePoof()
        {
            var ps = KartParticles.CreateSystem(transform, "ImpactPoof", poofMaterial, 8);
            var main = ps.main;
            main.startLifetime = 0.3f;
            main.startColor = smokeColor;
            main.gravityModifier = 0f;

            // Swells from half a metre to a couple of metres across, then is
            // simply gone -- no fade.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = KartParticles.Grow(0.5f, 2.6f);

            // Lies flat on the road.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            ps.Play();
            return ps;
        }

        private TrailRenderer CreateSkidMark(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            // TransformZ alignment lays the ribbon flat in the plane facing
            // the object's Z axis -- point Z straight up. Kart pitch/roll are
            // frozen so this stays flat on the road.
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, transform.forward);

            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = markMaterial;
            trail.alignment = LineAlignment.TransformZ;
            // Thick, solid and short-lived: a bold mark that clears quickly
            // rather than a faint one that lingers.
            trail.time = 3f;
            trail.minVertexDistance = 0.1f;
            trail.startWidth = trail.endWidth = 0.36f;
            trail.startColor = trail.endColor = skidMarkColor;
            trail.numCornerVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            return trail;
        }
    }
}
