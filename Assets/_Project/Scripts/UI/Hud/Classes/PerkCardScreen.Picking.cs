using UnityEngine;

// PerkCardScreen: Opening, picking, closing. One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private void Open()
    {
        var drawn = PlayerPerks.Draw(Choices);
        if (drawn.Count == 0) return;
        dealt = drawn.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            bool used = i < drawn.Count;
            card.Root.gameObject.SetActive(used);
            if (!used) continue;
            Fill(card, drawn[i]);
            card.Hover = 0f;
            card.Landed = false;
            card.Lid.color = Color.clear;
            card.Root.anchoredPosition = new Vector2((i - (dealt - 1) * .5f) * Spread, 30f);
            card.Lift.anchoredPosition = Vector2.zero;
            card.Lift.localScale = Vector3.one;
            card.Lift.localRotation = Quaternion.identity;
        }
        costLabel.text = Texts.Format("perk.cost", Mathf.RoundToInt(player.Ultimate.RequiredCharge));
        practiceLabel.gameObject.SetActive(PlayerPerks.IsPractising);
        BuildTray(null);
        ClearEffects();

        open = true;
        closing = popped = false;
        picked = -1;
        shake = 0f;
        openedAt = Time.unscaledTime;
        layer.gameObject.SetActive(true);
        layerGroup.alpha = 1f;
        Hold(true);
        Animate();
    }

    private void Fill(Card card, PlayerPerks.Perk perk)
    {
        card.Perk = perk;
        int level = PlayerPerks.Level(perk.Id);
        card.Tag.text = perk.Tag;
        card.Icon.texture = KitButtons.Icon(perk.Icon);
        card.Icon.enabled = card.Icon.texture != null;
        card.Name.text = perk.Name;
        card.Why.text = perk.Reason;
        card.Owned.gameObject.SetActive(level > 0);
        string next = level + 1 >= perk.Levels ? Texts.Get("perk.step.last") : Texts.Format("perk.step", level + 1);
        card.Step.text = level == 0 ? Texts.Get("perk.step.new") : Texts.Format("perk.step.owned", level, next);
        card.Text.text = perk.Describe(level + 1);
        int shown = Mathf.Min(perk.Levels, MaxPips);
        float width = shown * 34f + (shown - 1) * 8f;
        for (int i = 0; i < MaxPips; i++)
        {
            var pip = card.Pips[i];
            pip.gameObject.SetActive(i < shown);
            pip.rectTransform.anchoredPosition = new Vector2((CardWidth - width) * .5f + i * 42f, 0f);
        }
        // Dressed by the level this pick brings, its last the grandest, or as a special card (Looks).
        Dress(card);
    }

    // The held perks along the bottom; the one just raised, if any, waits hidden for its card to arrive.
    private void BuildTray(string arriving)
    {
        var made = PerkBadge.Row(tray, PlayerPerks.Held(), BadgeHeight, 12f);
        emptyLabel.gameObject.SetActive(made.Count == 0);
        landing = arriving != null && made.TryGetValue(arriving, out var badge) ? badge : null;
        landingGroup = landing != null ? landing.GetComponent<CanvasGroup>() : null;
        if (landingGroup != null) landingGroup.alpha = 0f;
    }

    private void Pick(int index)
    {
        if (!open || closing || picked >= 0 || index < 0 || index >= dealt) return;
        var card = cards[index];
        if (card.Perk == null || player.Ultimate == null || !player.Ultimate.TryConsume())
        {
            Close();
            return;
        }
        picked = index;
        endedAt = Time.unscaledTime;
        card.Hover = 1f;
        released = false;
        PlayerPerks.Raise(card.Perk.Id);
        BuildTray(card.Perk.Id);
        // Pressed: it goes down and holds a beat before it is let go (Release, from Animate), so the press is felt (the
        // user, 2026-11-14: "눌렀다는 느낌이 덜해. 고른지 모르겠어"). Its sound knows that beat: the click now, the ring at
        // the release; a chord as well for a card picked to its last level or a special one.
        bool grand = card.Tier == Tier.Level3 || card.Tier == Tier.Special;
        KitSound.Play(grand ? KitSound.Cue.CardPickGrand : KitSound.Cue.CardPick);
    }

    // Let go, a beat after the press: one soft flash (the user's rule against strobes: a single flash at most .6 and
    // short), sparks round the card only and a jolt, grander for a grander card (its tier: Tables/PerkCards; its outline
    // and the star at its corner follow in AnimatePick, its stamp in AnimateStamp), and the others catch fire, each a
    // moment apart (AnimateBurn), with one rush of fire for them all.
    private void Release(Card card)
    {
        released = true;
        KitPulse.Play(card.Lid, new Color(1f, 1f, 1f, .6f), .35f);
        var look = card.Look ?? LookOf(Tier.Level1);
        Sparks(card, look);
        shake = Mathf.Max(shake, look.Shake);
        float now = Time.unscaledTime;
        int embers = Mathf.Max(0, Mathf.RoundToInt(LookNumber("Burn", "embers", 34f)));
        bool others = false;
        for (int i = 0; i < dealt; i++)
        {
            if (i == picked) continue;
            cards[i].BurnFrom = now + (float)random.NextDouble() * .06f;
            cards[i].EmbersLeft = embers;
            others = true;
        }
        if (others) KitSound.Play(KitSound.Cue.CardBurn, .8f);
    }

    private void Close()
    {
        if (!open || closing || picked >= 0) return;
        closing = true;
        endedAt = Time.unscaledTime;
    }

    // Everything is away: the game runs again.
    private void Finish()
    {
        open = closing = false;
        picked = -1;
        ClearEffects();
        layer.gameObject.SetActive(false);
        Release();
    }

    private void Release()
    {
        Hold(false);
        closedFrame = Time.frameCount;
    }

    // Alone the game stops, the cursor comes out and every key waits; in a room only the combat keys wait.
    private void Hold(bool hold)
    {
        if (hold)
        {
            bool alone = !GameSession.IsOnline;
            InputHold.Take(this, alone ? Held.All : Held.Combat);
            if (alone) GamePause.Take(this);
            return;
        }
        InputHold.Release(this);
        GamePause.Release(this);
    }
}
