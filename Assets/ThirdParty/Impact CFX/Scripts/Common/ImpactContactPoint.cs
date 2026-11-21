using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Wraps data for a single 3D or 2D contact point.
    /// </summary>
    public struct ImpactContactPoint
    {
        /// <summary>
        /// The position of the contact point.
        /// </summary>
        public Vector3 Point;

        /// <summary>
        /// The normal of the contact.
        /// </summary>
        public Vector3 Normal;

        /// <summary>
        /// The specific collider object that triggered the collision.
        /// </summary>
        public GameObject TriggerObject;

        /// <summary>
        /// The collider object being collided with.
        /// </summary>
        public GameObject HitObject;

        /// <summary>
        /// The collision velocity provided by the collision message.
        /// </summary>
        public Vector3 CollisionMessageVelocity;

        private Collider thisCollider3D;
        private Collider2D thisCollider2D;

        private Collider otherCollider3D;
        private Collider2D otherCollider2D;

        private PhysicsType physicsType;

        #region Contact Point Constructors 

        /// <summary>
        /// Creates an impact contact point from the given <see cref="ContactPoint"/>.
        /// </summary>
        /// <param name="contactPoint3D">The contact point to get data from.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision"/>, if applicable.</param>
        /// <param name="invert">Should the trigger and hit objects be swapped?</param>
        public ImpactContactPoint(ContactPoint contactPoint3D, Vector3 collisionMessageVelocity, bool invert = false)
        {
            Point = contactPoint3D.point;
            Normal = contactPoint3D.normal;

            if (invert)
            {
                thisCollider3D = contactPoint3D.otherCollider;
                thisCollider2D = default(Collider2D);

                otherCollider3D = contactPoint3D.thisCollider;
                otherCollider2D = default(Collider2D);

                TriggerObject = otherCollider3D.gameObject;
                HitObject = thisCollider3D.gameObject;
            }
            else
            {
                thisCollider3D = contactPoint3D.thisCollider;
                thisCollider2D = default(Collider2D);

                otherCollider3D = contactPoint3D.otherCollider;
                otherCollider2D = default(Collider2D);

                TriggerObject = thisCollider3D.gameObject;
                HitObject = otherCollider3D.gameObject;
            }

            physicsType = PhysicsType.Physics3D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        /// <summary>
        /// Creates an impact contact point from the given <see cref="ContactPoint2D"/>.
        /// </summary>
        /// <param name="contactPoint2D">The contact point to get data from.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision2D"/>, if applicable.</param>
        /// <param name="invert">Should the trigger and hit objects be swapped?</param>
        public ImpactContactPoint(ContactPoint2D contactPoint2D, Vector3 collisionMessageVelocity, bool invert = false)
        {
            Point = contactPoint2D.point;
            Normal = contactPoint2D.normal;

            if (invert)
            {
                thisCollider2D = contactPoint2D.collider;
                thisCollider3D = default(Collider);

                otherCollider2D = contactPoint2D.otherCollider;
                otherCollider3D = default(Collider);

                TriggerObject = otherCollider2D.gameObject;
                HitObject = thisCollider2D.gameObject;
            }
            else
            {
                thisCollider2D = contactPoint2D.otherCollider;
                thisCollider3D = default(Collider);

                otherCollider2D = contactPoint2D.collider;
                otherCollider3D = default(Collider);

                TriggerObject = thisCollider2D.gameObject;
                HitObject = otherCollider2D.gameObject;
            }

            physicsType = PhysicsType.Physics2D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        #endregion

        #region Raycast Hit Constructors

        /// <summary>
        /// Creates an impact contact point from the given <see cref="RaycastHit"/>.
        /// </summary>
        /// <param name="raycastHit3D">The raycast hit data.</param>
        /// <param name="invert">Should the trigger and hit objects be swapped?</param>
        /// <param name="sourceObject">An optional source object for the raycast.</param>
        public ImpactContactPoint(RaycastHit raycastHit3D, bool invert, GameObject sourceObject = null)
        {
            Point = raycastHit3D.point;
            Normal = raycastHit3D.normal;

            if (invert)
            {
                thisCollider3D = raycastHit3D.collider;
                otherCollider3D = default(Collider);

                TriggerObject = thisCollider3D.gameObject;
                HitObject = sourceObject;
            }
            else
            {
                thisCollider3D = default(Collider);
                otherCollider3D = raycastHit3D.collider;

                TriggerObject = sourceObject;
                HitObject = otherCollider3D.gameObject;
            }

            thisCollider2D = default(Collider2D);
            otherCollider2D = default(Collider2D);

            physicsType = PhysicsType.Physics3D;

            CollisionMessageVelocity = Vector3.zero;
        }

        /// <summary>
        /// Creates an impact contact point from the given <see cref="RaycastHit2D"/>.
        /// </summary>
        /// <param name="raycastHit2D">The raycast hit data.</param>
        /// <param name="invert">Should the trigger and hit objects be swapped?</param>
        /// <param name="sourceObject">An optional source object for the raycast.</param>
        public ImpactContactPoint(RaycastHit2D raycastHit2D, bool invert, GameObject sourceObject = null)
        {
            Point = raycastHit2D.point;
            Normal = raycastHit2D.normal;

            if (invert)
            {
                thisCollider2D = raycastHit2D.collider;
                otherCollider2D = default(Collider2D);

                TriggerObject = thisCollider2D.gameObject;
                HitObject = sourceObject;
            }
            else
            {
                thisCollider2D = default(Collider2D);
                otherCollider2D = raycastHit2D.collider;

                TriggerObject = sourceObject;
                HitObject = otherCollider2D.gameObject;
            }

            thisCollider3D = default(Collider);
            otherCollider3D = default(Collider);

            physicsType = PhysicsType.Physics2D;

            CollisionMessageVelocity = Vector3.zero;
        }

        #endregion

        #region 3D Constructors

        /// <summary>
        /// Creates a contact point using the given data.
        /// </summary>
        /// <param name="point">The contact point.</param>
        /// <param name="normal">The contact normal.</param>
        /// <param name="triggerObject">The object that triggered the collision.</param>
        /// <param name="hitCollider3D">The collider that was hit.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision"/>, if applicable.</param>
        public ImpactContactPoint(Vector3 point, Vector3 normal, GameObject triggerObject, Collider hitCollider3D, Vector3 collisionMessageVelocity)
        {
            Point = point;
            Normal = normal;

            TriggerObject = triggerObject;
            HitObject = hitCollider3D.gameObject;

            thisCollider3D = default(Collider);
            thisCollider2D = default(Collider2D);

            otherCollider3D = hitCollider3D;
            otherCollider2D = default(Collider2D);

            physicsType = PhysicsType.Physics3D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        /// <summary>
        /// Creates a contact point using the given data.
        /// </summary>
        /// <param name="point">The contact point.</param>
        /// <param name="normal">The contact normal.</param>
        /// <param name="triggerCollider3D">The collider that triggered the collision.</param>
        /// <param name="hitCollider3D">The collider that was hit.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision"/>, if applicable.</param>
        public ImpactContactPoint(Vector3 point, Vector3 normal, Collider triggerCollider3D, Collider hitCollider3D, Vector3 collisionMessageVelocity)
        {
            Point = point;
            Normal = normal;

            TriggerObject = triggerCollider3D.gameObject;
            HitObject = hitCollider3D.gameObject;

            thisCollider3D = triggerCollider3D;
            thisCollider2D = default(Collider2D);

            otherCollider3D = hitCollider3D;
            otherCollider2D = default(Collider2D);

            physicsType = PhysicsType.Physics3D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        #endregion

        #region 2D Constructors

        /// <summary>
        /// Creates a contact point using the given data.
        /// </summary>
        /// <param name="point">The contact point.</param>
        /// <param name="normal">The contact normal.</param>
        /// <param name="triggerObject">The object that triggered the collision.</param>
        /// <param name="hitCollider2D">The collider that was hit.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision"/>, if applicable.</param>
        public ImpactContactPoint(Vector3 point, Vector3 normal, GameObject triggerObject, Collider2D hitCollider2D, Vector3 collisionMessageVelocity)
        {
            Point = point;
            Normal = normal;

            TriggerObject = triggerObject;
            HitObject = hitCollider2D.gameObject;

            thisCollider3D = default(Collider);
            thisCollider2D = default(Collider2D);

            otherCollider2D = hitCollider2D;
            otherCollider3D = default(Collider);

            physicsType = PhysicsType.Physics2D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        /// <summary>
        /// Creates a contact point using the given data.
        /// </summary>
        /// <param name="point">The contact point.</param>
        /// <param name="normal">The contact normal.</param>
        /// <param name="triggerCollider2D">The collider that triggered the collision.</param>
        /// <param name="hitCollider2D">The collider that was hit.</param>
        /// <param name="collisionMessageVelocity">Optional velocity provided by the original <see cref="Collision"/>, if applicable.</param>
        public ImpactContactPoint(Vector3 point, Vector3 normal, Collider2D triggerCollider2D, Collider2D hitCollider2D, Vector3 collisionMessageVelocity)
        {
            Point = point;
            Normal = normal;

            TriggerObject = triggerCollider2D.gameObject;
            HitObject = hitCollider2D.gameObject;

            thisCollider2D = triggerCollider2D;
            thisCollider3D = default(Collider);

            otherCollider2D = hitCollider2D;
            otherCollider3D = default(Collider);

            physicsType = PhysicsType.Physics2D;

            CollisionMessageVelocity = collisionMessageVelocity;
        }

        #endregion

        /// <summary>
        /// Creates a copy of the given contact point.
        /// </summary>
        /// <param name="source">The original contact point.</param>
        /// <param name="invert">Should the trigger and hit objects be swapped?</param>
        public ImpactContactPoint(ImpactContactPoint source, bool invert = false)
        {
            Point = source.Point;
            Normal = source.Normal;

            if (invert)
            {
                TriggerObject = source.HitObject;
                HitObject = source.TriggerObject;

                thisCollider3D = source.otherCollider3D;
                thisCollider2D = source.otherCollider2D;

                otherCollider3D = source.thisCollider3D;
                otherCollider2D = source.thisCollider2D;
            }
            else
            {
                TriggerObject = source.TriggerObject;
                HitObject = source.HitObject;

                thisCollider3D = source.thisCollider3D;
                thisCollider2D = source.thisCollider2D;

                otherCollider3D = source.otherCollider3D;
                otherCollider2D = source.otherCollider2D;
            }

            physicsType = source.physicsType;

            CollisionMessageVelocity = source.CollisionMessageVelocity;
        }

        /// <summary>
        /// Creates a contact point using the given data.
        /// </summary>
        /// <param name="point">The contact point.</param>
        /// <param name="normal">The contact normal.</param>
        /// <param name="triggerObject">The object that triggered the collision.</param>
        /// <param name="physicsType">Whether this is a 3D or 2D physics contact point.</param>
        public ImpactContactPoint(Vector3 point, Vector3 normal, GameObject triggerObject, PhysicsType physicsType)
        {
            Point = point;
            Normal = normal;

            TriggerObject = triggerObject;
            HitObject = null;

            thisCollider3D = default(Collider);
            thisCollider2D = default(Collider2D);

            otherCollider3D = default(Collider);
            otherCollider2D = default(Collider2D);

            this.physicsType = physicsType;

            CollisionMessageVelocity = Vector3.zero;
        }

        private void setData(Vector3 point, Vector3 normal, GameObject triggerObject, GameObject hitObject,
            Collider thisCollider, Collider otherCollider, Vector3 velocity, bool invert)
        {
            Point = point;
            Normal = normal;

            if (invert)
            {
                thisCollider3D = thisCollider;
                otherCollider3D = otherCollider;

                TriggerObject = triggerObject;
                HitObject = hitObject;
            }
            else
            {
                thisCollider3D = thisCollider;
                otherCollider3D = otherCollider;

                TriggerObject = triggerObject;
                HitObject = hitObject;
            }

            thisCollider2D = default(Collider2D);
            otherCollider2D = default(Collider2D);

            physicsType = PhysicsType.Physics3D;

            CollisionMessageVelocity = velocity;
        }

        private void setData(Vector3 point, Vector3 normal, GameObject triggerObject, GameObject hitObject,
            Collider2D thisCollider, Collider2D otherCollider, Vector3 velocity, bool invert)
        {
            Point = point;
            Normal = normal;

            if (invert)
            {
                thisCollider2D = thisCollider;
                otherCollider2D = otherCollider;

                TriggerObject = triggerObject;
                HitObject = hitObject;
            }
            else
            {
                thisCollider2D = thisCollider;
                otherCollider2D = otherCollider;

                TriggerObject = triggerObject;
                HitObject = hitObject;
            }

            thisCollider3D = default(Collider);
            otherCollider3D = default(Collider);

            physicsType = PhysicsType.Physics2D;

            CollisionMessageVelocity = velocity;
        }

        /// <summary>
        /// Get the ID of the physics material belonging to the collider that triggered the collision.
        /// </summary>
        /// <returns>The ID of the physics material. Will return 0 if there is no material.</returns>
        public int GetTriggerObjectPhysicsMaterialID()
        {
            if (physicsType == PhysicsType.Physics3D && thisCollider3D != null)
            {
                PhysicsMaterial physicMaterial = thisCollider3D.sharedMaterial;
                if (physicMaterial != null)
                {
                    return physicMaterial.GetInstanceID();
                }
            }
            else if (physicsType == PhysicsType.Physics2D && thisCollider2D != null)
            {
                PhysicsMaterial2D physicsMaterial2D = thisCollider2D.sharedMaterial;
                if (physicsMaterial2D != null)
                {
                    return physicsMaterial2D.GetInstanceID();
                }
            }

            return 0;
        }

        /// <summary>
        /// Get the ID of the physics material belonging to the collider being collided with.
        /// </summary>
        /// <returns>The ID of the physics material. Will return 0 if there is no material.</returns>
        public int GetHitObjectPhysicsMaterialID()
        {
            if (physicsType == PhysicsType.Physics3D && otherCollider3D != null)
            {
                PhysicsMaterial physicMaterial = otherCollider3D.sharedMaterial;
                if (physicMaterial != null)
                {
                    return physicMaterial.GetInstanceID();
                }
            }
            else if (physicsType == PhysicsType.Physics2D && otherCollider2D != null)
            {
                PhysicsMaterial2D physicsMaterial2D = otherCollider2D.sharedMaterial;
                if (physicsMaterial2D != null)
                {
                    return physicsMaterial2D.GetInstanceID();
                }
            }

            return 0;
        }

        /// <summary>
        /// Get the ID the object that triggered the collision.
        /// </summary>
        /// <returns>The ID of the object that triggered the collision. Will return 0 if the object is not known.</returns>
        public int GetTriggerObjectID()
        {
            if (TriggerObject != null)
                return TriggerObject.GetInstanceID();
            else
                return 0;
        }

        /// <summary>
        /// Get the ID the object that was hit.
        /// </summary>
        /// <returns>The ID the object that was hit. Will return 0 if the object is not known.</returns>
        public int GetHitObjectID()
        {
            if (HitObject != null)
                return HitObject.GetInstanceID();
            else
                return 0;
        }
    }
}
