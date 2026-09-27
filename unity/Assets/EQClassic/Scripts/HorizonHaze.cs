using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The haze at the end of the view: a ring around the camera just inside the far clip plane, in the
    /// fog's colour, opaque below the eye and fading out above it. The zone's fog turns the distance into
    /// that colour, but the clip plane then cut hills and walls sharp against a sky of another colour (at
    /// night the fog is dark and the sky is not: Mistmoore's black silhouettes on a red sky). With the ring
    /// the fogged distance melts into the sky instead. Drawn after the zone (transparent, depth tested), so
    /// whatever stands nearer covers it.
    /// </summary>
    public sealed class HorizonHaze
    {
        private const int Segments = 64;
        /// <summary>Where the ring stands, as a fraction of the far clip distance.</summary>
        private const float RadiusFraction = 0.96f;
        /// <summary>How high above the eye the haze fades out, as a fraction of the ring's radius.</summary>
        private const float FadeHeight = 0.25f;

        private GameObject _ring;
        private Material _material;

        public void Update(Camera camera, bool enabled)
        {
            if (camera == null)
                return;
            if (_ring == null)
                Build();
            if (_ring == null)
                return;
            _ring.SetActive(enabled);
            if (!enabled)
                return;
            float radius = camera.farClipPlane * RadiusFraction;
            _ring.transform.position = camera.transform.position;
            _ring.transform.localScale = new Vector3(radius, radius, radius);
            _material.color = RenderSettings.fogColor;
        }

        private void Build()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;
            // A unit cylinder: bottom at −1 and the eye's height at 0 fully opaque, clear at FadeHeight.
            float[] heights = { -1f, 0f, FadeHeight };
            float[] alphas = { 1f, 1f, 0f };
            var vertices = new Vector3[Segments * heights.Length];
            var colours = new Color[vertices.Length];
            for (int ring = 0; ring < heights.Length; ring++)
                for (int i = 0; i < Segments; i++)
                {
                    float angle = i * 2f * Mathf.PI / Segments;
                    vertices[ring * Segments + i] = new Vector3(Mathf.Cos(angle), heights[ring], Mathf.Sin(angle));
                    colours[ring * Segments + i] = new Color(1f, 1f, 1f, alphas[ring]);
                }
            var triangles = new int[(heights.Length - 1) * Segments * 6];
            int t = 0;
            for (int ring = 0; ring < heights.Length - 1; ring++)
                for (int i = 0; i < Segments; i++)
                {
                    int a = ring * Segments + i, b = ring * Segments + (i + 1) % Segments;
                    int c = a + Segments, d = b + Segments;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            var mesh = new Mesh { vertices = vertices, colors = colours, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            _ring = new GameObject("Horizon haze");
            Object.DontDestroyOnLoad(_ring);
            _ring.AddComponent<MeshFilter>().mesh = mesh;
            _material = new Material(shader); // Sprites/Default: vertex colour × _Color, no depth writes, both faces
            _ring.AddComponent<MeshRenderer>().material = _material;
        }
    }
}
