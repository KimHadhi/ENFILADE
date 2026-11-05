using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using RVFXJobsBurstPlaneValue = RVFX.DestructionSystem.RVFXJobsBurstFractureProvider.RVFXJobsBurstPlaneValue;
using RVFXJobsBurstBrickCellValue = RVFX.DestructionSystem.RVFXJobsBurstFractureProvider.RVFXJobsBurstBrickCellValue;
using RVFXJobsBurstDouble3 = RVFX.DestructionSystem.RVFXJobsBurstFractureProvider.RVFXJobsBurstDouble3;

namespace RVFX.DestructionSystem
{
    // This adapter ships as source so Burst/Collections code generation uses
    // the packages installed in the destination project. Core algorithms and
    // serialized component types remain in the product DLLs.
    internal static class RVFXBurstAdapter
    {
        private const float FastMinimumSquaredDistance = 1.401298E-45f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Register()
        {
            RVFXJobsBurstFractureProvider.IsBurstEnabled = () => BurstCompiler.IsEnabled;
            RVFXJobsBurstFractureProvider.BuildVoronoi = (sites, planes, count, minimum, batchSize) =>
                new RVFXVoronoiPlaneJob { sites=sites, planes=planes, siteCount=count,
                    accurateMinimumNormalSquared=minimum }.Schedule(planes.Length,batchSize).Complete();
            RVFXJobsBurstFractureProvider.BuildBricks = (cells, planes, minimum, maximum, tolerance, batchSize) =>
                new RVFXBrickPlaneJob { cells=cells, planes=planes,
                    sourceMinimum=new double3(minimum.x,minimum.y,minimum.z),
                    sourceMaximum=new double3(maximum.x,maximum.y,maximum.z),
                    safeTolerance=tolerance }.Schedule(planes.Length,batchSize).Complete();
            RVFXCPUTransformJobBridge.Apply = (positions, rotations, scales, access, offset0, offset1, blend) =>
                new PlaybackJob { positions=positions, rotations=rotations, scales=scales,
                    offset0=offset0, offset1=offset1, blend=blend }.Schedule(access).Complete();
        }

        private static RVFXJobsBurstDouble3 Pack(double3 value)
        {
            return new RVFXJobsBurstDouble3(value.x,value.y,value.z);
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High)]
        private struct RVFXVoronoiPlaneJob : IJobParallelFor
        {
            [ReadOnly]
            internal NativeArray<Vector3> sites;

            [WriteOnly]
            internal NativeArray<RVFXJobsBurstPlaneValue> planes;

            internal int siteCount;
            internal double accurateMinimumNormalSquared;

            public void Execute(int index)
            {
                int stride = siteCount - 1;
                int cellIndex = index / stride;
                int slot = index - cellIndex * stride;
                int otherIndex = slot >= cellIndex
                    ? slot + 1
                    : slot;

                float3 site = (float3)sites[cellIndex];
                float3 other = (float3)sites[otherIndex];
                float3 delta = new float3(
                    other.x - site.x,
                    other.y - site.y,
                    other.z - site.z);
                // Keep the historical Vector3.sqrMagnitude evaluation
                // order. Fast cap reconstruction is sensitive to even a
                // one-ulp plane change, so vector dot/reassociation is not
                // used here despite Burst compiling the surrounding batch.
                float squared =
                    delta.x * delta.x +
                    delta.y * delta.y +
                    delta.z * delta.z;
                double3 accurateSite = new double3(site);
                double3 accurateOther = new double3(other);
                double3 accurateDelta = accurateOther - accurateSite;
                double accurateSquared = math.lengthsq(accurateDelta);
                float fastLength =
                    squared > FastMinimumSquaredDistance
                        ? math.sqrt(squared)
                        : 0.0f;

                RVFXJobsBurstPlaneValue value =
                    new RVFXJobsBurstPlaneValue
                    {
                        fastPoint = new float3(
                            (site.x + other.x) * 0.5f,
                            (site.y + other.y) * 0.5f,
                            (site.z + other.z) * 0.5f),
                        fastNormal =
                            squared > FastMinimumSquaredDistance
                            ? new float3(
                                delta.x / fastLength,
                                delta.y / fastLength,
                                delta.z / fastLength)
                            : float3.zero,
                        fastSquaredDistance = squared,
                        accuratePoint =
                            Pack((accurateSite + accurateOther) * 0.5),
                        accurateNormal = Pack(accurateDelta),
                        otherSeedIndex = otherIndex,
                        planeIndex = slot,
                        fastValid = (byte)(
                            squared > FastMinimumSquaredDistance
                            ? 1
                            : 0),
                        accurateValid = (byte)(
                            accurateSquared >
                            accurateMinimumNormalSquared
                                ? 1
                                : 0)
                    };
                planes[index] = value;
            }
        }

