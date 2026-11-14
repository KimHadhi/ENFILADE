using System;
using UnityEngine;

/// <summary>
/// What an avatar mod says about itself: the file "&lt;name&gt;.avatar.json" beside its bundle in a mod's Avatars folder,
/// written by the exporter (ModAvatarExporter) in the maker's own Unity project and read by the game (ModAvatars).
/// The bundle holds what only Unity can pack (the prefab, its meshes and textures); this file holds everything else as
/// the maker had it, in plain words a person can read and change: each material's shader by name with all its numbers,
/// colours and textures, and VRChat's PhysBones and face settings as they were set. Nothing in it is worked out for
/// this game: the game does that when the avatar is stood up (which shader to draw a material with, how PhysBones
/// become its own sway), so a better rule in a later version of the game reaches every avatar ever exported, and the
/// exporter stays small enough to run in any Unity a maker has (the user, 2026-11-14: "VRChat 유저들이 자기가 쓰던
/// 캐릭터를 내 게임에서도 쓰고 싶어 할 텐데 이거 호환 그대로 잘 되게끔 … 유저를 제약하지 마. 최대한 유동적이고 편하게").
/// One thing in it is this game's own idea, and so is said in the game's words and left in the open for its maker to
/// change: which of the model's meshes go in which of the wardrobe's slots (a character's <see cref="Part"/>s, an
/// outfit's <see cref="Item"/>s). The exporter sorts them by their names and where they sit on the body and shows its
/// maker the result to correct, so what is written here is what the maker saw.
/// A file holds characters, or outfits to wear on a character (the user, 2026-11-14, on clothes as mods of their own:
/// "좀 더 유저에게 자유를 주고 유동적이고 개방적인 방향이네. 좋아. 이런 거 좋아요").
/// This file is shared with the exporter's package, so it uses nothing of the game's: Unity's own types only.
/// </summary>
[Serializable]
public sealed class ModAvatarFile
{
    public const string Extension = ".avatar.json";
    /// <summary>Raised when a file an older game can't read is written; a newer file than the game knows is read as far as it goes.</summary>
    public const int Latest = 1;
    /// <summary>An outfit's <see cref="Outfit.skin"/> when it is made for no one in particular: a rigid accessory any character can wear.</summary>
    public const string Anyone = "*";

    public int version = Latest;
    /// <summary>The bundle's file name, beside this file.</summary>
    public string bundle = "";
    /// <summary>Who wrote it: the exporter's version, the Unity it ran in and that project's render pipeline. For reports only.</summary>
    public string exporter = "";
    public Character[] characters = Array.Empty<Character>();
    public Outfit[] outfits = Array.Empty<Outfit>();
    public MaterialData[] materials = Array.Empty<MaterialData>();

    /// <summary>A playable character: a humanoid model as its maker dressed it.</summary>
    [Serializable]
    public sealed class Character
    {
        /// <summary>Its id in the game (saved settings, the network, table rows): lower-case letters, digits and -.</summary>
        public string id = "";
        /// <summary>Its name on the character cards.</summary>
        public string name = "";
        /// <summary>The prefab's path in the bundle.</summary>
        public string prefab = "";
        public PhysBone[] sway = Array.Empty<PhysBone>();
        public Face face = new Face();
        /// <summary>
        /// The clothes it came in, slot by slot: the wardrobe can take each off or put an outfit's piece in its place.
        /// A mesh no part lists is its body and stays on. None at all: the character is worn whole, as it came.
        /// </summary>
        public Part[] parts = Array.Empty<Part>();
    }

    /// <summary>What a character wears of its own in one of the wardrobe's slots (the game's CharacterParts table).</summary>
    [Serializable]
    public sealed class Part
    {
        /// <summary>The slot's id (hair, top, bottom, legs, shoes …: the game's OutfitSlots table).</summary>
        public string slot = "";
        /// <summary>The names of its meshes' objects. A one-piece is listed under both the slots it fills.</summary>
        public string[] meshes = Array.Empty<string>();
        /// <summary>What the wardrobe calls it; "" reads the meshes' names.</summary>
        public string name = "";
        /// <summary>See-through by its texture (lace, a net, sheer tights): what lies under it shows and is never hidden.</summary>
        public bool sheer;
    }

