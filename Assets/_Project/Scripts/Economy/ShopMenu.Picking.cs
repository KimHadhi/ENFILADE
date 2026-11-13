using UnityEngine.InputSystem;

// ShopMenu: Picking. One part of the class; the rest is in ShopMenu.cs and the files beside it.
public sealed partial class ShopMenu
{
    // The chat, the console, the settings, the pause menu or the key window has the keyboard: its number keys are theirs.
    private bool KeysBlocked()
    {
        if (DevConsole.TakesKeyboard || SettingsMenu.IsOpen || GamePause.IsPaused || KeyBindings.Busy) return true;
        // Looked for once an opening: a scene without a chat mustn't be searched every frame.
        if (chat == null && !chatLooked)
        {
            chat = FindAnyObjectByType<SinglePlayerChatManager>();
            chatLooked = true;
        }
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    // A number key picks what the page shows under that key, as a click on it would.
    private void PickByKey(Keyboard keyboard)
    {
        for (int i = 0; i < keyed.Count && i < RowKeys.Length; i++)
        {
            if (!keyboard[RowKeys[i]].wasPressedThisFrame && !keyboard[PadKeys[i]].wasPressedThisFrame) continue;
            Pick(keyed[i]);
            return;
        }
    }

    private void Pick(Row row)
    {
        if (!open || reveal.Closing || row == null) return;
        if (row.Back) GoBack();
        else if (row.Group != null)
        {
            KitSound.Play(KitSound.Cue.Ping, .3f);
            ShowPage(row.Group, PageFrom);
        }
        else if (row.Item != null) Buy(row);
    }

    private void GoBack()
    {
        if (shownGroup == null) return;
        KitSound.Play(KitSound.Cue.Ping, .3f);
        ShowPage(null, PageFrom);
    }
}
