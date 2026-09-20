using MarioKart.Rendering;
using UnityEngine;

namespace MarioKart.Players
{
    /// <summary>
    /// Runtime kart dressing: four wheels on the unit cube body. Added from
    /// KartController so an already-built scene doesn't need a rebuild.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartVisual : MonoBehaviour
    {
        private static readonly Vector3[] WheelLocalPositions =
        {
            new Vector3(-0.48f, -0.45f, 0.38f),
            new Vector3(0.48f, -0.45f, 0.38f),
            new Vector3(-0.48f, -0.45f, -0.38f),
            new Vector3(0.48f, -0.45f, -0.38f),
        };

        private Transform[] wheels;
        private KartController kart;
        private Material wheelMaterial;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            wheelMaterial = GhibliLook.Lit(new Color(0.28f, 0.24f, 0.22f));
            wheelMaterial.name = "KartWheel";

            var body = GetComponent<Renderer>();
            if (body != null)
            {
                Color c = body.sharedMaterial != null && body.sharedMaterial.HasProperty("_Color")
                    ? body.sharedMaterial.color
                    : Color.white;
                body.sharedMaterial = GhibliLook.Lit(GhibliLook.Countryside(c));
            }

            wheels = new Transform[WheelLocalPositions.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = $"Wheel_{i}";
                var col = wheel.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                    Destroy(col);
                }
                wheel.transform.SetParent(transform, worldPositionStays: false);
                wheel.transform.localPosition = WheelLocalPositions[i];
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(0.38f, 0.12f, 0.38f);
                wheel.GetComponent<Renderer>().sharedMaterial = wheelMaterial;
                wheels[i] = wheel.transform;
            }
        }

        private void OnDestroy()
        {
            if (wheelMaterial != null) Destroy(wheelMaterial);
        }

        private void Update()
        {
            if (kart == null) return;
            float spin = kart.ForwardSpeed * 140f * Time.deltaTime;
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].Rotate(Vector3.up, spin, Space.Self);
            }
        }
    }
}
