
using UnityEngine;

public class ExitGame : MonoBehaviour
{
    public GameObject exitConfirmPanel;

    public void OpenExitPanel()
    {
        exitConfirmPanel.SetActive(true);
    }

    public void CloseExitPanel()
    {
        exitConfirmPanel.SetActive(false);
    }

    public void QuitGame()
    {
        Debug.Log("게임 종료");
        Application.Quit();
    }
}