    /// <summary>
    /// Clothes to wear, made for one character's body (its bones carry that character's bones' names, as every outfit
    /// sold for an avatar does) or, rigid and with no such bones, for anyone. The game joins it to the body when it
    /// is put on, as Modular Avatar's Merge Armature does, and each of its <see cref="items"/> goes in its own slot of
    /// the wardrobe, to be mixed with any other clothes.
    /// </summary>
    [Serializable]
    public sealed class Outfit
    {
        /// <summary>The model's id: lower-case letters, digits, - and _. The same outfit made for another character keeps it.</summary>
        public string id = "";
        /// <summary>Its name, which its items' names start with.</summary>
        public string name = "";
        /// <summary>The id of the character it is made for (the game's own or a mod's), or <see cref="Anyone"/>.</summary>
        public string skin = "";
        /// <summary>The prefab's path in the bundle.</summary>
        public string prefab = "";
        public PhysBone[] sway = Array.Empty<PhysBone>();
        /// <summary>
        /// What can be worn from it. None: the game's own items of this model id are worn from it (the game's outfit,
        /// brought for a character the game hasn't it for).
        /// </summary>
        public Item[] items = Array.Empty<Item>();
        /// <summary>Parts of it that follow one bone of the body instead of its skeleton (Modular Avatar's Bone Proxy): a hat, glasses.</summary>
        public Hold[] holds = Array.Empty<Hold>();
        /// <summary>
        /// Where its root stood against the root of the avatar it was fitted on, in that root's own measure; unset (a
        /// scale of zero), it stood at the same spot, as an outfit dropped under its avatar does.
        /// </summary>
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale;
    }

    /// <summary>One thing to wear in one slot of the wardrobe (the game's Outfits table): some meshes of an outfit's model.</summary>
    [Serializable]
    public sealed class Item
    {
        /// <summary>Its id in saved looks and over the network: lower-case letters, digits, - and _. Kept once players wear it.</summary>
        public string id = "";
        public string slot = "";
        /// <summary>Slots it fills besides its own, "a|b" (a bodysuit is underwear top that takes the bottom too); "" for none.</summary>
        public string also = "";
        /// <summary>Slots it covers, "a|b": what is worn there goes out of sight (a full mask over glasses); "" for none.</summary>
        public string hides = "";
        public string name = "";
        public string[] meshes = Array.Empty<string>();
        /// <summary>See-through by its texture: what lies under it shows and is never hidden.</summary>
        public bool sheer;
    }

    /// <summary>
    /// A part of an outfit that follows one bone of the body, as Modular Avatar's Bone Proxy says it should (its
    /// settings, as they were): <see cref="path"/> from the outfit's root, the humanoid bone by Unity's name for it
    /// (Head, Chest, LeftHand …), a path on under that bone or "", and how it is put there (Modular Avatar's own word:
    /// AsChildAtRoot, AsChildKeepWorldPose, AsChildKeepPosition, AsChildKeepRotation).
    /// </summary>
    [Serializable]
    public sealed class Hold
    {
        public string path = "", bone = "", under = "", mode = "";
    }

    /// <summary>
    /// One VRCPhysBone as its maker set it (VRChat's own names and units). Bones are named by their path from the
    /// avatar's root ("Armature/Hips/Spine/…"), "" for the root itself.
    /// </summary>
    [Serializable]
    public sealed class PhysBone
    {
        public string root = "";
        public string[] ignore = Array.Empty<string>();
        /// <summary>The bones its colliders stand on.</summary>
        public string[] colliders = Array.Empty<string>();
        /// <summary>
        /// An outfit's colliders that stand on bones of the avatar it is worn on, not on its own (a skirt's against the
        /// legs): those bones, by their path from that avatar's root.
        /// </summary>
        public string[] bodyColliders = Array.Empty<string>();
        public Vector3 endpoint;
        public int integration, limitType;
        public float pull, spring, stiffness, gravity, gravityFalloff, immobile, radius, maxAngleX, maxAngleZ;
        /// <summary>The curve beside each value (root 0 to far end 1, as a share of the value); no keys where the maker drew none.</summary>
        public Curve pullCurve = new Curve(), springCurve = new Curve(), stiffnessCurve = new Curve(), gravityCurve = new Curve(),
            immobileCurve = new Curve(), radiusCurve = new Curve(), maxAngleXCurve = new Curve(), maxAngleZCurve = new Curve();
        public bool collides;
    }

