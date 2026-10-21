using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WeaponSlotsMeleeChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Any).Invoke(target, args);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Any).SetValue(target, value);
    private static WeaponData Data(WeaponCategory category)
    { var data = ScriptableObject.CreateInstance<WeaponData>(); Set(data, "category", category); return data; }

    [MenuItem("Tools/Combat/Run Four Slots and Fists Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, "Assets/CombatCheckHost.unity");
        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            Slots(); Debug.Log("PASS FISTS: four empty selections, categories, pickup replacement and magazine preservation");
            ChargeAndCover(); Debug.Log("PASS FISTS: release-only charge scaling, cooldown and cover");
            Defense(); Debug.Log("PASS FISTS: frontal parry/guard, rear and non-blockable damage, single parry reward");
            ShoveAndGuardBreak(); Debug.Log("PASS FISTS: V tap/hold, one target, action lock, approach and recovery");
            Cancellation(); Debug.Log("PASS FISTS: slot switch and cancellation discard held attacks");
            Hud(); Debug.Log("PASS FISTS: existing HUD migrates to four selectable, non-overlapping rows");
            RangedShove(); Debug.Log("PASS COMBAT: armed shove cancels windup, stuns and reduces posture");
            FastGuardBreak(); Debug.Log("PASS COMBAT: short tap and fast hold are distinct at 30/60/120Hz; approach remains collision-safe");
            EnemyBars(); Debug.Log("PASS UI: enemy health/posture bars reflect damage and hide behind cover or on death");
            StaggerRecovery(); Debug.Log("PASS STAGGER: latched until full recovery, one cue per break, reservation pauses recovery");
            Execution(); Debug.Log("PASS EXECUTION: reach/cover, holstered use, invulnerability, one kill and cleanup");
            ExecutionPresentation(); Debug.Log("PASS UI: stagger assets, Korean prompt, pop/fade and persistent badge");
            ExecutionCrowd(); Debug.Log("PASS EXECUTION: crowd windup cancellation, retreat, navigation cover and release");
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Actions/Player.inputactions");
            Require(asset.FindAction("Combat/Guard") != null && asset.FindAction("Player/Shove") != null, "Defense and shove input actions exist");
            Debug.Log("WEAPON_SLOTS_MELEE_CHECKS_PASSED: 14");
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(test, true); }
    }
    public static void RunBatch()
    {
        Run();
        WeaponFoundationChecks.Run();
        MovementRefactorChecks.Run();
    }
    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root, Player;
        public readonly PlayerMovement Movement;
        public readonly PlayerWeaponSlots Slots;
        public readonly PlayerMeleeCombat Melee;
        public readonly Vector3 Origin = new Vector3(6000f, 6000f, 6000f);
        public Fixture()
        {
            Root = new GameObject("Fist checks");
            Player = new GameObject("Player"); Player.transform.SetParent(Root.transform); Player.transform.position = Origin; Player.layer = 31;
            var controller = Player.AddComponent<CharacterController>(); controller.height = 1.75f; controller.radius = .3f; controller.skinWidth = .03f;
            var eye = new GameObject("Camera"); eye.transform.SetParent(Player.transform, false); eye.transform.localPosition = Vector3.up; eye.AddComponent<Camera>();
            Movement = Player.AddComponent<PlayerMovement>(); Call(Movement, "Awake");
            Call(Movement.Execution, "OnEnable");
            Player.AddComponent<PlayerWeaponInput>(); Player.AddComponent<WeaponController>();
            Slots = Player.AddComponent<PlayerWeaponSlots>(); Call(Slots, "OnEnable");
            Melee = Player.AddComponent<PlayerMeleeCombat>(); Call(Melee, "OnEnable");
            Slots.SelectSlot(0);
            Box(new Vector3(0,-.1f,0), new Vector3(10,.2f,10));
            Ground();
        }
        public GameObject Box(Vector3 position, Vector3 size)
        {
            var box = new GameObject("Surface"); box.transform.SetParent(Root.transform); box.transform.position = Origin + position;
            box.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return box;
        }
        public WeaponTrainingTarget Target(float z, float x = 0f)
        {
            var box = Box(new Vector3(x,1,z), new Vector3(.6f,2f,.6f));
            var receiver = box.AddComponent<WeaponTrainingTarget>(); receiver.ResetTarget();
            Call(box.GetComponent<MeleeCombatTarget>(), "Awake"); Call(box.GetComponent<MeleeCombatTarget>(), "OnEnable"); return receiver;
        }
        public void Ground()
        {
            var motor = typeof(PlayerMovement).GetProperty("Motor", Any).GetValue(Movement);
            Movement.VerticalVelocity = -2f; Call(motor, "Move", .02f); Call(motor, "CheckGroundedAndSlope");
        }
        public void Step(float dt, MeleeInputFrame frame = default)
        {
            Melee.ProcessFrame(dt, frame);
            var motor = typeof(PlayerMovement).GetProperty("Motor", Any).GetValue(Movement);
            Call(motor, "Move", dt); Call(motor, "CheckGroundedAndSlope");
        }
        public void Dispose() => Object.DestroyImmediate(Root);
    }
    private static void Slots()
    {
        using (var f = new Fixture())
        {
            for (int i = 0; i < 4; i++)
            {
                Require(f.Slots.SelectSlot(i) && f.Slots.SelectedSlot == i && f.Slots.UsesFists, "Empty slot remains selected: " + i);
                f.Slots.ToggleSlot(i);
                Require(f.Slots.SelectedSlot == -1 && !f.Slots.UsesFists && !f.Melee.IsActive
                    && f.Player.GetComponent<WeaponController>().Runtime == null, "Repeated slot key holsters everything");
                Call(f.Slots,"OnEnable"); Require(f.Slots.SelectedSlot == -1, "Re-enable preserves the deselected state");
                f.Slots.ToggleSlot(i); Require(f.Slots.SelectedSlot == i && f.Slots.UsesFists, "Empty slot can be reselected");
            }
            Require(!f.Slots.SelectSlot(-2) && !f.Slots.SelectSlot(4), "Invalid slots rejected");
            var ranged = Data(WeaponCategory.Ranged); var melee = Data(WeaponCategory.Melee); var special = Data(WeaponCategory.Special);
            try
            {
                Require(!f.Slots.TrySetWeapon(2,ranged) && !f.Slots.TrySetWeapon(3,melee) && !f.Slots.TrySetWeapon(0,special), "Category restrictions");
                Require(f.Slots.TryAcquire(ranged,out _) && f.Slots.GetWeapon(0) == ranged, "First empty ranged slot");
                Require(f.Slots.TryAcquire(ranged,out _) && f.Slots.GetWeapon(1) == ranged, "Second empty ranged slot");
                f.Slots.SelectSlot(0); var runtime = f.Slots.GetRuntime(0);
                runtime.Tick(.01f,new WeaponInputFrame(true,true)); int ammo = runtime.Ammo;
                runtime.Tick(.01f,new WeaponInputFrame(false,false,true));
                f.Slots.SelectSlot(1); Require(!runtime.IsReloading, "Switch interrupts reload");
                f.Slots.SelectSlot(0); Require(runtime.Ammo == ammo && f.Slots.GetRuntime(1).Ammo == ranged.MagazineSize, "Independent persistent magazines");
                f.Slots.ToggleSlot(0);
                Require(f.Slots.SelectedSlot == -1 && !f.Slots.UsesFists && f.Player.GetComponent<WeaponController>().Runtime == null,
                    "Toggling an occupied slot holsters the gun without raising fists");
                f.Slots.ToggleSlot(0); Require(f.Slots.GetRuntime(0) == runtime && runtime.Ammo == ammo, "Re-equipping preserves the original magazine");
                f.Slots.SelectSlot(2); Require(!f.Slots.TryAcquire(ranged,out _), "Full ranged slots cannot replace melee selection");
                f.Slots.SelectSlot(1); Require(f.Slots.TryAcquire(ranged,out var replaced) && replaced == ranged, "Compatible selected replacement");
                Require(f.Slots.TrySetWeapon(2,melee) && f.Slots.TrySetWeapon(3,special), "Melee and special slots accept matching data");
                f.Slots.TrySetWeapon(1,null); Require(f.Slots.UsesFists, "Removing selected weapon restores fists");
            }
            finally { Object.DestroyImmediate(ranged); Object.DestroyImmediate(melee); Object.DestroyImmediate(special); }
        }
    }
    private static void ChargeAndCover()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(1.2f);
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackPressed:true,attackHeld:true));
            Require(target.Health == 1000, "Press alone never damages");
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackReleased:true)); float light = 1000-target.Health;
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackReleased:true)); Require(1000-target.Health == light, "Release hits once");
            f.Melee.ProcessFrame(.5f,default);
            f.Melee.ProcessFrame(.9f,new MeleeInputFrame(attackPressed:true,attackHeld:true));
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackReleased:true));
            Require(1000-target.Health-light > light*2f, "Charge increases health damage");
            Require(target.GetComponent<MeleeCombatTarget>().Posture < 65f, "Charge damages posture");
            f.Box(new Vector3(0,1,.5f),new Vector3(2,2,.1f)); float health = target.Health;
            f.Melee.ProcessFrame(.5f,default);
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackPressed:true,attackHeld:true));
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackReleased:true));
            Require(target.Health == health, "Wall blocks fists");
        }
    }
    private static void Defense()
    {
        using (var f = new Fixture())
        {
            var attacker = f.Target(1.2f);
            DamageHit Hit(bool blockable = true, DamageKind kind = DamageKind.Attack) => new DamageHit(DamageHit.NewAttackId(),10f,kind,
                f.Origin,Vector3.back,attacker.gameObject,blockable);
            f.Melee.ProcessFrame(.01f,new MeleeInputFrame(guardPressed:true,guardHeld:true));
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit()) == DamageResult.Parried, "Fresh front guard parries");
            var target = attacker.GetComponent<MeleeCombatTarget>(); Require(target.IsStunned && target.Posture == 75f, "Parry interrupts attacker and damages posture");
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit()) == DamageResult.Blocked && target.Posture == 75f, "A press grants only one parry");
            f.Melee.ProcessFrame(.3f,new MeleeInputFrame(guardHeld:true));
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit()) == DamageResult.Blocked, "Held guard blocks");
            attacker.transform.position = f.Origin + Vector3.back;
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit()) == DamageResult.Damaged, "Rear damage bypasses guard");
            attacker.transform.position = f.Origin + Vector3.forward;
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit(false)) == DamageResult.Damaged, "Bullets/unblockable damage bypass guard");
            Require(f.Movement.DamageReceiver.ReceiveHit(Hit(true,DamageKind.Fall)) == DamageResult.Damaged, "Fall bypasses guard");
        }
    }
    private static void ShoveAndGuardBreak()
    {
        using (var f = new Fixture())
        {
            var enemy = f.Target(1.2f); var other = f.Target(1.2f,1f);
            var target = enemy.GetComponent<MeleeCombatTarget>();
            f.Step(.02f,new MeleeInputFrame(shovePressed:true,shoveHeld:true));
            Require(enemy.Health == 1000, "V press waits to distinguish hold");
            f.Step(.02f,new MeleeInputFrame(shoveReleased:true));
            Require(enemy.Health == 997 && target.IsStunned, "V tap shoves and interrupts");
            f.Step(.6f);
            f.Step(.41f,new MeleeInputFrame(shovePressed:true,shoveHeld:true));
            Require(f.Melee.IsGuardBreaking && f.Movement.CombatInputLocked, "V hold reserves one target and locks caster");
            Require(!f.Slots.SelectSlot(1), "Cannot switch during guard break");
            for (int i = 0; i < 16; i++) f.Step(.05f);
            Require(!f.Melee.IsGuardBreaking && !f.Movement.CombatInputLocked, "Guard break unlocks on completion");
            Require(target.Posture <= 37f && target.IsStunned, "Guard break applies major posture damage and stun");
            Require(other.GetComponent<MeleeCombatTarget>().Posture == 100f && !other.GetComponent<MeleeCombatTarget>().IsStunned, "Only one target affected");
            Require(enemy.Health == 997, "Holding V never also emits a tap hit");
        }
    }
    private static void Cancellation()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(1.2f);
            f.Melee.ProcessFrame(.5f,new MeleeInputFrame(attackPressed:true,attackHeld:true));
            // Slots locate the component lazily too, for editor fixtures and authored component order.
            f.Slots.SelectSlot(1); f.Melee.ProcessFrame(.01f,new MeleeInputFrame(attackReleased:true));
            Require(target.Health == 1000, "Changing empty slots cancels charge");
            f.Melee.ProcessFrame(.41f,new MeleeInputFrame(shovePressed:true,shoveHeld:true));
            Require(f.Melee.IsGuardBreaking, "Guard break cancellation fixture");
            f.Melee.CancelAction(); Require(!f.Movement.CombatInputLocked && !target.GetComponent<MeleeCombatTarget>().IsStunned, "Cancellation releases reservation");
        }
    }
    private static void Hud()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/HUD/WeaponSlotsHUD.prefab");
        Require(prefab != null, "Existing HUD prefab available");
        using (var f = new Fixture())
        {
            var viewObject = Object.Instantiate(prefab, f.Root.transform);
            var hud = viewObject.GetComponent<WeaponSlotsHUD>();
            Set(hud,"source",f.Slots);
            hud.UpgradeToFourSlots(); hud.UpgradeToFourSlots();
            var rows = (WeaponSlotsHUD.SlotView[])typeof(WeaponSlotsHUD).GetField("slots",Any).GetValue(hud);
            Require(rows.Length == 4 && rows[3].number.text == "4", "Migration adds exactly one fourth row");
            f.Slots.SelectSlot(3); hud.Reveal(); hud.Refresh(1f);
            Require(rows[3].weaponName.text == "SPECIAL / \u00b7" && rows[3].weaponName.gameObject.activeSelf && rows[3].silhouette.Kind == 0,
                "Selected empty fourth slot retains category and a minimal empty symbol");
            Require(rows[3].rect.localScale.x > 1.1f && rows[2].weaponName.gameObject.activeSelf, "Selection emphasizes fourth row while other categories remain visible");
            for (int i=0;i<3;i++) Require(rows[i].rect.anchoredPosition.y-rows[i+1].rect.anchoredPosition.y >
                (rows[i].rect.rect.height*rows[i].rect.localScale.y+rows[i+1].rect.rect.height*rows[i+1].rect.localScale.y)*.5f,
                "HUD rows do not overlap");
            f.Slots.ToggleSlot(3); hud.Refresh(1f);
            foreach (var row in rows) Require(row.weaponName.gameObject.activeSelf && row.rect.localScale.x < 1.01f,
                "Deselection removes highlights but preserves slot types");
        }
    }
    private static void RangedShove()
    {
        using (var f = new Fixture())
        {
            var gun = Data(WeaponCategory.Ranged);
            try
            {
                f.Slots.TrySetWeapon(0,gun);
                var enemy = f.Box(new Vector3(0,1,1.2f),new Vector3(.6f,2f,.6f));
                var ai = enemy.AddComponent<EnemyCombatAI>(); Call(ai,"Awake");
                var health = enemy.GetComponent<EnemyHealth>(); Call(health,"Awake");
                var posture = enemy.GetComponent<MeleeCombatTarget>(); Call(posture,"Awake");
                Set(ai,"preparing",true); Set(ai,"timer",.5f);
                var abilities = typeof(PlayerMovement).GetProperty("Abilities",Any).GetValue(f.Movement);
                var combat = typeof(MovementAbilities).GetProperty("Combat").GetValue(abilities);
                Call(combat,"TryShove");
                Require(health.Health < health.MaximumHealth && !ai.IsPreparingAttack && posture.IsStunned && posture.Posture == 92f,
                    "Gun shove must interrupt the actual enemy AI and damage posture");
                posture.Tick(.5f); Require(!posture.IsStunned && !ai.IsPreparingAttack, "Stun expires without resuming the interrupted windup");
            }
            finally { Object.DestroyImmediate(gun); }
        }
    }
    private static void FastGuardBreak()
    {
        foreach (int hz in new[] {30,60,120})
        foreach (float distance in new[] {1f,2.3f})
        using (var f = new Fixture())
        {
            float dt = 1f/hz;
            var target = f.Target(distance); var posture = target.GetComponent<MeleeCombatTarget>();
            float held = 0f;
            while (!f.Melee.IsGuardBreaking && held < .25f)
            { f.Step(dt,new MeleeInputFrame(shovePressed:held == 0f,shoveHeld:true)); held += dt; }
            Require(held <= .14f+dt+.001f && f.Melee.IsGuardBreaking, "Hold recognized within 0.14s plus one frame: " + hz);
            float elapsed = 0f;
            while (posture.Posture == 100f && f.Melee.IsGuardBreaking && elapsed < .3f) { f.Step(dt); elapsed += dt; }
            Require(posture.Posture == 45f && elapsed <= .24f+dt, "Guard break connects rapidly at near/far range: " + hz + "/" + distance);
            while (f.Melee.IsGuardBreaking && elapsed < .5f) { f.Step(dt); elapsed += dt; }
            Require(!f.Melee.IsGuardBreaking && elapsed <= .42f+dt, "Short recovery releases player promptly");
        }
        using (var f = new Fixture())
        {
            var target = f.Target(1.2f);
            f.Step(.05f,new MeleeInputFrame(shovePressed:true,shoveHeld:true));
            f.Step(.02f,new MeleeInputFrame(shoveReleased:true));
            Require(target.Health == 997 && !f.Melee.IsGuardBreaking, "A 70ms tap remains a shove");
        }
        using (var f = new Fixture())
        {
            var target = f.Target(2f);
            // Low wall is below the eye trace, but blocks the player's real capsule approach.
            f.Box(new Vector3(0,.3f,.8f),new Vector3(2,.6f,.2f));
            f.Step(.15f,new MeleeInputFrame(shovePressed:true,shoveHeld:true));
            for (int i=0;i<40;i++) f.Step(.01f);
            Require(target.GetComponent<MeleeCombatTarget>().Posture == 100f && !f.Melee.IsGuardBreaking
                && f.Player.transform.position.z < f.Origin.z+.8f, "Fast approach cannot bypass a wall or hit from out of reach");
        }
    }
    private static void StaggerRecovery()
    {
        foreach (int hz in new[] {30,60,120})
        using (var f = new Fixture())
        {
            var target = f.Target(1.2f).GetComponent<MeleeCombatTarget>(); int cues = 0;
            target.Staggered += () => cues++;
            target.ApplyPostureDamage(100f);
            Require(target.IsStaggered && target.IsStunned && target.CanExecute && cues == 1, "Break enables deathblow and interrupts");
            float dt = 1f / hz;
            for (int i=0; i<hz*2; i++) target.Tick(dt);
            Require(target.Posture > 0f && target.Posture < 100f && target.IsStaggered && target.IsStunned,
                "Partial recovery never unlocks AI");
            float recovering = target.Posture; target.ApplyPostureDamage(100f);
            Require(target.Posture == recovering && cues == 1, "Repeated hits do not retrigger feedback or restart stagger");
            Require(target.TryReserveExecution(f.Movement.Execution), "Execution can reserve a partially recovered stagger");
            target.Tick(1f); Require(target.Posture == recovering, "Reserved victim cannot recover out of deathblow");
            target.ReleaseExecution(f.Movement.Execution);
            for (int i=0; i<hz*3; i++) target.Tick(dt);
            Require(target.Posture == 100f && !target.IsStaggered && !target.IsStunned && !target.CanExecute, "Full recovery restores action");
            target.ApplyPostureDamage(100f); Require(cues == 2, "A later genuine break plays a new cue");
        }
    }
    private static void Execution()
    {
        using (var f = new Fixture())
        {
            var enemy = f.Target(1.2f); var target = enemy.GetComponent<MeleeCombatTarget>();
            var execute = f.Movement.Execution;
            Require(!execute.TryBegin(target), "Healthy posture cannot be executed");
            target.ApplyPostureDamage(100f); f.Slots.SelectSlot(-1);
            Require(execute.FindCandidate() == target, "No equipped slot is required");
            var wall = f.Box(new Vector3(0,1,.5f), new Vector3(2,2,.1f));
            Require(execute.FindCandidate() == null && !execute.TryBegin(target), "Wall blocks both hint and activation");
            Object.DestroyImmediate(wall); Physics.SyncTransforms();
            enemy.transform.position += Vector3.forward * 5f; Physics.SyncTransforms();
            Require(execute.FindCandidate() == null && !execute.TryBegin(target), "Out of reach cannot execute");
            enemy.transform.position -= Vector3.forward * 5f; Physics.SyncTransforms();
            int impacts = 0; execute.Impact += _ => impacts++;
            f.Movement.DamageReceiver.SetShield(20f);
            Require(execute.TryBegin(target) && f.Movement.CombatInputLocked && !f.Slots.SelectSlot(1), "Execution locks player and slots");
            Require(!execute.TryBegin(target), "Repeated activation cannot restart execution");
            foreach (var kind in new[] {DamageKind.Attack, DamageKind.Fall})
                Require(f.Movement.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),50f,kind,f.Origin,Vector3.forward)) == DamageResult.Invulnerable,
                    "All damage is rejected during execution");
            Require(f.Movement.DamageReceiver.Health == 100f && f.Movement.DamageReceiver.Shield == 20f, "Invulnerability preserves HP and shield");
            target.Tick(5f); Require(target.Posture == 0f, "Victim waits for execution impact");
            execute.Tick(.36f);
            Require(enemy.Health == 0f && impacts == 1 && execute.IsExecuting, "One lethal impact; protection continues through recovery");
            execute.Tick(.1f); Require(impacts == 1, "Impact cannot repeat");
            execute.Tick(.4f);
            Require(!execute.IsExecuting && !f.Movement.CombatInputLocked && !PlayerExecution.Suppresses(target), "Completion releases all locks");
            Require(f.Movement.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),30f,DamageKind.Attack,f.Origin,Vector3.forward)) == DamageResult.Damaged,
                "Protection ends when the transaction completes");
            enemy.ResetTarget(); target.ApplyPostureDamage(100f); Require(execute.TryBegin(target), "Second execution starts");
            execute.Cancel(); target.Tick(5f);
            Require(enemy.Health == 1000f && !target.IsStunned && !execute.IsExecuting, "Cancellation releases target without killing it");
            target.ApplyPostureDamage(100f); Require(execute.TryBegin(target), "Target disappearance fixture");
            Object.DestroyImmediate(enemy.gameObject); execute.Tick(.01f);
            Require(!execute.IsExecuting && !f.Movement.CombatInputLocked, "Destroyed target cannot leave invulnerability or input locked");
        }
    }
    private static void ExecutionPresentation()
    {
        var settings = CombatFeedbackSettings.Instance;
        Require(settings != null && settings.staggerImage != null && settings.staggerSound != null && settings.promptFont != null,
            "User supplied assets: settings=" + (settings != null) + ", image=" + (settings != null && settings.staggerImage != null)
                + ", audio=" + (settings != null && settings.staggerSound != null) + ", font=" + (settings != null && settings.promptFont != null));
        Require(settings.promptFont.HasCharacters("[F] 처형하기", out uint[] missing, true, true), "Prompt font contains all Korean glyphs");
        using (var f = new Fixture())
        {
            var enemy = f.Target(2f); var target = enemy.GetComponent<MeleeCombatTarget>();
            var camera = f.Player.GetComponentInChildren<Camera>(); camera.transform.position = f.Origin + new Vector3(0,1,-4);
            target.ApplyPostureDamage(100f);
            var bars = enemy.gameObject.AddComponent<EnemyVitalsHUD>(); bars.Refresh(camera);
            Require(bars.IsStaggerVisible, "Stagger shows the imported overhead image");
            target.Tick(2f); bars.Refresh(camera); Require(bars.IsStaggerVisible, "Badge persists during partial recovery");
            target.Tick(3f); bars.Refresh(camera); Require(!bars.IsStaggerVisible, "Badge disappears at full recovery");
            var hud = f.Player.AddComponent<ExecutionHUD>(); hud.Refresh(target,.01f);
            Require(hud.PromptScale > 1.2f && hud.Opacity > 0f, "Execution prompt pops in larger");
            hud.Refresh(target,.3f); Require(hud.PromptScale == 1f && hud.Opacity == 1f, "Prompt settles at normal size");
            hud.Refresh(null,.2f); Require(hud.Opacity == 0f, "Lost target fades prompt away");
        }
    }
    private static void ExecutionCrowd()
    {
        using (var f = new Fixture())
        {
            var navSettings = UnityEngine.AI.NavMesh.GetSettingsByIndex(0);
            var source = new UnityEngine.AI.NavMeshBuildSource {shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(f.Origin + Vector3.down * .1f,Quaternion.identity,Vector3.one),size = new Vector3(20,.2f,20)};
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(navSettings, new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>{source},
                new Bounds(f.Origin,new Vector3(22,10,22)),Vector3.zero,Quaternion.identity);
            Require(data != null,"Crowd test navigation mesh"); var nav = UnityEngine.AI.NavMesh.AddNavMeshData(data);
            try
            {
                var target = f.Target(1.2f).GetComponent<MeleeCombatTarget>(); target.ApplyPostureDamage(100f);
                var bystander = f.Box(new Vector3(1.1f,0,0),new Vector3(.6f,1.8f,.6f));
                var ai = bystander.AddComponent<EnemyCombatAI>(); Call(ai,"Awake");
                Call(bystander.GetComponent<EnemyHealth>(),"Awake");
                var posture = bystander.GetComponent<MeleeCombatTarget>(); Call(posture,"Awake"); Call(posture,"OnEnable");
                var agent = bystander.GetComponent<UnityEngine.AI.NavMeshAgent>();
                Require(agent.Warp(f.Origin + new Vector3(1.1f,0,0)),"Bystander placed on navigation mesh");
                Set(ai,"preparing",true);
                Require(f.Movement.Execution.TryBegin(target) && !ai.IsPreparingAttack, "Existing enemy windup immediately cancelled");
                float before = Vector3.Distance(bystander.transform.position,f.Player.transform.position);
                posture.Tick(.1f);
                Require(agent.isStopped && Vector3.Distance(bystander.transform.position,f.Player.transform.position) > before,
                    "Close enemy backs away while chase remains stopped");
                Call(ai,"Update"); Require(!ai.IsPreparingAttack,"Suppressed enemy cannot prepare an attack");
                var wall = f.Box(new Vector3(1.9f,.8f,0),new Vector3(.2f,2f,3f));
                for(int i=0;i<10;i++) posture.Tick(.1f);
                Require(bystander.transform.position.x < f.Origin.x+1.9f,"Retreat respects unbaked physical cover");
                Object.DestroyImmediate(wall); Physics.SyncTransforms();
                for(int i=0;i<20;i++) posture.Tick(.1f);
                Vector3 stopped = bystander.transform.position; posture.Tick(.1f);
                Require(Vector3.Distance(stopped,bystander.transform.position)<.01f,"Retreat stops at clearance radius");
                f.Movement.Execution.Cancel(); posture.Tick(.1f);
                Require(!agent.isStopped && !PlayerExecution.Suppresses(ai), "Normal navigation resumes after execution");
            }
            finally { nav.Remove(); Object.DestroyImmediate(data); }
        }
    }
    private static void EnemyBars()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(2f); var posture = target.GetComponent<MeleeCombatTarget>();
            var view = f.Player.GetComponentInChildren<Camera>(); view.transform.position = f.Origin + new Vector3(0,1,-4);
            var bars = target.gameObject.AddComponent<EnemyVitalsHUD>();
            bars.Refresh(view); Require(bars.IsVisible && bars.HealthRatio == 1f && bars.PostureRatio == 1f, "Living target shows full bars");
            target.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),250f,DamageKind.Attack,Vector3.zero,Vector3.forward));
            posture.ApplyPostureDamage(40f); bars.Refresh(view);
            Require(Mathf.Abs(bars.HealthRatio-.75f)<.001f && Mathf.Abs(bars.PostureRatio-.6f)<.001f, "Both bars track their own resource");
            var wall = f.Box(new Vector3(0,2,0),new Vector3(3,4,.2f)); bars.Refresh(view);
            Require(!bars.IsVisible, "Solid cover hides overhead bars");
            Object.DestroyImmediate(wall); Physics.SyncTransforms(); bars.Refresh(view); Require(bars.IsVisible,"Bars return when cover is removed");
            view.transform.rotation = Quaternion.Euler(0,180,0); bars.Refresh(view); Require(!bars.IsVisible,"Targets behind the camera stay hidden");
            view.transform.rotation = Quaternion.identity;
            target.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),1000f,DamageKind.Attack,Vector3.zero,Vector3.forward));
            bars.Refresh(view); Require(!bars.IsVisible,"Dead target hides bars");
        }
    }
}
