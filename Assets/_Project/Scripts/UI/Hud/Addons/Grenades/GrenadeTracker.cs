using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The one list of live grenades both addons' looks read (the user picked both, "원본대로"). Each addon kept its own:
    /// MW Quick Grenade a table of every npc_grenade_frag made (Indicator.Frags, by entity index), Arctic's a sphere search
    /// round the eye every frame (ents.FindInSphere) and a clientside copy of each model. Here every thrown grenade on this
    /// PC (ThrownGrenade.Live: this player's own and friends' copies) is one record, found once, with what both need worked
    /// out once a frame: its bearing from the eye as Source writes it, its distances, its centre, and its model's parts.
    /// <para>MW's indicator follows the frag alone (npc_grenade_frag is the HL2 frag, the addon's own quick grenade), and
    /// its record outlives the grenade until its picture has faded; the others' go with their grenade. Arctic's list
    /// can take the Support's claymore too (GrenadesOptions.listClaymores).</para>
    /// </summary>
    internal sealed class GrenadeTracker
    {
        /// <summary>One mesh of a grenade's model.</summary>
        internal struct Part
        {
            public Transform Transform;
            public Mesh Mesh;
            public Renderer Renderer;
        }

        internal sealed class Grenade
        {
            /// <summary>The ThrownGrenade (or ClaymoreMine) it follows; Unity-null once gone.</summary>
            public Component Source;
            /// <summary>An npc_grenade_frag: MW's indicator shows it.</summary>
            public bool Frag;
            /// <summary>Still in the world this frame (IsValid).</summary>
            public bool Live;
            internal bool seen;

            // This frame's numbers, set while Live. Distances in Source units.
            /// <summary>ent:GetPos() and ent:WorldSpaceCenter().</summary>
            public Vector3 Origin, Center;
            /// <summary>((pos - EyePos()):Angle() - EyeAngles()).y + 90 (°) for GetPos and for WorldSpaceCenter: 90 straight
            /// ahead, 180 to the left, 0 to the right.</summary>
            public float Bearing, CenterBearing;
            /// <summary>From ply:GetPos() to GetPos (MW's), and from EyePos() to WorldSpaceCenter (Arctic's).</summary>
            public float FeetDistance, EyeDistance;

            // MW's record of it (Indicator.Frags[index]), eased frame by frame and kept while it fades after the burst.
            public float Alpha, X, Y, Ang, Dist;
            public Color Tint;
            public bool Placed;

            public readonly List<Part> Parts = new List<Part>(2);
        }

        private static readonly List<MeshFilter> filters = new List<MeshFilter>();
        private static readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();

        /// <summary>Every record, oldest first.</summary>
        public readonly List<Grenade> All = new List<Grenade>();
        private readonly Stack<Grenade> spare = new Stack<Grenade>();

        /// <summary>
        /// This frame: finds new grenades, marks the gone, and works out the live ones' numbers from the eye (the main
        /// camera: EyePos and EyeAngles) and the feet (the local player's position, ply:GetPos()).
        /// </summary>
        public void Sweep(Transform eye, Vector3 feet, bool claymores)
        {
            for (int i = 0; i < All.Count; i++) All[i].seen = false;
            var thrown = ThrownGrenade.Live;
            for (int i = 0; i < thrown.Count; i++)
            {
                var grenade = thrown[i];
                // One that burst this frame stays in Live until the frame ends: it is gone already.
                if (grenade != null && !grenade.Done) Mark(grenade, grenade.Kind == Throwable.Frag);
            }
            if (claymores)
            {
                var placed = ClaymoreMine.Placed;
                for (int i = 0; i < placed.Count; i++)
                    if (placed[i] != null && placed[i].Live) Mark(placed[i], false);
            }
            Vector3 eyeAt = eye.position;
            float eyeYaw = eye.eulerAngles.y;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var grenade = All[i];
                grenade.Live = grenade.seen && grenade.Source != null;
                if (!grenade.Live)
                {
                    // Only MW's frag record outlives its grenade (it fades); the indicator lets it go.
                    if (!grenade.Frag) Release(i);
                    continue;
                }
                grenade.Origin = grenade.Source.transform.position;
                grenade.Center = CentreOf(grenade);
                grenade.Bearing = Bearing(eyeAt, grenade.Origin, eyeYaw);
                grenade.CenterBearing = Bearing(eyeAt, grenade.Center, eyeYaw);
                grenade.FeetDistance = Vector3.Distance(grenade.Origin, feet) / GrenadesTable.Unit;
                grenade.EyeDistance = Vector3.Distance(grenade.Center, eyeAt) / GrenadesTable.Unit;
            }
        }

        /// <summary>Lets record <paramref name="index"/> go (a faded frag's, or any gone one).</summary>
        public void Release(int index)
        {
            var grenade = All[index];
            All.RemoveAt(index);
            grenade.Source = null;
            grenade.Parts.Clear();
            spare.Push(grenade);
        }

        /// <summary>Lets every gone record go at once (MW's indicator switched off: nothing will fade them).</summary>
        public void ReleaseGone()
        {
            for (int i = All.Count - 1; i >= 0; i--)
                if (!All[i].Live) Release(i);
        }

        public void Clear()
        {
            for (int i = All.Count - 1; i >= 0; i--) Release(i);
        }

        /// <summary>
        /// Source's bearing of <paramref name="at"/> from the eye, as both addons write it: the yaw of (at - eye) less the
        /// eye's yaw, plus 90. Source's yaw turns left (anticlockwise seen from above) where Unity's turns right, so each
        /// sign flips; pitch plays no part.
        /// </summary>
        public static float Bearing(Vector3 eye, Vector3 at, float eyeYaw)
        {
            float yaw = Mathf.Atan2(at.x - eye.x, at.z - eye.z) * Mathf.Rad2Deg;
            return eyeYaw - yaw + 90f;
        }

        private void Mark(Component source, bool frag)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Source == source)
                {
                    All[i].seen = true;
                    return;
                }
            // New: Indicator:Add (OnEntityCreated), its record starting at the screen's middle, unseen and white.
            var grenade = spare.Count > 0 ? spare.Pop() : new Grenade();
            grenade.Source = source;
            grenade.Frag = frag;
            grenade.seen = true;
            grenade.Live = false;
            grenade.Alpha = grenade.Ang = grenade.Dist = 0f;
            grenade.Placed = false;
            grenade.Tint = Color.white;
            Gather(source.transform, grenade.Parts);
            All.Add(grenade);
        }

        // Its model as the hologram draws it (ClientsideModel(ent:GetModel())): every mesh shown under it.
        private static void Gather(Transform root, List<Part> parts)
        {
            parts.Clear();
            root.GetComponentsInChildren(false, filters);
            for (int i = 0; i < filters.Count; i++)
            {
                var filter = filters[i];
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
                parts.Add(new Part { Transform = filter.transform, Mesh = mesh, Renderer = renderer });
            }
            root.GetComponentsInChildren(false, skins);
            for (int i = 0; i < skins.Count; i++)
            {
                var skin = skins[i];
                if (skin.sharedMesh == null || !skin.enabled) continue;
                parts.Add(new Part { Transform = skin.transform, Mesh = skin.sharedMesh, Renderer = skin });
            }
            filters.Clear();
            skins.Clear();
        }

        // WorldSpaceCenter(): the middle of its model's bounds, its position when it shows nothing.
        private static Vector3 CentreOf(Grenade grenade)
        {
            var parts = grenade.Parts;
            bool any = false;
            var bounds = default(Bounds);
            for (int i = 0; i < parts.Count; i++)
            {
                var renderer = parts[i].Renderer;
                if (renderer == null) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any ? bounds.center : grenade.Origin;
        }
    }
}
