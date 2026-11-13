using UnityEngine;
using UnityEngine.InputSystem;

// PerkCardScreen: Every frame. One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private void Update()
    {
        if (host == null) return;
        var keyboard = Keyboard.current;
        bool able = player.IsInitialized && receiver != null && receiver.IsAlive && !player.IsDowned;
        // Every card taken, the full gauge buys the class's ultimate instead (Docs/Features/ClassCards.md: ClassCards.Ultimate).
        bool cards = PlayerPerks.AnyLeft(), ultimate = !cards && ClassCards.Local != null && ClassCards.Local.HasUltimate;
        bool ready = able && player.Ultimate != null && player.Ultimate.IsReady && (cards || ultimate);
        if (open && !able && picked < 0) Close();

        if (!open)
        {
            ShowPrompt(ready && !Blocked(), cards);
            // Player/PerkCards (X unless the key window says otherwise).
            if (ready && KeyBindings.Pressed("Player/PerkCards") && !Blocked())
            {
                if (cards) Open();
                else if (player.Ultimate.TryConsume())
                {
                    ClassCards.Local.Ultimate();
                    // Its face, for everyone (PlayerFace: only this PC knows the ultimate went off).
                    PlayerFace.Ultimate(player);
                }
            }
            return;
        }
        ShowPrompt(false, true);
        if (keyboard != null && picked < 0 && !closing)
        {
            // The same key closes the cards (read from its keys: alone, the cards hold the player's map).
            if (KeyBindings.Pressed("Player/PerkCards") || keyboard.escapeKey.wasPressedThisFrame) Close();
            else
                for (int i = 0; i < dealt; i++)
                    if (keyboard[DigitKeys[i]].wasPressedThisFrame || keyboard[PadKeys[i]].wasPressedThisFrame) { Pick(i); break; }
        }
        Animate();
    }

    // Another menu, the chat or the console has the keyboard, or the Esc menu is up.
    private bool Blocked()
    {
        if (DevConsole.TakesKeyboard || ShopMenu.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || SettingsMenu.IsOpen || GamePause.IsPaused
            || HubDressing.IsOpen) return true;
        if (chat == null) chat = FindAnyObjectByType<SinglePlayerChatManager>();
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    private void ShowPrompt(bool show, bool cards)
    {
        if (prompt.gameObject.activeSelf != show)
        {
            prompt.gameObject.SetActive(show);
            if (show) promptKey.text = KeyBindings.Name("Player/PerkCards");
        }
        if (show && cards != promptCards)
        {
            promptCards = cards;
            promptLabel.text = Texts.Get(cards ? "perk.ready" : "perk.ult.ready");
        }
        if (!show) return;
        float time = Time.unscaledTime;
        promptBar.color = KitStyle.With(KitStyle.Accent, .55f + .45f * Mathf.Abs(Mathf.Sin(time * 3f)));
        float bob = 1f + .03f * Mathf.Sin(time * 3f);
        prompt.localScale = new Vector3(bob, bob, 1f);
        promptGroup.alpha = 1f;
    }

    private void Animate()
    {
        float time = Time.unscaledTime, dt = Time.unscaledDeltaTime, since = time - openedAt;
        bool ending = picked >= 0 || closing;
        float end = ending ? time - endedAt : 0f;
        veil.color = KitStyle.With(KitStyle.Night, (GameSession.IsOnline ? .35f : .66f) * Mathf.Clamp01(since / .12f));

        // The slash tears across, then settles faint behind the cards.
        float tear = Mathf.Clamp01(since / .2f);
        band.localScale = new Vector3(KitReveal.EaseOut(tear), 1f - .55f * Mathf.Clamp01((since - .2f) / .3f), 1f);
        bandImage.color = KitStyle.With(KitStyle.Accent, Mathf.Lerp(.9f, .16f, Mathf.Clamp01((since - .15f) / .35f)));
        // The title slams in from big.
        float slam = Mathf.Clamp01((since - .04f) / .18f);
        float titleScale = Mathf.Lerp(1.9f, 1f, KitReveal.EaseOut(slam));
        title.localScale = new Vector3(titleScale, titleScale, 1f);
        titleGroup.alpha = slam;
        if (slam >= 1f && since - .04f - .18f < dt) shake = Mathf.Max(shake, 8f);

        var mouse = Mouse.current;
        bool pointing = Cursor.visible && mouse != null && !ending;
        for (int i = 0; i < dealt; i++) AnimateCard(cards[i], i, since, end, dt, time, pointing, mouse);

        // The picked card's badge pops as the card lands in it.
        if (picked >= 0 && landing != null)
        {
            float arrive = Mathf.Clamp01((end - PickPunch - PickFly) / .16f);
            if (arrive > 0f && !popped)
            {
                popped = true;
                Ring(Local(landing, Vector2.zero), KitStyle.Accent, 60f, 420f, .35f);
                shake = Mathf.Max(shake, 6f);
            }
            if (landingGroup != null) landingGroup.alpha = arrive > 0f ? 1f : 0f;
            float pop = arrive > 0f ? 1f + .35f * (1f - KitReveal.EaseOut(arrive)) : 1f;
            landing.localScale = new Vector3(pop, pop, 1f);
        }

        AnimateEffects(dt);
        shake = Mathf.MoveTowards(shake, 0f, dt * 90f);
        stage.anchoredPosition = shake > 0f ? new Vector2(Jitter(), Jitter()) * shake * .5f : Vector2.zero;

        if (picked >= 0) layerGroup.alpha = 1f - Mathf.Clamp01((end - (PickSeconds - .14f)) / .14f);
        else if (closing) layerGroup.alpha = 1f - Mathf.Clamp01(end / CloseSeconds);
        if ((picked >= 0 && end >= PickSeconds) || (closing && end >= CloseSeconds)) Finish();
    }

    private void AnimateCard(Card card, int index, float since, float end, float dt, float time, bool pointing, Mouse mouse)
    {
        // Dealt face down from below, overshooting into place, then flipped over.
        float start = DealFirst + index * DealStagger;
        float k = Mathf.Clamp01((since - start) / DealSeconds);
        float y = Mathf.LerpUnclamped(DealFrom, 0f, OutBack(k));
        float scale = Mathf.LerpUnclamped(.8f, 1f, OutBack(k));
        if (k >= 1f && !card.Landed)
        {
            card.Landed = true;
            Ring(Local(card.Root, Vector2.zero), KitStyle.Accent, 200f, 760f, .32f);
            shake = Mathf.Max(shake, 9f);
        }
        float flip = Mathf.Clamp01((since - start - DealSeconds) / FlipSeconds);
        bool front = flip >= .5f;
        if (card.Front.gameObject.activeSelf != front) card.Front.gameObject.SetActive(front);
        if (card.Back.gameObject.activeSelf == front) card.Back.gameObject.SetActive(!front);
        float face = front ? (flip - .5f) * 2f : 1f - flip * 2f;
        card.Body.anchoredPosition = new Vector2(0f, y);
        card.Body.localScale = new Vector3(scale * Mathf.Max(.02f, face), scale, 1f);

        // A shine runs across now and then, each card in turn.
        float shine = Mathf.Repeat(time + index * .35f, ShineEvery) / ShineSeconds;
        card.Shine.anchoredPosition = new Vector2(shine < 1f ? Mathf.Lerp(-CardWidth, CardWidth, shine) : CardWidth * 2f, 0f);

        bool over = pointing && flip >= 1f && RectTransformUtility.RectangleContainsScreenPoint(card.Root, mouse.position.ReadValue(), null);
        card.Hover = Mathf.MoveTowards(card.Hover, over || picked == index ? 1f : 0f, dt / HoverSeconds);
        Paint(card, card.Hover);
        card.Glow.color = KitStyle.With(KitStyle.Accent, .5f * card.Hover + (card.Perk != null && card.Perk.Final ? .18f : 0f));

        Vector2 lift = new Vector2(0f, 26f * KitReveal.EaseOut(card.Hover));
        float grow = 1f + .05f * card.Hover;
        float spin = 0f;
        if (picked == index)
        {
            // A punch, then it shrinks into its badge at the bottom.
            float punch = Mathf.Clamp01(end / PickPunch);
            float fly = Mathf.Clamp01((end - PickPunch) / PickFly);
            grow *= 1f + .14f * Mathf.Sin(punch * Mathf.PI);
            if (landing != null && fly > 0f)
            {
                float eased = fly * fly;
                Vector2 target = Local(card.Root, landing, Vector2.zero);
                lift = Vector2.LerpUnclamped(lift, target, eased);
                grow = Mathf.Lerp(grow, BadgeHeight / CardHeight, eased);
                spin = Mathf.Lerp(0f, -Tilts[index % Tilts.Length], eased);
                card.Glow.color = KitStyle.With(KitStyle.Accent, .5f * (1f - fly));
            }
        }
        else if (picked >= 0 || closing)
        {
            float fall = picked >= 0 ? Mathf.Clamp01(end / .4f) : Mathf.Clamp01(end / CloseSeconds);
            lift += new Vector2((index - 1) * 120f * fall, -900f * fall * fall);
            spin = (index % 2 == 0 ? 1f : -1f) * 28f * fall;
        }
        card.Lift.anchoredPosition = lift;
        card.Lift.localScale = new Vector3(grow, grow, 1f);
        card.Lift.localRotation = Quaternion.Euler(0f, 0f, spin);
    }

    // The card under the pointer (or picked) turns pink with dark type. A class's final card (궁극) wears a pink frame
    // from the start, so it reads apart before it is pointed at.
    private static void Paint(Card card, float hover)
    {
        bool final = card.Perk != null && card.Perk.Final;
        card.Frame.color = Color.Lerp(final ? KitStyle.Accent : KitStyle.Ice, KitStyle.Accent, hover);
        card.Plate.color = Color.Lerp(KitStyle.With(KitStyle.Night, .97f), KitStyle.Accent, hover);
        Color ink = Color.Lerp(KitStyle.Text, KitStyle.Ink, hover);
        card.Name.color = card.Tag.color = ink;
        card.Icon.color = ink;
        card.Text.color = Color.Lerp(KitStyle.Soft, KitStyle.With(KitStyle.Ink, .8f), hover);
        card.Why.color = Color.Lerp(KitStyle.With(KitStyle.Soft, .72f), KitStyle.With(KitStyle.Ink, .6f), hover);
        card.Step.color = Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover);
        card.TagBack.color = Color.Lerp(final ? KitStyle.With(KitStyle.Accent, .35f) : KitStyle.With(KitStyle.Ice, .15f),
            KitStyle.With(KitStyle.Ink, .15f), hover);
        card.Owned.color = Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover);
        card.KeyBack.color = Color.Lerp(KitStyle.Ice, KitStyle.Ink, hover);
        card.Key.color = Color.Lerp(KitStyle.Ink, KitStyle.Accent, hover);
        if (card.Perk == null) return;
        int level = PlayerPerks.Level(card.Perk.Id);
        for (int i = 0; i < card.Pips.Length; i++)
        {
            if (!card.Pips[i].gameObject.activeSelf) continue;
            card.Pips[i].color = i < level ? ink
                : i == level ? Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover)
                : Color.Lerp(KitStyle.Faint, KitStyle.With(KitStyle.Ink, .25f), hover);
        }
    }
}
