using System;
using TMPro;
using UnityEngine;

/// <summary>
/// A testing room's station worked with Interact. The key is shared: the station says what F would do to the interact
/// key's one owner and is worked on the press the owner gives it (InteractKey); it never reads the key or the Player
/// action map itself.
/// </summary>
[DefaultExecutionOrder(FrameOrder.TestInteraction), DisallowMultipleComponent]
public sealed class PlayerTestInteraction : MonoBehaviour
{
    /// <summary>Working a testing room's station, as the interact key's one owner knows it (InteractKey).</summary>
    public const string OfferId = "test.station";

    [SerializeField] private Camera view;
    [SerializeField] private SinglePlayerChatManager chat;
    [SerializeField] private TMP_Text prompt;
    [SerializeField, Min(.5f)] private float reach=3.5f;
    private PlayerMovement player;
    public ChatTestStation Target { get; private set; }
    private void Start()
    {
        player=GetComponent<PlayerMovement>();
        if(view==null) view=GetComponentInChildren<Camera>();
        // The player is a prefab, so it finds the chat and the prompt on this scene's HUD.
        if(chat==null) chat=SceneOne<SinglePlayerChatManager>.Get();
        if(prompt==null && chat!=null)
            prompt=Array.Find(chat.transform.root.GetComponentsInChildren<TMP_Text>(true),text=>text.name=="InteractionPrompt");
    }
    private void Update()
    {
        Target=null;
        // What the body can do and whether the chat or the console is typed into; a takedown on offer for the same key is
        // not asked here any more (that it comes first is the key's order).
        bool available=player!=null && player.InputAvailable && !player.CombatInputLocked
            && (chat==null||!chat.IsChatOpen) && !DevConsole.IsOpen;
        if(available && view!=null && Physics.Raycast(view.transform.position,view.transform.forward,out RaycastHit hit,reach,~(1<<gameObject.layer),QueryTriggerInteraction.Ignore))
            Target=hit.collider.GetComponentInParent<ChatTestStation>();
        if(Target!=null) InteractKey.Offer(OfferId, target: Target);
        bool shown=Target!=null && InteractKey.Shows(OfferId);
        if(prompt!=null) { prompt.gameObject.SetActive(shown); if(shown) prompt.text=Target.Prompt; }
        if(Target!=null && InteractKey.Pressed(OfferId)) Target.Activate(chat);
    }
    private void OnDisable() { Target=null; if(prompt!=null) prompt.gameObject.SetActive(false); }
}