    [Serializable]
    public sealed class Curve
    {
        public Key[] keys = Array.Empty<Key>();

        public Curve() { }

        public Curve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return;
            keys = new Key[curve.length];
            for (int i = 0; i < keys.Length; i++)
            {
                var key = curve[i];
                keys[i] = new Key
                {
                    time = key.time, value = key.value, inSlope = Finite(key.inTangent), outSlope = Finite(key.outTangent),
                    inWeight = key.inWeight, outWeight = key.outWeight, weighted = (int)key.weightedMode
                };
            }
        }

        /// <summary>The curve as Unity evaluates it, or null when it has no keys.</summary>
        public AnimationCurve ToCurve()
        {
            if (keys == null || keys.Length == 0) return null;
            var made = new Keyframe[keys.Length];
            for (int i = 0; i < made.Length; i++)
                made[i] = new Keyframe(keys[i].time, keys[i].value, Finite(keys[i].inSlope), Finite(keys[i].outSlope), keys[i].inWeight, keys[i].outWeight)
                {
                    weightedMode = (WeightedMode)keys[i].weighted
                };
            return new AnimationCurve(made);
        }

        // A stepped key's slope is infinite, which JSON has no number for: it is written and read as 0.
        private static float Finite(float slope) => float.IsNaN(slope) || float.IsInfinity(slope) ? 0f : slope;
    }

    [Serializable]
    public struct Key
    {
        public float time, value, inSlope, outSlope, inWeight, outWeight;
        public int weighted;
    }

    /// <summary>What VRChat's avatar descriptor says of the face, by shape key name; "" where it says nothing.</summary>
    [Serializable]
    public sealed class Face
    {
        public string blink = "";
        /// <summary>VRChat's fifteen visemes in its order: sil PP FF TH DD kk CH SS nn RR aa E ih oh ou.</summary>
        public string[] visemes = Array.Empty<string>();
    }

    /// <summary>
    /// A material as its maker had it. The bundle holds a plain stand-in of this name on the model (or, with
    /// <see cref="keep"/>, the material itself with its shader); the game makes the real one from this.
    /// </summary>
    [Serializable]
    public sealed class MaterialData
    {
        public string name = "";
        /// <summary>The shader's name in the maker's project.</summary>
        public string shader = "";
        /// <summary>The shader it was locked or optimised from, where the material says so (Poiyomi's OriginalShader tag).</summary>
        public string original = "";
        /// <summary>The bundle carries the material with its own shader, to be drawn as it is (a shader made for this game's Unity and URP).</summary>
        public bool keep;
        public int queue = -1;
        public string[] keywords = Array.Empty<string>();
        /// <summary>The shader's passes the material has switched off, by their LightMode (lilToon turns its extra passes on and off this way).</summary>
        public string[] off = Array.Empty<string>();
        public Tag[] tags = Array.Empty<Tag>();
        public Number[] floats = Array.Empty<Number>();
        /// <summary>Colours and vectors.</summary>
        public Four[] vectors = Array.Empty<Four>();
        public Picture[] textures = Array.Empty<Picture>();
        public bool instancing, doubleSidedGI;
        /// <summary>The properties Unity takes as the material's main texture and colour, "" when the shader marks none.</summary>
        public string mainTexture = "", mainColor = "";
    }

    [Serializable]
    public struct Tag
    {
        public string name, value;
    }

    [Serializable]
    public struct Number
    {
        public string name;
        public float value;
        /// <summary>A whole-number property (ShaderLab's Integer), set as one.</summary>
        public bool whole;
    }

    [Serializable]
    public struct Four
    {
        public string name;
        public float x, y, z, w;
        /// <summary>A colour (read and set as one, so Unity converts it for the colour space), not a plain vector.</summary>
        public bool color;
    }

    [Serializable]
    public struct Picture
    {
        public string name;
        /// <summary>The texture's path in the bundle, and its own name where the file holds several.</summary>
        public string asset, sub;
        public Vector2 scale, offset;
    }
}
