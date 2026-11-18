
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class NarrativeEventManager : MonoBehaviour
{
    private static NarrativeEventManager instance;

    public static NarrativeEventManager GetInstance()
    {
        return instance;
    }

    // =====================================================
    // PHYSICAL EVENTS
    // =====================================================

    [Serializable]
    public class NarrativeEvent
    {
        public int eventID;
        public Transform npc;
        public Transform destination;
        public float moveSpeed = 2f;
    }

    public List<NarrativeEvent> events = new List<NarrativeEvent>();

    // =====================================================
    // DIALOGUE NARRATIVES
    // =====================================================

    public enum TriggerType
    {
        Interaction,
        Automatic,
        Both
    }

    [Serializable]
    public class DialogueNarrative
    {
        [Header("Identity")]
        public int eventID;
        public Transform npc;
        public TextAsset inkJSON;

        [Header("Trigger")]
        public TriggerType triggerType;
        public int priority = 0;

        [Header("Progression")]
        public bool playOnce = true;
        public List<int> requiredEvents = new List<int>();
    }

    [Header("Dialogue Narratives")]
    public List<DialogueNarrative> narratives =
        new List<DialogueNarrative>();

    // =====================================================
    // NARRATIVE STATE
    // =====================================================

    private HashSet<int> completedNarratives =
        new HashSet<int>();

    private Queue<int> automaticQueue =
        new Queue<int>();

    private HashSet<int> queuedNarratives =
        new HashSet<int>();

    private bool narrativeIsPlaying = false;

    // =====================================================
    // INITIALIZATION
    // =====================================================

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning(
                "Found more than one NarrativeEventManager!"
            );
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Update()
    {
        ProcessAutomaticQueue();
    }

    // =====================================================
    // PHYSICAL EVENT EXECUTION
    // =====================================================

    public void PlayEvent(int eventID, Action onComplete)
    {
        NarrativeEvent selectedEvent =
            events.Find(e => e.eventID == eventID);

        if (selectedEvent == null)
        {
            Debug.LogWarning("Event not found: " + eventID);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(MoveNPC(selectedEvent, onComplete));
    }

    IEnumerator MoveNPC(NarrativeEvent e, Action onComplete)
    {
        if (e.npc == null || e.destination == null)
        {
            Debug.LogWarning(
                "Missing NPC or destination: " + e.eventID
            );

            onComplete?.Invoke();
            yield break;
        }

        float targetX = e.destination.position.x;

        while (Mathf.Abs(e.npc.position.x - targetX) > 0.05f)
        {
            Vector3 targetPosition = new Vector3(
                targetX,
                e.npc.position.y,
                e.npc.position.z
            );

            e.npc.position = Vector3.MoveTowards(
                e.npc.position,
                targetPosition,
                e.moveSpeed * Time.deltaTime
            );

            yield return null;
        }

        e.npc.position = new Vector3(
            targetX,
            e.npc.position.y,
            e.npc.position.z
        );

        onComplete?.Invoke();
    }

    // =====================================================
    // NARRATIVE PROGRESSION
    // =====================================================

    public bool HasNarrativeCompleted(int eventID)
    {
        return completedNarratives.Contains(eventID);
    }

    private bool IsNarrativeEligible(DialogueNarrative narrative)
    {
        if (narrative == null || narrative.inkJSON == null)
            return false;

        if (narrative.playOnce &&
            completedNarratives.Contains(narrative.eventID))
            return false;

        foreach (int requiredID in narrative.requiredEvents)
        {
            if (!completedNarratives.Contains(requiredID))
                return false;
        }

        return true;
    }

    private DialogueNarrative GetNextNarrative(
        Transform npc,
        TriggerType requestedTrigger
    )
    {
        DialogueNarrative selected = null;

        foreach (DialogueNarrative narrative in narratives)
        {
            if (narrative.npc != npc)
                continue;

            bool triggerMatches =
                narrative.triggerType == requestedTrigger ||
                narrative.triggerType == TriggerType.Both;

            if (!triggerMatches)
                continue;

            if (!IsNarrativeEligible(narrative))
                continue;

            if (selected == null ||
                narrative.priority > selected.priority)
            {
                selected = narrative;
            }
        }

        return selected;
    }

    // =====================================================
    // PLAYER INTERACTION
    // =====================================================

    public void InteractWithNPC(
        Transform npc,
        TextAsset defaultInkJSON
    )
    {
        if (DialogueManager.GetInstance().dialogueIsPlaying)
            return;

        // Automatic narratives have priority over new interactions.
        if (automaticQueue.Count > 0)
        {
            ProcessAutomaticQueue();
            return;
        }

        DialogueNarrative selected =
            GetNextNarrative(npc, TriggerType.Interaction);

        if (selected != null)
        {
            StartNarrative(selected);
            return;
        }

        // No special narrative available.
        // Use the NPC's default dialogue.
        if (defaultInkJSON != null)
        {
            DialogueManager.GetInstance().EnterDialogueMode(
                defaultInkJSON
            );
        }
        else
        {
            Debug.LogWarning(
                "No available narrative or default dialogue for NPC: "
                + npc.name
            );
        }
    }

    // =====================================================
    // AUTOMATIC NARRATIVE QUEUE
    // =====================================================

    public void QueueNarrative(int eventID)
    {
        DialogueNarrative narrative =
            narratives.Find(n => n.eventID == eventID);

        if (narrative == null)
        {
            Debug.LogWarning(
                "Narrative not found: " + eventID
            );
            return;
        }

        if (narrative.triggerType != TriggerType.Automatic &&
            narrative.triggerType != TriggerType.Both)
        {
            Debug.LogWarning(
                "Narrative is not configured for automatic triggering: "
                + eventID
            );
            return;
        }

        if (!IsNarrativeEligible(narrative))
            return;

        if (queuedNarratives.Contains(eventID))
            return;

        automaticQueue.Enqueue(eventID);
        queuedNarratives.Add(eventID);

        Debug.Log("Narrative queued: " + eventID);
    }

    private void ProcessAutomaticQueue()
    {
        if (narrativeIsPlaying)
            return;

        if (DialogueManager.GetInstance() == null)
            return;

        if (DialogueManager.GetInstance().dialogueIsPlaying)
            return;

        if (automaticQueue.Count == 0)
            return;

        int eventID = automaticQueue.Dequeue();
        queuedNarratives.Remove(eventID);

        DialogueNarrative narrative =
            narratives.Find(n => n.eventID == eventID);

        if (narrative == null)
            return;

        // Requirements may have changed while this was queued.
        if (!IsNarrativeEligible(narrative))
            return;

        StartNarrative(narrative);
    }

    // =====================================================
    // START AND COMPLETE NARRATIVES
    // =====================================================

    private void StartNarrative(DialogueNarrative narrative)
    {
        if (narrativeIsPlaying)
            return;

        if (DialogueManager.GetInstance() == null)
            return;

        narrativeIsPlaying = true;

        DialogueManager.GetInstance().EnterDialogueMode(
            narrative.inkJSON,
            () => OnNarrativeCompleted(narrative.eventID)
        );
    }

    private void OnNarrativeCompleted(int eventID)
    {
        DialogueNarrative narrative =
            narratives.Find(n => n.eventID == eventID);

        if (narrative != null && narrative.playOnce)
        {
            completedNarratives.Add(eventID);
        }

        narrativeIsPlaying = false;

        Debug.Log("Narrative completed: " + eventID);

        ProcessAutomaticQueue();
    }
}
