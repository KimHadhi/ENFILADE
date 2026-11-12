using UnityEngine;

/// <summary>
/// The class throwables' models (Resources/Combat/GrenadeLibrary): the CS:GO thrown world models of the ArcCW GSO GMod
/// addon (2257255110) and, for the Knight's knife, the MW2019 throwing knife of "[MW] Quick Throwing Knife" (3650480912;
/// the user chose the addon's own model, 2026-11-12), turned into FBX by Tools/Blender/addon_weapon_to_fbx.py with the
/// recipes in Tools/Blender/Addons (GrenadeFrag, GrenadeFlash, GrenadeSmoke, GrenadeCanister, MWThrowingKnife). Each sits
/// at its own middle, real size. Art/Models' import lays a recipe's point along the model's -Z (a half turn about Y from
/// the recipe's frame, measured on the Desperado): the knife's point is -Z, its broad side +Y and its flat faces ±X, and
/// ThrownKnife turns it point first from its Tip marker. A missing one is drawn from plain shapes instead (Throwables.Model).
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Grenade Library", fileName = "GrenadeLibrary")]
public sealed class GrenadeLibrary : ScriptableObject
{
    public const string ResourcePath = "Combat/GrenadeLibrary";

    [Tooltip("Knight: the throwing knife.")]
    public GameObject knife;
    [Tooltip("Assault: the M67 frag.")]
    public GameObject frag;
    [Tooltip("Ghost: the smoke canister.")]
    public GameObject smoke;
    [Tooltip("Point: the M84 flashbang.")]
    public GameObject flash;
    [Tooltip("Détente: the antiseptic mist canister (the incendiary's body, repainted white with a red cross).")]
    public GameObject mist;
    [Tooltip("Support: the improvised directional mine, two C4 bricks strapped to a phone detonator (Sierra Division's " +
             "Heist Essentials Bomb_03). The Schießhund's dye grenade wears the smoke canister, dyed in code.")]
    public GameObject claymore;
}
