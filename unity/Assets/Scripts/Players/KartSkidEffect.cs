using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Skid feedback when the kart hits or scrapes anything solid -- barrier
    /// walls or the other kart. Three parts:
    ///   • tells KartController to drop grip briefly so the kart really slides,
    ///   • tyre smoke at the contact point (and sparks against walls),
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
        public Color smokeColor = new Color(0.86f, 0.84f, 0.78f, 0.28f);
        [Tooltip("Tyre smoke puffs per second per rear wheel while skidding.")]
        public float smokeRatePerWheel = 8f;
        public Color sparkColor = new Color(1f, 0.85f, 0.55f);
        public Color skidMarkColor = new Color(0.28f, 0.22f, 0.16f, 0.55f);

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
        private TrailRenderer[] skidMarks;
        private float smokeAccumulator;
        private Texture2D texture;
        private Material smokeMaterial, sparkMaterial, markMaterial;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();

            texture = KartParticles.SoftCircle();
            smokeMaterial = KartParticles.CreateMaterial("KartSmoke", texture, additive: false);
            sparkMaterial = KartParticles.CreateMaterial("KartSparks", texture, additive: true);
            markMaterial = new Material(Shader.Find("Sprites/Default")) { name = "KartSkidMark", color = skidMarkColor };

            smoke = CreateSmoke();
            sparks = CreateSparks();

            skidMarks = new TrailRenderer[RearWheelLocalPositions.Length];
            for (int i = 0; i < skidMarks.Length; i++)
            {
                skidMarks[i] = CreateSkidMark($"SkidMark_{i}", RearWheelLocalPositions[i]);
            }
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { smokeMaterial, sparkMaterial, markMaterial })
            {
                if (m != null) Destroy(m);
            }
            if (texture != null) Destroy(texture);
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
                velocity = (-transform.forward * 1.5f + Vector3.up * 0.8f + Random.insideUnitSphere * 0.5f) * Random.Range(0.7f, 1.2f),
            };
            smoke.Emit(emit, 1);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (rb.isKinematic) return;
            if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

            kart.StartSkid(skidDuration);

            // A small puff from each rear wheel on impact; sparks only against walls.
            foreach (var wheel in RearWheelLocalPositions)
            {
                for (int i = 0; i < 3; i++) EmitSmoke(transform.TransformPoint(wheel));
            }
            bool wall = collision.collider.GetComponent<MarioKart.World.TrackBarrier>() != null;
            if (wall) EmitSparks(collision, 14);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (rb.isKinematic || Mathf.Abs(kart.ForwardSpeed) < 3f) return;

            // Keep the slide alive while grinding, with a light trickle of
            // smoke/sparks so a long scrape stays visible without flooding.
            kart.StartSkid(0.15f); // Update() handles the smoke while skidding
            bool wall = collision.collider.GetComponent<MarioKart.World.TrackBarrier>() != null;
            if (wall) EmitSparks(collision, 2);
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
            var ps = KartParticles.CreateSystem(transform, "SkidSmoke", smokeMaterial, 256);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startColor = smokeColor;
            main.gravityModifier = -0.05f; // drifts gently upward

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = KartParticles.FadeOut(0.2f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f)); // billows out a little

            ps.Play();
            return ps;
        }

        private ParticleSystem CreateSparks()
        {
            var ps = KartParticles.CreateSystem(transform, "SkidSparks", sparkMaterial, 256);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = sparkColor;
            main.gravityModifier = 1f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = KartParticles.FadeOut(0.5f);

            // Stretch along velocity so they read as streaks, not dots.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 1.5f;

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
            trail.time = 6f;
            trail.minVertexDistance = 0.1f;
            trail.startWidth = trail.endWidth = 0.28f;
            trail.startColor = trail.endColor = skidMarkColor;
            trail.numCornerVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            return trail;
        }
    }
}
