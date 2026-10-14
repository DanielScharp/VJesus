using System.Numerics;
using VJApp.Model;

namespace VJApp.Engine;

/// <summary>
/// Gera os triângulos que o motor desenha em cada saída a partir do modelo de mapeamento.
/// Formato de vértice (12 floats): pos.xy (0..1 na saída), uv.xy (0..1 na composição),
/// st.xy (0..1 dentro do slice, para o soft-edge), edges (esq, dir, cima, baixo), gamma, opacidade.
/// </summary>
static class MappingMesh
{
    public const int FloatsPerVertex = 12;
    const int Tess = 24; // subdivisões por eixo de cada slice

    public static float[] BuildSlices(OutputScreen screen)
    {
        var v = new List<float>();
        foreach (var slice in screen.Slices)
        {
            if (!slice.Enabled || !slice.IsValid() || slice.Opacity <= 0) continue;

            var grid = new Vector2[Tess + 1, Tess + 1];
            Func<float, float, Vector2> eval = slice.Cols == 2 && slice.Rows == 2
                ? Homography(slice.Points)
                : (s, t) => CatmullRomSurface(slice, s, t);
            for (int j = 0; j <= Tess; j++)
                for (int i = 0; i <= Tess; i++)
                    grid[i, j] = eval((float)i / Tess, (float)j / Tess);

            var e = slice.SoftEdge;
            void Vert(int i, int j)
            {
                float s = (float)i / Tess, t = (float)j / Tess;
                var p = grid[i, j];
                v.AddRange([p.X, p.Y,
                    slice.Input.X + s * slice.Input.W, slice.Input.Y + t * slice.Input.H,
                    s, t, e.Left, e.Right, e.Top, e.Bottom, Math.Max(0.01f, e.Gamma), slice.Opacity]);
            }
            for (int j = 0; j < Tess; j++)
                for (int i = 0; i < Tess; i++)
                {
                    Vert(i, j); Vert(i + 1, j); Vert(i + 1, j + 1);
                    Vert(i, j); Vert(i + 1, j + 1); Vert(i, j + 1);
                }
        }
        return [.. v];
    }

    /// <summary>Máscaras viram triângulos (ear clipping) desenhados em preto.</summary>
    public static float[] BuildMasks(OutputScreen screen)
    {
        var v = new List<float>();
        foreach (var mask in screen.Masks)
        {
            if (!mask.Enabled || mask.Points.Count < 3) continue;
            var pts = mask.Points.Select(p => new Vector2(p.X, p.Y)).ToList();
            foreach (var (a, b, c) in Triangulate(pts))
                foreach (var p in new[] { a, b, c })
                    v.AddRange([p.X, p.Y, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1]);
        }
        return [.. v];
    }

    // ---------- warp de 4 cantos: homografia do quadrado unitário para o quadrilátero ----------

    static Func<float, float, Vector2> Homography(List<Pt> pts)
    {
        // ordem dos pontos: 0 = sup. esq., 1 = sup. dir., 2 = inf. esq., 3 = inf. dir.
        double x0 = pts[0].X, y0 = pts[0].Y, x1 = pts[1].X, y1 = pts[1].Y;
        double x2 = pts[3].X, y2 = pts[3].Y, x3 = pts[2].X, y3 = pts[2].Y;
        double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
        double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
        double a, b, c, d, e, f, g, h;
        double den = dx1 * dy2 - dx2 * dy1;
        if (Math.Abs(dx3) < 1e-9 && Math.Abs(dy3) < 1e-9 || Math.Abs(den) < 1e-12)
        {
            // paralelogramo: transformação afim
            a = x1 - x0; b = x2 - x1; c = x0; d = y1 - y0; e = y2 - y1; f = y0; g = 0; h = 0;
        }
        else
        {
            g = (dx3 * dy2 - dx2 * dy3) / den;
            h = (dx1 * dy3 - dx3 * dy1) / den;
            a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0;
            d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
        }
        return (s, t) =>
        {
            double w = g * s + h * t + 1;
            return new Vector2((float)((a * s + b * t + c) / w), (float)((d * s + e * t + f) / w));
        };
    }

    // ---------- malha: superfície Catmull-Rom que passa por todos os pontos de controle ----------

    static Vector2 CatmullRomSurface(Slice slice, float s, float t)
    {
        Vector2 P(int c, int r)
        {
            c = Math.Clamp(c, 0, slice.Cols - 1);
            r = Math.Clamp(r, 0, slice.Rows - 1);
            var p = slice.Points[r * slice.Cols + c];
            return new Vector2(p.X, p.Y);
        }

        float u = s * (slice.Cols - 1), w = t * (slice.Rows - 1);
        int ci = Math.Min((int)u, slice.Cols - 2), ri = Math.Min((int)w, slice.Rows - 2);
        float lu = u - ci, lw = w - ri;

        Span<Vector2> rows = stackalloc Vector2[4];
        for (int k = -1; k <= 2; k++)
            rows[k + 1] = CatmullRom(P(ci - 1, ri + k), P(ci, ri + k), P(ci + 1, ri + k), P(ci + 2, ri + k), lu);
        return CatmullRom(rows[0], rows[1], rows[2], rows[3], lw);
    }

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    // ---------- triangulação de polígono simples (ear clipping) ----------

    static IEnumerable<(Vector2, Vector2, Vector2)> Triangulate(List<Vector2> poly)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList();
        float area = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            area += p.X * q.Y - q.X * p.Y;
        }
        if (area < 0) idx.Reverse(); // garante sentido anti-horário

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0;

        int guard = 0;
        while (idx.Count > 3 && guard++ < 1000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                var a = poly[idx[(i + idx.Count - 1) % idx.Count]];
                var b = poly[idx[i]];
                var c = poly[idx[(i + 1) % idx.Count]];
                if (Cross(a, b, c) <= 0) continue; // vértice côncavo
                bool ear = true;
                foreach (var k in idx)
                {
                    var p = poly[k];
                    if (p == a || p == b || p == c) continue;
                    if (Inside(p, a, b, c)) { ear = false; break; }
                }
                if (!ear) continue;
                yield return (a, b, c);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // polígono que se cruza: desiste do resto
        }
        if (idx.Count == 3) yield return (poly[idx[0]], poly[idx[1]], poly[idx[2]]);
    }
}
