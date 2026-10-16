using CodeWalker.GameFiles;
using SharpDX;

namespace S4ClothesRenderer;

public sealed class Rig
{
    private const float HangDegrees = 10;
    private const float ElbowDegrees = 12;
    private static readonly Vector3 Front = -Vector3.UnitY;

    private readonly Matrix[] skin;
    private readonly bool[] moved;

    private Rig(Matrix[] skin)
    {
        this.skin = skin;
        moved = [.. skin.Select(m => !m.IsIdentity)];
    }

    public static Rig ArmsDown(Skeleton skeleton)
    {
        var bones = skeleton?.Bones?.Items;
        if (bones == null) return null;
        var rest = bones.Select(b => Matrix.Invert(b.BindTransformInv)).ToArray();
        var skin = Enumerable.Repeat(Matrix.Identity, bones.Length).ToArray();
        int Find(string name) => Array.FindIndex(bones, b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        foreach (var side in new[] { "L", "R" })
        {
            int upper = Find($"SKEL_{side}_UpperArm"), fore = Find($"SKEL_{side}_Forearm"), hand = Find($"SKEL_{side}_Hand");
            if (upper < 0 || fore < 0 || hand < 0) continue;
            Vector3 shoulder = rest[upper].TranslationVector, elbow = rest[fore].TranslationVector, wrist = rest[hand].TranslationVector;
            var bend = Toward(elbow, Vector3.Normalize(Vector3.Cross(wrist - elbow, Front)), ElbowDegrees, wrist, Front);
            var arm = elbow - shoulder;
            var raised = MathF.Atan2(MathF.Abs(arm.X), -arm.Z) * 180 / MathF.PI;
            var lower = Toward(shoulder, Vector3.UnitY, raised - HangDegrees, elbow, -Vector3.UnitZ);
            for (var i = 0; i < bones.Length; i++)
            {
                if (Under(bones, i, fore)) skin[i] = bend * lower;
                else if (Under(bones, i, upper)) skin[i] = lower;
            }
        }
        return new Rig(skin);
    }

    private static Matrix Toward(Vector3 pivot, Vector3 axis, float degrees, Vector3 point, Vector3 direction)
    {
        var angle = degrees * MathF.PI / 180;
        Matrix Rotation(float a) => Matrix.Translation(-pivot) * Matrix.RotationAxis(axis, a) * Matrix.Translation(pivot);
        var plus = Vector3.Dot(Vector3.TransformCoordinate(point, Rotation(angle)) - point, direction);
        var minus = Vector3.Dot(Vector3.TransformCoordinate(point, Rotation(-angle)) - point, direction);
        return Rotation(plus >= minus ? angle : -angle);
    }

    private static bool Under(Bone[] bones, int index, int ancestor)
    {
        for (; index >= 0; index = bones[index].ParentIndex)
        {
            if (index == ancestor) return true;
        }
        return false;
    }

    public void Apply(List<Mesh> meshes)
    {
        foreach (var m in meshes)
        {
            if (m.Bones == null) continue;
            var n = m.Positions.Length / 3;
            for (var i = 0; i < n; i++)
            {
                var blend = new Matrix();
                var total = 0f;
                var any = false;
                for (var k = 0; k < 4; k++)
                {
                    var w = m.Weights[i * 4 + k];
                    var b = m.Bones[i * 4 + k];
                    if (w <= 0 || b >= skin.Length) continue;
                    blend += skin[b] * w;
                    total += w;
                    any |= moved[b];
                }
                if (!any) continue;
                blend *= 1 / total;
                var p = Vector3.TransformCoordinate(new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]), blend);
                m.Positions[i * 3] = p.X; m.Positions[i * 3 + 1] = p.Y; m.Positions[i * 3 + 2] = p.Z;
                if (!m.HasNormals) continue;
                var nv = Vector3.Normalize(Vector3.TransformNormal(new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]), blend));
                m.Normals[i * 3] = nv.X; m.Normals[i * 3 + 1] = nv.Y; m.Normals[i * 3 + 2] = nv.Z;
            }
        }
    }
}
