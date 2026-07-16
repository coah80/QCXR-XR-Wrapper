using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class UIHandler : MonoBehaviour
{
    private readonly ConcurrentQueue<string> pendingErrors = new ConcurrentQueue<string>();
    private int mainThreadId;
    public TextMeshProUGUI minuteHourText;
    public TMP_Dropdown dropdownMain;
    public TMP_Dropdown dropdownModSearch;
    public TMP_Dropdown dropdownModInfo;
    public TMP_Dropdown dropdownInstanceCreator;
    public GameObject legalContent;
    public GameObject errorMenu;
    public GameObject logPanel;
    public Button legalContinue;
    public Toggle modToggle;
    public Toggle modpacksToggle;
    public Toggle resourcePacksToggle;
    public Button modsButton;
    public Button instancesButton;
    public Button playButton;
    public Button killButton;
    public Button logoutButton;
    public Button needHelpButton;
    public static int selectedInstance;
    public string pfpUrl;
    static string profileName;
    public ModManager modManager;
    public LoginHandler loginHandler;

    void Awake()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    void Start()
    {
        // Add listeners for toggle buttons
        modToggle.onValueChanged.AddListener((value) => OnToggleClicked(value, modToggle));
        modpacksToggle.onValueChanged.AddListener((value) => OnToggleClicked(value, modpacksToggle));
        resourcePacksToggle.onValueChanged.AddListener((value) => OnToggleClicked(value, resourcePacksToggle));
        
        dropdownMain.onValueChanged.AddListener(delegate
        {
            selectedInstance = dropdownMain.value;
            UpdateDropdowns(false, null);
        });
        dropdownInstanceCreator.onValueChanged.AddListener(delegate
        {
            selectedInstance = dropdownInstanceCreator.value;
            UpdateDropdowns(false, null);
        });
        dropdownModInfo.onValueChanged.AddListener(delegate
        {
            selectedInstance = dropdownModInfo.value;
            UpdateDropdowns(false, null);
        });
        dropdownModSearch.onValueChanged.AddListener(delegate
        {
            selectedInstance = dropdownModSearch.value;
            UpdateDropdowns(false, null);
        });
        
        dropdownMain.interactable = false;
        modsButton.interactable = false;
        instancesButton.interactable = false;
        playButton.interactable = false;

        CloneHelpButton("SendLogsButton", "Send Logs",
            new Vector2(0f, -(((RectTransform)needHelpButton.transform).rect.height + 10f)),
            () => StartCoroutine(LogUploader.SendLogs(this)));

        StartCoroutine(UpdateChecker.CheckForUpdate(ShowUpdateButton));

        CheckForPreviousCrash();
    }

    private Button updateButton;

    public void ShowUpdateButton(string releaseUrl)
    {
        if (updateButton != null) return;
        updateButton = CloneHelpButton("UpdateButton", "Update!",
            new Vector2(-(((RectTransform)needHelpButton.transform).rect.width + 10f), 0f),
            () => Application.OpenURL(releaseUrl));
        FlashRed(updateButton);
    }

    private void CheckForPreviousCrash()
    {
        long newest = LogUploader.NewestCrashTimestamp();

        if (!PlayerPrefs.HasKey("lastCrashAck"))
        {
            PlayerPrefs.SetString("lastCrashAck", newest.ToString());
            PlayerPrefs.Save();
            return;
        }

        if (newest == 0) return;

        long acknowledged = 0;
        long.TryParse(PlayerPrefs.GetString("lastCrashAck", "0"), out acknowledged);
        if (newest <= acknowledged) return;

        ShowCrashMenu(newest);
    }

    private void ShowCrashMenu(long crashTimestamp)
    {
        GameObject crashMenu = Instantiate(errorMenu, errorMenu.transform.parent);
        crashMenu.name = "CrashMenu";

        Button rootButton = crashMenu.GetComponent<Button>();
        Button okButton = null;
        foreach (Button candidate in crashMenu.GetComponentsInChildren<Button>(true))
        {
            if (candidate.transform != crashMenu.transform)
            {
                okButton = candidate;
                break;
            }
        }
        if (okButton == null)
        {
            Destroy(crashMenu);
            SetAndShowError("Oops! game crashed. Use Send Logs to report it.");
            return;
        }

        foreach (TextMeshProUGUI text in crashMenu.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!text.transform.IsChildOf(okButton.transform))
            {
                text.text = "Oops! game crashed.";
                break;
            }
        }

        void Acknowledge()
        {
            PlayerPrefs.SetString("lastCrashAck", crashTimestamp.ToString());
            PlayerPrefs.Save();
            Destroy(crashMenu);
        }

        SetupDialogButton(okButton, "okay.", Acknowledge);
        if (rootButton != null)
        {
            SetupDialogButton(rootButton, null, Acknowledge);
        }

        Button sendButton = Instantiate(okButton.gameObject, okButton.transform.parent).GetComponent<Button>();
        sendButton.gameObject.name = "SendLogsCrashButton";
        RectTransform okRt = (RectTransform)okButton.transform;
        RectTransform sendRt = (RectTransform)sendButton.transform;
        float shift = okRt.rect.width * 0.5f + 8f;
        Vector2 basePos = okRt.anchoredPosition;
        okRt.anchoredPosition = basePos + new Vector2(-shift, 0f);
        sendRt.anchoredPosition = basePos + new Vector2(shift, 0f);
        SetupDialogButton(sendButton, "send logs", () =>
        {
            Acknowledge();
            StartCoroutine(LogUploader.SendLogs(this, "Oops! game crashed."));
        });

        crashMenu.SetActive(true);
    }

    private void SetupDialogButton(Button button, string text, Action onClick)
    {
        if (text != null)
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
        }
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick());
    }

    private Button CloneHelpButton(string name, string text, Vector2 offset, Action onClick)
    {
        return CloneButton(needHelpButton, name, text, offset, onClick);
    }

    public static Button CloneButton(Button template, string name, string text, Vector2 offset, Action onClick)
    {
        GameObject clone = Instantiate(template.gameObject, template.transform.parent);
        clone.name = name;

        RectTransform src = (RectTransform)template.transform;
        RectTransform rt = (RectTransform)clone.transform;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition + offset;

        TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = text;

        Button button = clone.GetComponent<Button>();
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick());
        button.interactable = true;
        return button;
    }

    private void FlashRed(Button button)
    {
        Image image = button.GetComponent<Image>();
        if (image == null) image = button.GetComponentInChildren<Image>();
        if (image == null) return;

        Color baseColor = image.color;
        Color red = new Color(0.85f, 0.1f, 0.1f, baseColor.a);
        LeanTween.value(button.gameObject, 0f, 1f, 0.6f)
            .setLoopPingPong()
            .setOnUpdate((float t) => image.color = Color.Lerp(baseColor, red, t));
    }
    
    void Update()
    {
        while (pendingErrors.TryDequeue(out string errorMessage))
        {
            ShowError(errorMessage);
        }
        string time = DateTime.Now.ToString("hh:mm tt");
        minuteHourText.text = time;
        UILoginCheck();
    }

    public void UILoginCheck()
    {
        if (loginHandler.selectedAccountUsername != "Add Account" && !loginHandler.isDemoMode)
        {
            dropdownMain.interactable = true;
            modsButton.interactable = true;
            instancesButton.interactable = true;
            playButton.interactable = true;
        }
        else if (loginHandler.isDemoMode)
        {
            dropdownMain.interactable = false;
            modsButton.interactable = false;
            instancesButton.interactable = false;
            playButton.interactable = true;
        }
        else
        {
            dropdownMain.interactable = false;
            modsButton.interactable = false;
            instancesButton.interactable = false;
            playButton.interactable = false;
        }
    }
    
    void OnToggleClicked(bool value, Toggle clickedToggle)
    {
        if (modManager.isSearching)
        {
            clickedToggle.isOn = false;
            return;
        }
        
        if (value)
        {
            // Enable the toggles
            Toggle[] allToggles = { modToggle, modpacksToggle, resourcePacksToggle };
            foreach (Toggle toggle in allToggles)
                toggle.interactable = true;

            clickedToggle.isOn = false;
            // Disable the clicked toggle
            clickedToggle.interactable = false;
            modManager.SearchMods();
        }
    }

    public void UpdateDropdowns(bool init, List<string> list)
    {
        if (init)
        {
            dropdownMain.AddOptions(list);
            dropdownInstanceCreator.AddOptions(list);
            dropdownModInfo.AddOptions(list);
            dropdownModSearch.AddOptions(list);
        }
        else
        {
            dropdownMain.SetValueWithoutNotify(selectedInstance);
            dropdownInstanceCreator.SetValueWithoutNotify(selectedInstance);
            dropdownModInfo.SetValueWithoutNotify(selectedInstance);
            dropdownModSearch.SetValueWithoutNotify(selectedInstance);
        }
    }

    public void ClearDropdowns()
    {
        dropdownMain.ClearOptions();
        dropdownInstanceCreator.ClearOptions();
        dropdownModInfo.ClearOptions();
        dropdownModSearch.ClearOptions();
    }

    public void SetAndShowError(String errorMessage)
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
        {
            pendingErrors.Enqueue(errorMessage);
            return;
        }
        ShowError(errorMessage);
    }

    private void ShowError(string errorMessage)
    {
        errorMenu.GetComponentInChildren<TextMeshProUGUI>().text = errorMessage;
        errorMenu.SetActive(true);
    }

    public void UpdateLegalButton()
    {
        ScrollRect scroll = legalContent.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null)
        {
            legalContinue.interactable = true;
            return;
        }

        bool scrollable = scroll.content.rect.height > scroll.viewport.rect.height + 1f;
        if (!scrollable || scroll.verticalNormalizedPosition <= 0.02f)
        {
            legalContinue.interactable = true;
        }
    }

    public void PlaySetter()
    {
        needHelpButton.interactable = false;
        logoutButton.interactable = false;
        dropdownMain.interactable = false;
        modsButton.interactable = false;
        instancesButton.interactable = false;
        playButton.gameObject.SetActive(false);
        killButton.gameObject.SetActive(true);
        logPanel.SetActive(true);
    }   
    
    public void KillSetter()
    {
        needHelpButton.interactable = true;
        logoutButton.interactable = true;
        dropdownMain.interactable = true;
        modsButton.interactable = true;
        instancesButton.interactable = true;
        playButton.gameObject.SetActive(true);
        killButton.gameObject.SetActive(false);
        logPanel.SetActive(false);
    }
}
