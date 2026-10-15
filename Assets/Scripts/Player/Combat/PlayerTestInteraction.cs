using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads Player/Interact; never owns or enables the Player action map.</summary>
[DefaultExecutionOrder(260), DisallowMultipleComponent]
public sealed class PlayerTestInteraction : MonoBehaviour
{
    [SerializeField] private Camera view;
    [SerializeField] private SinglePlayerChatManager chat;
    [SerializeField] private TMP_Text prompt;
    [SerializeField, Min(.5f)] private float reach=3.5f;
    private PlayerMovement player;
    private PlayerInput input;
    private InputAction interact;
    public ChatTestStation Target { get; private set; }
    private void Start()
    {
        player=GetComponent<PlayerMovement>(); input=GetComponent<PlayerInput>();
        if(view==null) view=GetComponentInChildren<Camera>();
        interact=(input!=null ? input.actions : InputSystem.actions)?.FindAction("Player/Interact",false);
    }
    private void Update()
    {
        Target=null;
        bool available=player!=null && player.InputAvailable && (chat==null||!chat.IsChatOpen) && interact!=null && interact.enabled;
        if(available && view!=null && Physics.Raycast(view.transform.position,view.transform.forward,out RaycastHit hit,reach,~(1<<gameObject.layer),QueryTriggerInteraction.Ignore))
            Target=hit.collider.GetComponentInParent<ChatTestStation>();
        if(prompt!=null) { prompt.gameObject.SetActive(Target!=null); if(Target!=null) prompt.text=Target.Prompt; }
        if(Target!=null && interact.WasPressedThisFrame()) Target.Activate(chat);
    }
    private void OnDisable() { Target=null; if(prompt!=null) prompt.gameObject.SetActive(false); }
}
