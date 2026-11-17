
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class NarrativeEventManager : MonoBehaviour
{
    [Serializable]
    public class NarrativeEvent
    {
        public int eventID;
        public Transform npc;
        public Transform destination;
        public float moveSpeed = 2f;
    }

    public List<NarrativeEvent> events = new List<NarrativeEvent>();

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
            Debug.LogWarning("Missing NPC or destination: " + e.eventID);
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

        // Snap to the exact destination X position.
        e.npc.position = new Vector3(
            targetX,
            e.npc.position.y,
            e.npc.position.z
        );

        onComplete?.Invoke();
    }
}
