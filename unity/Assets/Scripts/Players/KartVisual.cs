using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Cartoon go-kart body built from primitive meshes at runtime, replacing
    /// the bare cube the scene builder makes each kart from. The cube stays
    /// as the physics body (its BoxCollider + Rigidbody are untouched); only
    /// its renderer is switched off and a `Visual` child holds the parts.
    ///
    /// Reads KartController every frame to animate: wheels spin with speed
    /// and the front pair yaws with steering, the chassis leans out of
    /// corners and dips/lifts under braking/acceleration, and the engine
    /// gives a faint idle shake. Body colour comes from the kart's own
    /// material so P1/P2 stay distinct; the rest is toon-styled via
    /// ToonStyle so it picks up the outline and shadow band like everything
    /// else in the world.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartVisual : MonoBehaviour
    {
        [Header("Wheels")]
        [Tooltip("Max front-wheel yaw at full steering input, degrees.")]
        public float maxSteerAngle = 28f;

        [Header("Chassis lean")]
        [Tooltip("Roll away from the corner at full steer and top speed, degrees.")]
        public float maxRoll = 7f;
        [Tooltip("Pitch per m/s² of longitudinal acceleration, degrees (nose lifts when accelerating).")]
        public float pitchPerAcceleration = 0.25f;
        public float maxPitch = 4f;
        [Tooltip("How quickly lean follows the input. Higher = snappier.")]
        public float leanResponse = 8f;
        [Tooltip("Idle engine shake amplitude in metres (0 = off).")]
        public float engineShake = 0.004f;

        [Header("Colours")]
        public Color trimColor = new Color(0.16f, 0.16f, 0.19f);   // tyres, seat, engine, visor
        public Color metalColor = new Color(0.82f, 0.84f, 0.86f);  // hubs, exhausts, steering wheel

        private const float FrontWheelRadius = 0.28f;
        private const float RearWheelRadius = 0.32f;
        private const float WheelWidth = 0.22f;
        private const float WheelX = 0.72f;
        private const float WheelZ = 0.85f;

        private KartController kart;
        private Transform chassis;
        private Transform steeringWheel;
        private Quaternion steeringWheelTilt;
        private Transform[] frontPivots;   // yaw with steering
        private Transform[] wheelSpinners; // roll with speed
        private float[] wheelRadii;
        private float[] wheelAngles;
        private float roll, pitch;
        private float lastForwardSpeed, smoothedAcceleration;
        private Material trimMaterial, metalMaterial;

        private void Awake()
        {
            kart = GetComponent<KartController>();

            // The cube's material carries the player colour and the toon shader.
            var cubeRenderer = GetComponent<MeshRenderer>();
            Material bodyMaterial = cubeRenderer != null ? cubeRenderer.sharedMaterial : null;
            if (bodyMaterial == null) bodyMaterial = ToonStyle.Create(Color.white, name: "KartBody");
            if (cubeRenderer != null) cubeRenderer.enabled = false;

            trimMaterial = ToonStyle.Create(trimColor, name: "KartTrim");
            metalMaterial = ToonStyle.Create(metalColor, name: "KartMetal");

            // The root is non-uniformly scaled (1.6 × 0.6 × 2.6); undo that
            // here so rotated parts underneath are not sheared, and so all
            // sizes below are plain metres.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            Vector3 s = transform.localScale;
            visual.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

            BuildChassis(visual, bodyMaterial);
            BuildWheels(visual);
        }

        private void OnDestroy()
        {
            if (trimMaterial != null) Destroy(trimMaterial);
            if (metalMaterial != null) Destroy(metalMaterial);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed = kart.ForwardSpeed;
            float steer = kart.Steering;
            float speedFraction = Mathf.Clamp(speed / Mathf.Max(0.01f, kart.maxSpeed), -1f, 1f);
            float follow = 1f - Mathf.Exp(-leanResponse * dt);

            // ---- Wheels: spin with distance travelled, front pair steers ----
            float distance = speed * dt;
            for (int i = 0; i < wheelSpinners.Length; i++)
            {
                wheelAngles[i] = Mathf.Repeat(wheelAngles[i] + distance / wheelRadii[i] * Mathf.Rad2Deg, 360f);
                wheelSpinners[i].localRotation = Quaternion.Euler(wheelAngles[i], 0f, 0f);
            }
            var steerRotation = Quaternion.Euler(0f, steer * maxSteerAngle, 0f);
            foreach (var pivot in frontPivots) pivot.localRotation = steerRotation;
            steeringWheel.localRotation = steeringWheelTilt * Quaternion.Euler(0f, steer * 90f, 0f);

            // ---- Chassis: lean out of the corner, pitch with acceleration ----
            float acceleration = (speed - lastForwardSpeed) / dt;
            lastForwardSpeed = speed;
            smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, acceleration, follow);

            // +Z-rotation tips the roof to the driver's right, so steering
            // right (+1) needs a negative roll to lean left, out of the turn.
            float targetRoll = -steer * Mathf.Abs(speedFraction) * maxRoll;
            // +X-rotation dips the nose, so accelerating (a > 0) lifts it.
            float targetPitch = Mathf.Clamp(-smoothedAcceleration * pitchPerAcceleration, -maxPitch, maxPitch);
            roll = Mathf.Lerp(roll, targetRoll, follow);
            pitch = Mathf.Lerp(pitch, targetPitch, follow);
            chassis.localRotation = Quaternion.Euler(pitch, 0f, roll);

            chassis.localPosition = new Vector3(0f, engineShake * Mathf.Sin(Time.time * 47f), 0f);
        }

        // ------------------------------------------------------------------
        // Construction. All positions are metres relative to the collider
        // centre; the collider spans ±0.8 × ±0.3 × ±1.3, so y = -0.3 is the
        // road surface.
        // ------------------------------------------------------------------

        private void BuildChassis(Transform visual, Material body)
        {
            chassis = new GameObject("Chassis").transform;
            chassis.SetParent(visual, false);

            // Tub and nose
            Part("Tub", Cube, chassis, body, new Vector3(0f, -0.12f, 0.05f), Vector3.zero, new Vector3(1.15f, 0.22f, 2.0f));
            Part("Nose", Cube, chassis, body, new Vector3(0f, 0.0f, 0.95f), new Vector3(-8f, 0f, 0f), new Vector3(0.75f, 0.18f, 0.6f));
            Part("Bumper", Cube, chassis, trimMaterial, new Vector3(0f, -0.15f, 1.27f), Vector3.zero, new Vector3(0.95f, 0.1f, 0.1f));
            Part("SidePod_L", Cube, chassis, body, new Vector3(-0.62f, -0.1f, -0.1f), Vector3.zero, new Vector3(0.22f, 0.16f, 0.9f));
            Part("SidePod_R", Cube, chassis, body, new Vector3(0.62f, -0.1f, -0.1f), Vector3.zero, new Vector3(0.22f, 0.16f, 0.9f));

            // Engine and exhausts (the exhaust flame particles sit just behind these)
            Part("Engine", Cube, chassis, trimMaterial, new Vector3(0f, 0.1f, -0.8f), Vector3.zero, new Vector3(0.6f, 0.3f, 0.45f));
            Part("Exhaust_L", Cylinder, chassis, metalMaterial, new Vector3(-0.48f, -0.12f, -1.18f), new Vector3(90f, 0f, 0f), new Vector3(0.14f, 0.2f, 0.14f));
            Part("Exhaust_R", Cylinder, chassis, metalMaterial, new Vector3(0.48f, -0.12f, -1.18f), new Vector3(90f, 0f, 0f), new Vector3(0.14f, 0.2f, 0.14f));

            // Rear wing
            Part("Spoiler", Cube, chassis, body, new Vector3(0f, 0.4f, -1.05f), new Vector3(-6f, 0f, 0f), new Vector3(1.2f, 0.05f, 0.32f));
            Part("SpoilerPost_L", Cube, chassis, trimMaterial, new Vector3(-0.4f, 0.28f, -1.05f), Vector3.zero, new Vector3(0.06f, 0.24f, 0.06f));
            Part("SpoilerPost_R", Cube, chassis, trimMaterial, new Vector3(0.4f, 0.28f, -1.05f), Vector3.zero, new Vector3(0.06f, 0.24f, 0.06f));

            // Driver
            Part("Seat", Cube, chassis, trimMaterial, new Vector3(0f, 0.2f, -0.5f), new Vector3(-10f, 0f, 0f), new Vector3(0.55f, 0.42f, 0.1f));
            Part("Torso", Cube, chassis, body, new Vector3(0f, 0.14f, -0.28f), Vector3.zero, new Vector3(0.42f, 0.3f, 0.28f));
            Part("Helmet", Sphere, chassis, body, new Vector3(0f, 0.46f, -0.28f), Vector3.zero, Vector3.one * 0.4f);
            Part("Visor", Cube, chassis, trimMaterial, new Vector3(0f, 0.46f, -0.11f), Vector3.zero, new Vector3(0.28f, 0.12f, 0.1f));

            // Steering wheel: a disc tilted to face the driver, spun by input
            steeringWheelTilt = Quaternion.Euler(-60f, 0f, 0f);
            steeringWheel = new GameObject("SteeringWheel").transform;
            steeringWheel.SetParent(chassis, false);
            steeringWheel.localPosition = new Vector3(0f, 0.24f, 0.12f);
            steeringWheel.localRotation = steeringWheelTilt;
            Part("Rim", Cylinder, steeringWheel, metalMaterial, Vector3.zero, Vector3.zero, new Vector3(0.3f, 0.015f, 0.3f));
            Part("Spoke", Cube, steeringWheel, trimMaterial, Vector3.zero, Vector3.zero, new Vector3(0.28f, 0.03f, 0.05f));
            Part("Column", Cylinder, chassis, trimMaterial, new Vector3(0f, 0.1f, 0.2f), new Vector3(-60f, 0f, 0f), new Vector3(0.05f, 0.15f, 0.05f));
        }

        private void BuildWheels(Transform visual)
        {
            frontPivots = new Transform[2];
            wheelSpinners = new Transform[4];
            wheelRadii = new float[4];
            wheelAngles = new float[4];

            int n = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                // Front (steers) and rear (fixed). Bottom of every tyre sits on y = -0.3.
                frontPivots[n / 2] = Wheel(visual, $"Wheel_F{(side < 0 ? 'L' : 'R')}", new Vector3(side * WheelX, FrontWheelRadius - 0.3f, WheelZ), FrontWheelRadius, n++);
                Wheel(visual, $"Wheel_R{(side < 0 ? 'L' : 'R')}", new Vector3(side * WheelX, RearWheelRadius - 0.3f, -WheelZ), RearWheelRadius, n++);
            }
        }

        /// <summary>
        /// pivot (steer yaw) → spinner (roll about X) → tyre + hub bar. The
        /// bar across the tyre face is what makes the spin readable under
        /// flat cel shading. Returns the pivot.
        /// </summary>
        private Transform Wheel(Transform parent, string name, Vector3 position, float radius, int index)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;

            var spinner = new GameObject("Spin").transform;
            spinner.SetParent(pivot, false);
            wheelSpinners[index] = spinner;
            wheelRadii[index] = radius;

            // Built-in cylinder is 2 units tall along Y, radius 0.5.
            Part("Tyre", Cylinder, spinner, trimMaterial, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(radius * 2f, WheelWidth * 0.5f, radius * 2f));
            Part("Hub", Cube, spinner, metalMaterial, Vector3.zero, Vector3.zero, new Vector3(WheelWidth + 0.02f, 0.07f, radius * 1.4f));
            return pivot;
        }

        private static MeshRenderer Part(string name, Mesh mesh, Transform parent, Material material,
            Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        // Built-in primitive meshes, without the colliders CreatePrimitive
        // would attach (extra colliders would change the kart's physics).
        private static Mesh cube, sphere, cylinder;
        private static Mesh Cube => cube != null ? cube : cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        private static Mesh Sphere => sphere != null ? sphere : sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        private static Mesh Cylinder => cylinder != null ? cylinder : cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
    }
}
