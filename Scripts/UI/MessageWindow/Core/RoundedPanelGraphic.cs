using UnityEngine;
using UnityEngine.UI;

namespace ChatdollKit.UI.MessageWindow
{
    /// <summary>A flat rounded panel whose fill and optional border do not overlap.</summary>
    [AddComponentMenu(""), RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedPanelGraphic : MaskableGraphic
    {
        [Min(0f)] public float CornerRadius = 16f;
        [Min(0f)] public float BorderWidth;
        public Color BorderColor = new Color(1f, 1f, 1f, 0.2f);

        private const int CornerSegments = 10;
        private const int ContourVertices = (CornerSegments + 1) * 4;
        private float previousRadius;
        private float previousBorderWidth;
        private Color previousBorderColor;
        private float previousCanvasScale;

        protected override void OnEnable()
        {
            base.OnEnable();
            RememberSettings();
            SetVerticesDirty();
        }

        private void Update()
        {
            if (CornerRadius.Equals(previousRadius) && BorderWidth.Equals(previousBorderWidth) &&
                BorderColor.Equals(previousBorderColor) && CanvasScale().Equals(previousCanvasScale)) return;

            RememberSettings();
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            RememberSettings();
            SetVerticesDirty();
        }
#endif

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            var limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            var radius = ClampDimension(CornerRadius, limit);
            var borderWidth = ClampDimension(BorderWidth, limit);
            var antialiasWidth = Mathf.Min(1f / CanvasScale(), limit);
            if (borderWidth > 0f) antialiasWidth = Mathf.Min(antialiasWidth, borderWidth);

            var edgeColor = borderWidth > 0f ? BorderColor : color;
            var transparent = edgeColor;
            transparent.a = 0f;
            var outer = AddContour(vertices, rect, radius, transparent);
            var edgeRect = Inset(rect, antialiasWidth);
            var edgeRadius = Mathf.Max(0f, radius - antialiasWidth);
            var edge = AddContour(vertices, edgeRect, edgeRadius, edgeColor);
            AddRing(vertices, outer, edge);

            if (borderWidth > 0f)
            {
                var fillRect = Inset(rect, borderWidth);
                var fillRadius = Mathf.Max(0f, radius - borderWidth);
                if (borderWidth > antialiasWidth)
                {
                    var borderInside = AddContour(vertices, fillRect, fillRadius, BorderColor);
                    AddRing(vertices, edge, borderInside);
                }

                // Duplicate boundary vertices to keep fill and border colors separate,
                // without drawing the border underneath the translucent fill.
                var fill = AddContour(vertices, fillRect, fillRadius, color);
                AddFill(vertices, fill, fillRect, color);
            }
            else
            {
                AddFill(vertices, edge, edgeRect, color);
            }
        }

        private static int AddContour(VertexHelper vertices, Rect rect, float radius, Color tint)
        {
            var start = vertices.currentVertCount;
            for (var corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
                    corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
                for (var step = 0; step <= CornerSegments; step++)
                {
                    var angle = (-90f + corner * 90f + step * 90f / CornerSegments) * Mathf.Deg2Rad;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    vertices.AddVert(point, tint, Vector2.zero);
                }
            }
            return start;
        }

        private static void AddRing(VertexHelper vertices, int outside, int inside)
        {
            for (var i = 0; i < ContourVertices; i++)
            {
                var next = (i + 1) % ContourVertices;
                vertices.AddTriangle(inside + i, inside + next, outside + next);
                vertices.AddTriangle(inside + i, outside + next, outside + i);
            }
        }

        private static void AddFill(VertexHelper vertices, int contour, Rect rect, Color tint)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            var center = vertices.currentVertCount;
            vertices.AddVert(rect.center, tint, Vector2.zero);
            for (var i = 0; i < ContourVertices; i++)
            {
                vertices.AddTriangle(center, contour + (i + 1) % ContourVertices, contour + i);
            }
        }

        private void RememberSettings()
        {
            previousRadius = CornerRadius;
            previousBorderWidth = BorderWidth;
            previousBorderColor = BorderColor;
            previousCanvasScale = CanvasScale();
        }

        private float CanvasScale()
        {
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            return scale > 0f && !float.IsInfinity(scale) ? scale : 1f;
        }

        private static float ClampDimension(float value, float maximum) =>
            float.IsNaN(value) ? 0f : Mathf.Clamp(value, 0f, maximum);

        private static Rect Inset(Rect rect, float amount) =>
            Rect.MinMaxRect(rect.xMin + amount, rect.yMin + amount,
                rect.xMax - amount, rect.yMax - amount);
    }
}