        [BurstCompile(
            FloatMode = FloatMode.Strict,
            FloatPrecision = FloatPrecision.High)]
        private struct RVFXBrickPlaneJob : IJobParallelFor
        {
            [ReadOnly]
            internal NativeArray<RVFXJobsBurstBrickCellValue> cells;

            [WriteOnly]
            internal NativeArray<RVFXJobsBurstPlaneValue> planes;

            internal double3 sourceMinimum;
            internal double3 sourceMaximum;
            internal double safeTolerance;

            public void Execute(int index)
            {
                int cellIndex = index / 6;
                int planeIndex = index - cellIndex * 6;
                RVFXJobsBurstBrickCellValue cell = cells[cellIndex];
                float3 half = (float3)cell.size * 0.5f;
                float3 minimum = (float3)cell.center - half;
                float3 maximum = (float3)cell.center + half;
                float3 point = (float3)cell.center;
                float3 normal = float3.zero;

                if (planeIndex == 0)
                {
                    point.x = maximum.x;
                    normal.x = 1.0f;
                }
                else if (planeIndex == 1)
                {
                    point.x = minimum.x;
                    normal.x = -1.0f;
                }
                else if (planeIndex == 2)
                {
                    point.y = maximum.y;
                    normal.y = 1.0f;
                }
                else if (planeIndex == 3)
                {
                    point.y = minimum.y;
                    normal.y = -1.0f;
                }
                else if (planeIndex == 4)
                {
                    point.z = maximum.z;
                    normal.z = 1.0f;
                }
                else
                {
                    point.z = minimum.z;
                    normal.z = -1.0f;
                }

                double3 accurateCenter = new double3((float3)cell.center);
                double3 accurateSize = new double3((float3)cell.size);
                double3 accurateMinimum =
                    accurateCenter - accurateSize * 0.5;
                double3 accurateMaximum =
                    accurateCenter + accurateSize * 0.5;
                double3 accuratePoint = accurateCenter;
                double3 accurateNormal = double3.zero;
                bool canClip;

                if (planeIndex == 0)
                {
                    accuratePoint.x = accurateMaximum.x;
                    accurateNormal.x = 1.0;
                    canClip = accurateMaximum.x <
                        sourceMaximum.x - safeTolerance;
                }
                else if (planeIndex == 1)
                {
                    accuratePoint.x = accurateMinimum.x;
                    accurateNormal.x = -1.0;
                    canClip = accurateMinimum.x >
                        sourceMinimum.x + safeTolerance;
                }
                else if (planeIndex == 2)
                {
                    accuratePoint.y = accurateMaximum.y;
                    accurateNormal.y = 1.0;
                    canClip = accurateMaximum.y <
                        sourceMaximum.y - safeTolerance;
                }
                else if (planeIndex == 3)
                {
                    accuratePoint.y = accurateMinimum.y;
                    accurateNormal.y = -1.0;
                    canClip = accurateMinimum.y >
                        sourceMinimum.y + safeTolerance;
                }
                else if (planeIndex == 4)
                {
                    accuratePoint.z = accurateMaximum.z;
                    accurateNormal.z = 1.0;
                    canClip = accurateMaximum.z <
                        sourceMaximum.z - safeTolerance;
                }
                else
                {
                    accuratePoint.z = accurateMinimum.z;
                    accurateNormal.z = -1.0;
                    canClip = accurateMinimum.z >
                        sourceMinimum.z + safeTolerance;
                }

                bool sourceFace =
                    (cell.sourceBoundsFaceMask &
                     (1 << planeIndex)) != 0;
                planes[index] = new RVFXJobsBurstPlaneValue
                {
                    fastPoint = point,
                    fastNormal = normal,
                    fastSquaredDistance = 1.0f,
                    accuratePoint = Pack(accuratePoint),
                    accurateNormal = Pack(accurateNormal),
                    otherSeedIndex = -1,
                    planeIndex = planeIndex,
                    fastValid = 1,
                    accurateValid = (byte)(
                        !sourceFace && canClip ? 1 : 0)
                };
            }
        }

        [BurstCompile]
        private struct PlaybackJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<Vector3> positions;
            [ReadOnly] public NativeArray<Quaternion> rotations;
            [ReadOnly] public NativeArray<Vector3> scales;
            public int offset0, offset1;
            public float blend;
            public void Execute(int index, TransformAccess target)
            {
                if (!target.isValid) return;
                target.localPosition = math.lerp((float3)positions[offset0 + index], (float3)positions[offset1 + index], blend);
                target.localRotation = math.slerp((quaternion)rotations[offset0 + index], (quaternion)rotations[offset1 + index], blend);
                target.localScale = math.lerp((float3)scales[offset0 + index], (float3)scales[offset1 + index], blend);
            }
        }

    }
}
