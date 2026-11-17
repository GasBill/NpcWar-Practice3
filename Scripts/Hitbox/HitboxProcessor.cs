using Godot;

namespace TowerDefense;

// Абстрактный обработчик типа хитбоксов.
// Все геометрические проверки собраны здесь, а наследники только реагируют
// на уже найденное пересечение. Хитбокс — прямоугольный параллелепипед,
// поэтому проверка выполняется через SAT по осям двух OBB.
public abstract partial class HitboxProcessor : Node
{
    private const float HalfExtentFactor = 0.5f;
    private const float AxisEpsilon = 0.000001f;

    public abstract void ProcessHit(HitboxObject self, HitboxObject other, Vector3 intersection);

    public static bool Intersects(HitboxObject first, HitboxObject second)
    {
        return TryGetIntersection(first, second, out _);
    }

    // Возвращает минимальный вектор, который выталкивает second из first.
    public static bool TryGetIntersection(HitboxObject first, HitboxObject second, out Vector3 intersection)
    {
        intersection = Vector3.Zero;

        if (!HasVolume(first.Size) || !HasVolume(second.Size))
        {
            return false;
        }

        Basis firstBasis = Basis.FromEuler(first.Rotation);
        Basis secondBasis = Basis.FromEuler(second.Rotation);

        Vector3 firstExtents = first.Size * HalfExtentFactor;
        Vector3 secondExtents = second.Size * HalfExtentFactor;

        Vector3 firstAxisX = firstBasis.X;
        Vector3 firstAxisY = firstBasis.Y;
        Vector3 firstAxisZ = firstBasis.Z;
        Vector3 secondAxisX = secondBasis.X;
        Vector3 secondAxisY = secondBasis.Y;
        Vector3 secondAxisZ = secondBasis.Z;

        Vector3 centerOffset = second.Position - first.Position;
        float minimumOverlap = float.PositiveInfinity;
        Vector3 minimumAxis = Vector3.Zero;

        if (!TryAxis(firstAxisX, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryAxis(firstAxisY, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryAxis(firstAxisZ, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryAxis(secondAxisX, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryAxis(secondAxisY, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryAxis(secondAxisZ, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        if (!TryCrossAxis(firstAxisX, secondAxisX, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisX, secondAxisY, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisX, secondAxisZ, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisY, secondAxisX, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisY, secondAxisY, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisY, secondAxisZ, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisZ, secondAxisX, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisZ, secondAxisY, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis)
            || !TryCrossAxis(firstAxisZ, secondAxisZ, centerOffset, firstExtents, secondExtents,
                firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
                ref minimumOverlap, ref minimumAxis))
        {
            return false;
        }

        intersection = minimumAxis * minimumOverlap;
        return true;
    }

    private static bool TryCrossAxis(
        Vector3 firstAxis,
        Vector3 secondAxis,
        Vector3 centerOffset,
        Vector3 firstExtents,
        Vector3 secondExtents,
        Vector3 firstAxisX,
        Vector3 firstAxisY,
        Vector3 firstAxisZ,
        Vector3 secondAxisX,
        Vector3 secondAxisY,
        Vector3 secondAxisZ,
        ref float minimumOverlap,
        ref Vector3 minimumAxis)
    {
        Vector3 axis = firstAxis.Cross(secondAxis);

        if (axis.LengthSquared() <= AxisEpsilon)
        {
            return true;
        }

        return TryAxis(axis.Normalized(), centerOffset, firstExtents, secondExtents,
            firstAxisX, firstAxisY, firstAxisZ, secondAxisX, secondAxisY, secondAxisZ,
            ref minimumOverlap, ref minimumAxis);
    }

    private static bool TryAxis(
        Vector3 axis,
        Vector3 centerOffset,
        Vector3 firstExtents,
        Vector3 secondExtents,
        Vector3 firstAxisX,
        Vector3 firstAxisY,
        Vector3 firstAxisZ,
        Vector3 secondAxisX,
        Vector3 secondAxisY,
        Vector3 secondAxisZ,
        ref float minimumOverlap,
        ref Vector3 minimumAxis)
    {
        float firstRadius =
            firstExtents.X * Mathf.Abs(axis.Dot(firstAxisX)) +
            firstExtents.Y * Mathf.Abs(axis.Dot(firstAxisY)) +
            firstExtents.Z * Mathf.Abs(axis.Dot(firstAxisZ));

        float secondRadius =
            secondExtents.X * Mathf.Abs(axis.Dot(secondAxisX)) +
            secondExtents.Y * Mathf.Abs(axis.Dot(secondAxisY)) +
            secondExtents.Z * Mathf.Abs(axis.Dot(secondAxisZ));

        float distance = Mathf.Abs(centerOffset.Dot(axis));
        float overlap = firstRadius + secondRadius - distance;

        if (overlap <= 0f)
        {
            return false;
        }

        if (overlap < minimumOverlap)
        {
            minimumOverlap = overlap;
            minimumAxis = centerOffset.Dot(axis) < 0f ? -axis : axis;
        }

        return true;
    }

    private static bool HasVolume(Vector3 size)
    {
        return size.X > 0f && size.Y > 0f && size.Z > 0f;
    }
}
