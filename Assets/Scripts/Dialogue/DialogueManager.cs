
using System;
using System.Collections;
using System.Collections.Generic;
using Ink.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DialogueManager : MonoBehaviour
{
    [Header("Params")]
    [SerializeField] private float typingSpeed = 0.04f;

    [Header("Load Globals JSON")]
    [SerializeField] private TextAsset loadGlobalsJSON;

    [Header("Dialogue UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private GameObject continueIcon;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private TextMeshProUGUI displayNameText;
    [SerializeField] private Animator portraitAnimator;
    private Animator layoutAnimator;
    private Action onDialogueComplete;


    [Header("Choices UI")]
    [SerializeField] private GameObject[] choices;
    private TextMeshProUGUI[] choicesText;

    [Header("Narrative Events")]
    [SerializeField] private NarrativeEventManager narrativeEventManager;

    [Header("Audio")]
    [SerializeField] private DialogueAudioInfoSO defaultAudioInfo;
    [SerializeField] private DialogueAudioInfoSO[] audioInfos;
    [SerializeField] private bool makePredictable;
    private DialogueAudioInfoSO currentAudioInfo;
    private Dictionary<string, DialogueAudioInfoSO> audioInfoDictionary;
    private AudioSource audioSource;

    private Story currentStory;
    public bool dialogueIsPlaying { get; private set; }

    private bool canContinueToNextLine = false;
    private bool isWaitingForEvent = false;

    // Stores a position event until the player finishes reading the line.
    private bool hasPendingPositionEvent = false;
    private int pendingPositionEventID;

    private Coroutine displayLineCoroutine;

    private static DialogueManager instance;

    private const string SPEAKER_TAG = "speaker";
    private const string PORTRAIT_TAG = "portrait";
    private const string LAYOUT_TAG = "layout";
    private const string AUDIO_TAG = "audio";
    private const string PAUSE_TAG = "pause";
    private const string POSITION_TAG = "position";

    private DialogueVariables dialogueVariables;
    private float dialoguePause = 0f;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("Found more than one Dialogue Manager in the scene");
        }

        instance = this;

        dialogueVariables = new DialogueVariables(loadGlobalsJSON);

        audioSource = this.gameObject.AddComponent<AudioSource>();
        currentAudioInfo = defaultAudioInfo;
    }

    public static DialogueManager GetInstance()
    {
        return instance;
    }

    private void Start()
    {
        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);

        layoutAnimator = dialoguePanel.GetComponent<Animator>();

        choicesText = new TextMeshProUGUI[choices.Length];

        int index = 0;

        foreach (GameObject choice in choices)
        {
            choicesText[index] = choice.GetComponentInChildren<TextMeshProUGUI>();
            index++;
        }

        InitializeAudioInfoDictionary();
    }

    private void InitializeAudioInfoDictionary()
    {
        audioInfoDictionary = new Dictionary<string, DialogueAudioInfoSO>();

        audioInfoDictionary.Add(defaultAudioInfo.id, defaultAudioInfo);

        foreach (DialogueAudioInfoSO audioInfo in audioInfos)
        {
            audioInfoDictionary.Add(audioInfo.id, audioInfo);
        }
    }

    private void SetCurrentAudioInfo(string id)
    {
        DialogueAudioInfoSO audioInfo = null;

        audioInfoDictionary.TryGetValue(id, out audioInfo);

        if (audioInfo != null)
        {
            this.currentAudioInfo = audioInfo;
        }
        else
        {
            Debug.LogWarning("Failed to find audio info for id: " + id);
        }
    }

    private void Update()
    {
        if (!dialogueIsPlaying)
        {
            return;
        }

        if (canContinueToNextLine
            && currentStory.currentChoices.Count == 0
            && InputManager.GetInstance().GetSubmitPressed())
        {
            ContinueStory();
        }
    }


    public void EnterDialogueMode(
        TextAsset inkJSON,
        Action onComplete = null
    )
    {
        if (dialogueIsPlaying)
        {
            Debug.LogWarning(
                "Cannot start dialogue: another dialogue is already playing."
            );
            return;
        }

        if (inkJSON == null)
        {
            Debug.LogWarning("Cannot start dialogue: Ink JSON is missing.");
            return;
        }

        onDialogueComplete = onComplete;

        currentStory = new Story(inkJSON.text);

        dialogueIsPlaying = true;
        dialoguePanel.SetActive(true);

        dialogueVariables.StartListening(currentStory);

        displayNameText.text = "???";
        portraitAnimator.Play("default");
        layoutAnimator.Play("right");

        hasPendingPositionEvent = false;
        isWaitingForEvent = false;

        ContinueStory();
    }



    private IEnumerator ExitDialogueMode()
    {
        yield return new WaitForSeconds(0.2f);

        dialogueVariables.StopListening(currentStory);

        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);
        dialogueText.text = "";

        SetCurrentAudioInfo(defaultAudioInfo.id);

        Action callback = onDialogueComplete;
        onDialogueComplete = null;

        callback?.Invoke();
    }


    private void ContinueStory()
    {
        // Don't continue while an NPC is moving.
        if (isWaitingForEvent)
        {
            return;
        }

        // If the current line has a position tag,
        // execute the movement AFTER the player presses Continue.
        if (hasPendingPositionEvent)
        {
            isWaitingForEvent = true;
            canContinueToNextLine = false;

            continueIcon.SetActive(false);
            HideChoices();

            if (narrativeEventManager != null)
            {
                narrativeEventManager.PlayEvent(
                    pendingPositionEventID,
                    OnNarrativeEventComplete
                );
            }
            else
            {
                Debug.LogWarning("NarrativeEventManager is missing!");

                OnNarrativeEventComplete();
            }

            return;
        }

        if (currentStory.canContinue)
        {
            if (displayLineCoroutine != null)
            {
                StopCoroutine(displayLineCoroutine);
            }

            canContinueToNextLine = false;

            string nextLine = currentStory.Continue();

            HandleTags(currentStory.currentTags);

            displayLineCoroutine = StartCoroutine(
                DisplayLineWithPause(nextLine)
            );
        }
        else
        {
            StartCoroutine(ExitDialogueMode());
        }
    }

    private void OnNarrativeEventComplete()
    {
        isWaitingForEvent = false;
        hasPendingPositionEvent = false;

        // Continue with the next Ink line after the NPC arrives.
        ContinueStory();
    }

    private IEnumerator DisplayLineWithPause(string line)
    {
        if (dialoguePause > 0f)
        {
            yield return new WaitForSeconds(dialoguePause);
            dialoguePause = 0f;
        }

        yield return StartCoroutine(DisplayLine(line));
    }

    private IEnumerator DisplayLine(string line)
    {
        dialogueText.text = line;
        dialogueText.maxVisibleCharacters = 0;

        continueIcon.SetActive(false);
        HideChoices();

        canContinueToNextLine = false;

        bool isAddingRichTextTag = false;

        foreach (char letter in line.ToCharArray())
        {
            if (InputManager.GetInstance().GetSubmitPressed())
            {
                dialogueText.maxVisibleCharacters = line.Length;
                break;
            }

            if (letter == '<' || isAddingRichTextTag)
            {
                isAddingRichTextTag = true;

                if (letter == '>')
                {
                    isAddingRichTextTag = false;
                }
            }
            else
            {
                PlayDialogueSound(
                    dialogueText.maxVisibleCharacters,
                    dialogueText.text[dialogueText.maxVisibleCharacters]
                );

                dialogueText.maxVisibleCharacters++;

                yield return new WaitForSeconds(typingSpeed);
            }
        }

        // The line is fully displayed.
        // The player must press Continue before any movement starts.
        continueIcon.SetActive(true);

        DisplayChoices();

        canContinueToNextLine = true;
    }

    private void PlayDialogueSound(int currentDisplayedCharacterCount, char currentCharacter)
    {
        AudioClip[] dialogueTypingSoundClips = currentAudioInfo.dialogueTypingSoundClips;
        int frequencyLevel = currentAudioInfo.frequencyLevel;
        float minPitch = currentAudioInfo.minPitch;
        float maxPitch = currentAudioInfo.maxPitch;
        bool stopAudioSource = currentAudioInfo.stopAudioSource;

        if (currentDisplayedCharacterCount % frequencyLevel == 0)
        {
            if (stopAudioSource)
            {
                audioSource.Stop();
            }

            AudioClip soundClip = null;

            if (makePredictable)
            {
                int hashCode = currentCharacter.GetHashCode();

                int predictableIndex = hashCode % dialogueTypingSoundClips.Length;
                soundClip = dialogueTypingSoundClips[predictableIndex];

                int minPitchInt = (int)(minPitch * 100);
                int maxPitchInt = (int)(maxPitch * 100);
                int pitchRangeInt = maxPitchInt - minPitchInt;

                if (pitchRangeInt != 0)
                {
                    int predictablePitchInt = (hashCode % pitchRangeInt) + minPitchInt;
                    float predictablePitch = predictablePitchInt / 100f;

                    audioSource.pitch = predictablePitch;
                }
                else
                {
                    audioSource.pitch = minPitch;
                }
            }
            else
            {
                int randomIndex = UnityEngine.Random.Range(0, dialogueTypingSoundClips.Length);
                soundClip = dialogueTypingSoundClips[randomIndex];

                audioSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            }

            audioSource.PlayOneShot(soundClip);
        }
    }

    private void HideChoices()
    {
        foreach (GameObject choiceButton in choices)
        {
            choiceButton.SetActive(false);
        }
    }

    private void HandleTags(List<string> currentTags)
    {
        foreach (string tag in currentTags)
        {
            string[] splitTag = tag.Split(':');

            if (splitTag.Length != 2)
            {
                Debug.LogError("Tag could not be appropriately parsed: " + tag);
                continue;
            }

            string tagKey = splitTag[0].Trim();
            string tagValue = splitTag[1].Trim();

            switch (tagKey)
            {
                case SPEAKER_TAG:
                    displayNameText.text = tagValue;
                    break;

                case PORTRAIT_TAG:
                    portraitAnimator.Play(tagValue);
                    break;

                case LAYOUT_TAG:
                    layoutAnimator.Play(tagValue);
                    break;

                case AUDIO_TAG:
                    SetCurrentAudioInfo(tagValue);
                    break;

                case PAUSE_TAG:
                    if (float.TryParse(
                        tagValue,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float pause))
                    {
                        dialoguePause = Mathf.Max(0f, pause);
                    }
                    break;

                case POSITION_TAG:
                    if (int.TryParse(tagValue, out int eventID))
                    {
                        hasPendingPositionEvent = true;
                        pendingPositionEventID = eventID;
                    }
                    else
                    {
                        Debug.LogWarning("Invalid position event ID: " + tagValue);
                    }
                    break;

                default:
                    Debug.LogWarning(
                        "Tag came in but is not currently being handled: " + tag
                    );
                    break;
            }
        }
    }

    private void DisplayChoices()
    {
        List<Choice> currentChoices = currentStory.currentChoices;

        if (currentChoices.Count > choices.Length)
        {
            Debug.LogError(
                "More choices were given than the UI can support. Number of choices given: "
                + currentChoices.Count
            );
        }

        int index = 0;

        foreach (Choice choice in currentChoices)
        {
            choices[index].gameObject.SetActive(true);
            choicesText[index].text = choice.text;
            index++;
        }

        for (int i = index; i < choices.Length; i++)
        {
            choices[i].gameObject.SetActive(false);
        }

        if (currentChoices.Count > 0)
        {
            StartCoroutine(SelectFirstChoice());
        }
    }

    private IEnumerator SelectFirstChoice()
    {
        EventSystem.current.SetSelectedGameObject(null);

        yield return new WaitForEndOfFrame();

        if (choices.Length > 0)
        {
            EventSystem.current.SetSelectedGameObject(choices[0].gameObject);
        }
    }

    public void MakeChoice(int choiceIndex)
    {
        if (canContinueToNextLine && !isWaitingForEvent)
        {
            currentStory.ChooseChoiceIndex(choiceIndex);

            InputManager.GetInstance().RegisterSubmitPressed();

            ContinueStory();
        }
    }

    public Ink.Runtime.Object GetVariableState(string variableName)
    {
        Ink.Runtime.Object variableValue = null;

        dialogueVariables.variables.TryGetValue(variableName, out variableValue);

        if (variableValue == null)
        {
            Debug.LogWarning("Ink Variable was found to be null: " + variableName);
        }

        return variableValue;
    }

    public void OnApplicationQuit()
    {
        dialogueVariables.SaveVariables();
    }
}
