using UnityEngine;
using W = SettingsWidgets;

/// <summary>The window for the player's own profile: the nickname others see in the chat.</summary>
public sealed class ProfileView
{
    private const int NameLimit = 16;
    private readonly RectTransform shade, panel;
    private string typed;

    public ProfileView(Transform canvas)
    {
        panel = W.Window(canvas, "Profile", 700f, out shade);
    }

    /// <summary>Showing rebuilds the window, so a change that was cancelled doesn't stay in the box.</summary>
    public void Show()
    {
        for (int i = panel.childCount - 1; i >= 0; i--) Object.Destroy(panel.GetChild(i).gameObject);
        typed = GameSettings.Current.DisplayName;

        W.Size(W.Label(panel, "프로필", 36f, W.Text), height: 54f);
        W.TextInput(W.Row(panel, "닉네임"), typed, name => typed = name).characterLimit = NameLimit;
        W.Size(W.Label(panel, "비워 두면 PC 사용자 이름을 씁니다.", 20f, W.Muted), height: 34f);
        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, "취소", false, Hide);
        W.ActionButton(buttons, "저장", true, Confirm);
        shade.gameObject.SetActive(true);
    }

    private void Hide() => shade.gameObject.SetActive(false);

    private void Confirm()
    {
        GameSettings.Current.nickname = typed.Trim();
        GameSettings.Save();
        Hide();
    }
}
