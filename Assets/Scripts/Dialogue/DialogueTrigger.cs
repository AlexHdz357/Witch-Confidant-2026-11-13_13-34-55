
using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [Header("Visual Cue")]
    [SerializeField] private GameObject visualCue;

    [Header("Default Ink JSON")]
    [SerializeField] private TextAsset inkJSON;

    [Header("Automatic Narrative")]
    [SerializeField] private bool triggerAutomatically = false;
    [SerializeField] private int automaticEventID;

    private bool playerInRange;
    private bool automaticNarrativeTriggered;

    private void Awake()
    {
        playerInRange = false;
        automaticNarrativeTriggered = false;

        if (visualCue != null)
        {
            visualCue.SetActive(false);
        }
    }

    private void Update()
    {
        bool canInteract =
            playerInRange &&
            !DialogueManager.GetInstance().dialogueIsPlaying;

        if (visualCue != null)
        {
            visualCue.SetActive(canInteract);
        }

        if (canInteract &&
            InputManager.GetInstance().GetInteractPressed())
        {
            NarrativeEventManager.GetInstance()
                .InteractWithNPC(transform, inkJSON);
        }
    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        if (collider.CompareTag("Player"))
        {
            playerInRange = true;

            if (triggerAutomatically && !automaticNarrativeTriggered)
            {
                automaticNarrativeTriggered = true;

                NarrativeEventManager.GetInstance()
                    .QueueNarrative(automaticEventID);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collider)
    {
        if (collider.CompareTag("Player"))
        {
            playerInRange = false;
        }
    }
}
