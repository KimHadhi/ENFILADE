
using UnityEngine;
using UnityEngine.UI;

public class SettingsTabController : MonoBehaviour
{
    public GameObject generalContent;
    public GameObject soundContent;
    public GameObject keyBindingContent;

    public Button generalButton;
    public Button soundButton;
    public Button keyBindingButton;

    public Color selectedColor = new Color(0.45f, 0.55f, 0.75f);
    public Color normalColor = new Color(0.18f, 0.22f, 0.30f);

    void Start()
    {
        ShowGeneral();
    }

    public void ShowGeneral()
    {
        generalContent.SetActive(true);
        soundContent.SetActive(false);
        keyBindingContent.SetActive(false);

        UpdateButtonColors(0);
    }

    public void ShowSound()
    {
        generalContent.SetActive(false);
        soundContent.SetActive(true);
        keyBindingContent.SetActive(false);

        UpdateButtonColors(1);
    }

    public void ShowKeyBinding()
    {
        generalContent.SetActive(false);
        soundContent.SetActive(false);
        keyBindingContent.SetActive(true);

        UpdateButtonColors(2);
    }

    void UpdateButtonColors(int selectedTab)
    {
        generalButton.image.color =
            selectedTab == 0 ? selectedColor : normalColor;

        soundButton.image.color =
            selectedTab == 1 ? selectedColor : normalColor;

        keyBindingButton.image.color =
            selectedTab == 2 ? selectedColor : normalColor;
    }
}
