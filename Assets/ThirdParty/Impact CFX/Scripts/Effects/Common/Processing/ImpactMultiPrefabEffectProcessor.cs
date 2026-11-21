using ImpactCFX.Pooling;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Provides core implementation for effects that use multiple pooled prefabs.
    /// </summary>
    /// <typeparam name="TEffectAuthoring">The type for the script used for effect authoring.</typeparam>
    /// <typeparam name="TEffect">The type for the script used for the effect data suitable for jobs.</typeparam>
    /// <typeparam name="TEffectResult">The type for the script used for the effect result suitable for jobs.</typeparam>
    /// <typeparam name="TPool">The type of pool being used.</typeparam>
    /// <typeparam name="TPooledObject">The type of pooled object being used.</typeparam>
    public abstract class ImpactMultiPrefabEffectProcessor<TEffectAuthoring, TEffect, TEffectResult, TPool, TPooledObject> : ImpactSimpleEffectProcessor<TEffectAuthoring, TEffect, TEffectResult>
        where TEffectAuthoring : ImpactMultiPrefabEffectAuthoringBase
        where TEffect : unmanaged, IMultiPrefabEffectData<TEffectResult>
        where TEffectResult : unmanaged, IMultiPrefabEffectResult, IObjectPoolRequest
        where TPool : EffectObjectPool<TPooledObject>
        where TPooledObject : PooledEffectObjectBase
    {
        /// <summary>
        /// Event invoked when a pooled effect is played.
        /// </summary>
        public event Action<TEffectResult, TPooledObject, CollisionResultData> OnPooledEffectPlayed;

        private NativeList<int> prefabIDs;
        private bool prefabIDsListInitialized;

        private List<TPool> pools = new List<TPool>();
        private int largestPoolSize = 0;

        protected override void OnEnable()
        {
            base.OnEnable();
            initCollections();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            disposeCollections();
        }

        private void initCollections()
        {
            if (prefabIDsListInitialized)
                return;

            prefabIDs = new NativeList<int>(Allocator.Persistent);

            prefabIDsListInitialized = true;
        }

        private void disposeCollections()
        {
            if (prefabIDs.IsCreated)
                prefabIDs.Dispose();

            prefabIDsListInitialized = false;
        }

        public override void RegisterMaterial(ImpactMaterialAuthoring impactMaterial)
        {
            initCollections();
            base.RegisterMaterial(impactMaterial);
        }

        public override void RegisterMaterials(IEnumerable<ImpactMaterialAuthoring> impactMaterials)
        {
            initCollections();
            base.RegisterMaterials(impactMaterials);
        }

        protected override TEffect getEffect(TEffectAuthoring effectAuthoring)
        {
            ArrayChunk prefabArrayChunk = new ArrayChunk(prefabIDs.Length, 0);

            foreach (PooledEffectObjectBase prefab in effectAuthoring.Prefabs)
            {
                if (prefab == null)
                    continue;

                int prefabPoolID = prefab.GetPoolID();
                if (!pools.Exists(p => p.PoolID == prefabPoolID))
                {
                    GameObject g = new GameObject("Object Pool (" + prefab.name + ")");
                    DontDestroyOnLoad(g);
                    TPool pool = g.AddComponent<TPool>();
                    pool.InitializeWithTemplate(prefab as TPooledObject);

                    pools.Add(pool);

                    largestPoolSize = Mathf.Max(queueCapacity.Value, pool.PoolSize);
                    if (!queueCapacity.Override)
                        queueCapacity.Value = largestPoolSize;
                }

                prefabIDs.Add(prefabPoolID);
                prefabArrayChunk.Length++;
            }

            TEffect effect = getEffectForPooledEffect(effectAuthoring);
            effect.PrefabArrayChunk = prefabArrayChunk;

            return effect;
        }

        /// <summary>
        /// Gets effect data from the effect authoring asset.
        /// </summary>
        /// <param name="effectAuthoring">The source effect authoring asset.</param>
        /// <returns>An effect data instance.</returns>
        protected abstract TEffect getEffectForPooledEffect(TEffectAuthoring effectAuthoring);

        public override void ClearAllRegistered()
        {
            base.ClearAllRegistered();

            foreach (TPool pool in pools)
            {
                if (pool.IsAlive())
                    pool.Destroy();
            }

            pools.Clear();
        }

        public override JobHandle ScheduleProcessorJobs(NativeArray<CollisionInputData> collisionData, int collisionDataCount,
            NativeArray<MaterialCompositionData> materialCompositionData,
            NativeArray<ImpactVelocityData> velocityData,
            JobHandle dependencies)
        {
            //Schedule primary effect processing jobs
            JobHandle baseJobHandle = base.ScheduleProcessorJobs(collisionData, collisionDataCount, materialCompositionData, velocityData, dependencies);

            //Schedule job for converting prefab indices into prefab IDs usable by the object pool jobs
            ImpactMultiPrefabJob<TEffectResult> prefabJob = getMultiPoolPrefabJobBase();
            prefabJob.PrefabIDs = prefabIDs;
            prefabJob.Results = effectResults;
            JobHandle prefabJobHandle = prefabJob.Schedule(effectResults.Length, baseJobHandle);

            //Schedule jobs to get pooled objects for the effects
            NativeArray<JobHandle> objectPoolJobHandles = new NativeArray<JobHandle>(pools.Count, Allocator.Temp);

            for (int i = 0; i < pools.Count; i++)
            {
                TPool pool = pools[i];

                ObjectPoolJob<TEffectResult> objectPoolJob = getObjectPoolJobBase();
                objectPoolJob.TemplateID = pool.PoolID;
                objectPoolJob.Stealing = pool.Stealing;
                objectPoolJob.PooledObjects = pool.GetPooledObjectDataArray();
                objectPoolJob.ObjectRequests = effectResults;
                objectPoolJob.ObjectRequestCount = effectResultCount;
                objectPoolJob.CurrentFrame = Time.frameCount;

                objectPoolJobHandles[i] = objectPoolJob.Schedule(prefabJobHandle);
            }

            JobHandle combinedObjectPoolJobs = JobHandle.CombineDependencies(objectPoolJobHandles);
            objectPoolJobHandles.Dispose();

            return combinedObjectPoolJobs;
        }

        /// <summary>
        /// Gets a base instance of the object pool job.
        /// This instance does not need to have any data populated.
        /// This is needed to ensure that Burst compiles this job correctly, due to the use of generics and type constraints.
        /// </summary>
        /// <returns>An empty instance of the ObjectPoolJob.</returns>
        protected abstract ImpactMultiPrefabJob<TEffectResult> getMultiPoolPrefabJobBase();

        /// <summary>
        /// Gets a base instance of the object pool job.
        /// This instance does not need to have any data populated.
        /// This is needed to ensure that Burst compiles this job correctly, due to the use of generics and type constraints.
        /// </summary>
        /// <returns>An empty instance of the ObjectPoolJob.</returns>
        protected abstract ObjectPoolJob<TEffectResult> getObjectPoolJobBase();

        public override void ProcessResults(NativeArray<CollisionInputData> collisionDataArray,
            CollisionObjectPair[] collisionObjectPairArray,
            NativeArray<MaterialCompositionData> materialCompositionData,
            NativeArray<ImpactVelocityData> velocityData)
        {
            for (int i = 0; i < effectResultCount.Value; i++)
            {
                TEffectResult effectResult = effectResults[i];

                if (effectResult.IsEffectValid && effectResult.IsObjectPoolRequestValid && effectResult.ObjectIndex >= 0)
                {
                    CollisionInputData collision = collisionDataArray[effectResult.CollisionIndex];
                    CollisionObjectPair collisionObjectPair = collisionObjectPairArray[effectResult.CollisionIndex];
                    MaterialCompositionData materialComposition = materialCompositionData[effectResult.MaterialCompositionIndex];
                    ImpactVelocityData impactVelocityData = velocityData[effectResult.CollisionIndex];

                    PlayEffect(effectResult, collision, collisionObjectPair, materialComposition, impactVelocityData);
                }
            }
        }

        public override void PlayEffect(
            TEffectResult effectResult,
            CollisionResultData collisionResultData)
        {
            base.PlayEffect(effectResult, collisionResultData);

            if (findPool(effectResult.TemplateID, out TPool pool))
            {
                TPooledObject a = pool.RetrieveObject(effectResult.ObjectIndex, effectResult.Priority, effectResult.ContactPointID);
                PlayPooledEffect(effectResult, a, collisionResultData);
            }
        }

        /// <summary>
        /// Immediately plays an effect with a pooled object instance.
        /// This method provides uses data about the collision.
        /// </summary>
        /// <param name="effectResult">The effect result data.</param>
        /// <param name="pooledObject">The pooled object instance being used.</param>
        /// <param name="collisionData">The source collision data from which the effect was created.</param>
        /// <param name="collisionObjectPair">The pair of objects that are colliding.</param>
        /// <param name="materialCompositionData">The material composition data from the collision contact point.</param>
        /// <param name="velocityData">The velocity of the collision.</param>
        public virtual void PlayPooledEffect(
            TEffectResult effectResult,
            TPooledObject pooledObject,
            CollisionInputData collisionData,
            CollisionObjectPair collisionObjectPair,
            MaterialCompositionData materialCompositionData,
            ImpactVelocityData velocityData)
        {
            CollisionResultData collisionResultData = new CollisionResultData(collisionData, velocityData, materialCompositionData, collisionObjectPair);
            PlayPooledEffect(effectResult, pooledObject, collisionResultData);
        }

        /// <summary>
        /// Immediately plays an effect with a pooled object instance.
        /// This method uses simplified collision result data.
        /// </summary>
        /// <param name="effectResult">The effect result data.</param>
        /// <param name="pooledObject">The pooled object instance being used.</param>
        /// <param name="collisionResultData">Simplified data about the collision.</param>
        public virtual void PlayPooledEffect(TEffectResult effectResult, TPooledObject pooledObject, CollisionResultData collisionResultData)
        {
            invokePooledEffectPlayedEvent(effectResult, pooledObject, collisionResultData);
        }

        /// <summary>
        /// Invokes the <see cref=">OnPooledEffectPlayed"/> event with the given data.
        /// </summary>
        /// <param name="effectResult">The effect result data.</param>
        /// <param name="pooledObject">The pooled object instance being used.</param>
        /// <param name="collisionResultData">Data about the collision.</param>
        protected void invokePooledEffectPlayedEvent(TEffectResult effectResult, TPooledObject pooledObject, CollisionResultData collisionResultData)
        {
            OnPooledEffectPlayed?.Invoke(effectResult, pooledObject, collisionResultData);
        }

        private bool findPool(int poolID, out TPool pool)
        {
            foreach (TPool p in pools)
            {
                if (p.PoolID == poolID)
                {
                    pool = p;
                    return true;
                }
            }

            pool = default(TPool);
            return false;
        }

        public override void FixedUpdateProcessor()
        {
            foreach (TPool pool in pools)
            {
                pool.UpdatePooledObjects();
            }
        }

        public override void ResetProcessor()
        {
            foreach (TPool p in pools)
            {
                p.ReturnAllObjectsToPool();
            }
        }


        /// <summary>
        /// Scan each pool for missing objects that have been destroyed and re-instantiate them.
        /// </summary>
        public void ReinstantiateMissingPooledObjects()
        {
            foreach (TPool p in pools)
            {
                p.ReinstantiateMissingObjects();
            }
        }
    }
}