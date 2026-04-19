using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public class DeveloperConsole : MonoBehaviour
{
    private const int MaxLogLines = 200;
    private const int MaxHistory = 64;
    private const int MaxSuggestions = 8;

    private static DeveloperConsole _instance;

    private Canvas _canvas;
    private GameObject _root;
    private TMP_InputField _input;
    private TextMeshProUGUI _output;
    private ScrollRect _outputScroll;
    private GameObject _suggestionsPanel;
    private readonly List<TextMeshProUGUI> _suggestionItems = new();

    private readonly Queue<string> _logLines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private List<ConsoleCommandRegistry.Entry> _currentSuggestions = new();
    private int _suggestionIndex = -1;

    private bool _isOpen;
    private bool _swallowNextInputChange;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[DeveloperConsole]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DeveloperConsole>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        SetOpen(false);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // ² on French AZERTY = Backquote in the new Input System.
        // We also accept the IntlBackslash key as a fallback on some layouts.
        bool togglePressed = kb.backquoteKey.wasPressedThisFrame;

        if (togglePressed)
        {
            SetOpen(!_isOpen);
            return;
        }

        if (!_isOpen) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
            return;
        }

        if (kb.tabKey.wasPressedThisFrame)
            ApplyAutocomplete();

        if (kb.upArrowKey.wasPressedThisFrame)
            CycleSuggestionsOrHistory(-1);
        else if (kb.downArrowKey.wasPressedThisFrame)
            CycleSuggestionsOrHistory(+1);

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            Submit();
    }

    private void SetOpen(bool open)
    {
        _isOpen = open;
        _root.SetActive(open);

        if (open)
        {
            _historyIndex = -1;
            _swallowNextInputChange = true;
            _input.text = string.Empty;
            EventSystem.current?.SetSelectedGameObject(_input.gameObject);
            _input.ActivateInputField();
            RebuildSuggestions(string.Empty);
        }
        else
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }
    }

    private void Submit()
    {
        var line = _input.text;
        _input.text = string.Empty;
        EventSystem.current?.SetSelectedGameObject(_input.gameObject);
        _input.ActivateInputField();

        if (string.IsNullOrWhiteSpace(line))
        {
            RebuildSuggestions(string.Empty);
            return;
        }

        AppendLog($"<color=#9cdcfe>> {line}</color>");
        AddHistory(line);

        var result = ConsoleCommandInvoker.Execute(line);
        if (!string.IsNullOrEmpty(result.Output))
        {
            var color = result.Success ? "#dcdcdc" : "#ff8b8b";
            AppendLog($"<color={color}>{result.Output}</color>");
        }

        RebuildSuggestions(string.Empty);
    }

    private void OnInputChanged(string value)
    {
        // The ² toggle keystroke is consumed by the OS as a dead key on AZERTY, but
        // any echo right after opening would land in the input field — drop it.
        if (_swallowNextInputChange)
        {
            _swallowNextInputChange = false;
            if (!string.IsNullOrEmpty(value))
            {
                _input.text = string.Empty;
                _input.caretPosition = 0;
                return;
            }
        }
        var prefix = ExtractCommandPrefix(value);
        RebuildSuggestions(prefix);
    }

    private static string ExtractCommandPrefix(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        int space = value.IndexOf(' ');
        return space < 0 ? value : value.Substring(0, space);
    }

    private void RebuildSuggestions(string prefix)
    {
        _currentSuggestions = ConsoleCommandRegistry.FindMatches(prefix);
        _suggestionIndex = _currentSuggestions.Count > 0 ? 0 : -1;
        RefreshSuggestionUI();
    }

    private void RefreshSuggestionUI()
    {
        bool show = _currentSuggestions.Count > 0;
        _suggestionsPanel.SetActive(show);
        if (!show) return;

        int displayCount = Mathf.Min(_currentSuggestions.Count, MaxSuggestions);
        for (int i = 0; i < _suggestionItems.Count; i++)
        {
            if (i < displayCount)
            {
                var entry = _currentSuggestions[i];
                _suggestionItems[i].gameObject.SetActive(true);
                bool selected = i == _suggestionIndex;
                string nameColor = selected ? "#ffd866" : "#dcdcdc";
                string descColor = selected ? "#cccccc" : "#7a7a7a";
                string desc = string.IsNullOrEmpty(entry.Description) ? "" : $"  <color={descColor}>— {entry.Description}</color>";
                _suggestionItems[i].text = $"<color={nameColor}>{entry.Usage}</color>{desc}";
            }
            else
            {
                _suggestionItems[i].gameObject.SetActive(false);
            }
        }
    }

    private void CycleSuggestionsOrHistory(int direction)
    {
        if (_currentSuggestions.Count > 0 && _input.text.IndexOf(' ') < 0)
        {
            _suggestionIndex = Mod(_suggestionIndex + direction, _currentSuggestions.Count);
            RefreshSuggestionUI();
            return;
        }

        if (_history.Count == 0) return;

        if (_historyIndex == -1)
            _historyIndex = direction < 0 ? _history.Count - 1 : 0;
        else
            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _history.Count - 1);

        _input.text = _history[_historyIndex];
        _input.caretPosition = _input.text.Length;
    }

    private void ApplyAutocomplete()
    {
        if (_currentSuggestions.Count == 0 || _suggestionIndex < 0) return;
        var entry = _currentSuggestions[_suggestionIndex];

        // Replace only the command token, preserving any args the user already typed.
        int space = _input.text.IndexOf(' ');
        string rest = space < 0 ? string.Empty : _input.text.Substring(space);
        bool needsSpace = entry.Parameters.Length > 0 && string.IsNullOrEmpty(rest);
        _input.text = entry.Name + rest + (needsSpace ? " " : string.Empty);
        _input.caretPosition = _input.text.Length;
    }

    private void AddHistory(string line)
    {
        if (_history.Count > 0 && _history[_history.Count - 1] == line) return;
        _history.Add(line);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
        _historyIndex = -1;
    }

    private void AppendLog(string line)
    {
        _logLines.Enqueue(line);
        while (_logLines.Count > MaxLogLines) _logLines.Dequeue();

        var sb = new StringBuilder();
        foreach (var l in _logLines) sb.AppendLine(l);
        _output.text = sb.ToString();

        Canvas.ForceUpdateCanvases();
        if (_outputScroll != null) _outputScroll.verticalNormalizedPosition = 0f;
    }

    public static void Print(string line)
    {
        if (_instance == null) return;
        _instance.AppendLog(line);
    }

    public static void Clear()
    {
        if (_instance == null) return;
        _instance._logLines.Clear();
        _instance._output.text = string.Empty;
    }

    private static int Mod(int x, int m) => ((x % m) + m) % m;

    // ---------------- UI construction ----------------

    private void BuildUI()
    {
        _root = new GameObject("Root", typeof(RectTransform));
        _root.transform.SetParent(transform, false);

        var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(_root.transform, false);
        _canvas = canvasGO.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem();

        // Background panel covers the top half of the screen
        var panel = CreatePanel("Panel", canvasGO.transform, new Color(0f, 0f, 0f, 0.82f));
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Output scroll view
        var scrollGO = new GameObject("Output", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(panel.transform, false);
        var scrollRect = scrollGO.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(8f, 56f);
        scrollRect.offsetMax = new Vector2(-8f, -8f);
        scrollGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        _outputScroll = scrollGO.GetComponent<ScrollRect>();
        _outputScroll.horizontal = false;
        _outputScroll.vertical = true;

        var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRect = viewportGO.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewportGO.GetComponent<Mask>().showMaskGraphic = false;

        var contentGO = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        var fitter = contentGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var layout = contentGO.GetComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 4, 4);

        var outputTextGO = new GameObject("Text", typeof(RectTransform));
        outputTextGO.transform.SetParent(contentGO.transform, false);
        _output = outputTextGO.AddComponent<TextMeshProUGUI>();
        _output.fontSize = 16;
        _output.color = Color.white;
        _output.richText = true;
        _output.alignment = TextAlignmentOptions.TopLeft;
        _output.text = "Console ready. Type 'help' to list commands. Press ² to toggle.";
        var outputTextLE = outputTextGO.AddComponent<LayoutElement>();
        outputTextLE.flexibleHeight = 1;

        _outputScroll.viewport = viewportRect;
        _outputScroll.content = contentRect;

        // Input field
        var inputGO = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputGO.transform.SetParent(panel.transform, false);
        var inputRect = inputGO.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0f);
        inputRect.anchorMax = new Vector2(1f, 0f);
        inputRect.pivot = new Vector2(0.5f, 0f);
        inputRect.offsetMin = new Vector2(8f, 8f);
        inputRect.offsetMax = new Vector2(-8f, 44f);
        inputGO.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.95f);

        var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGO.transform, false);
        var textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 6f);
        textAreaRect.offsetMax = new Vector2(-10f, -6f);

        var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGO.transform.SetParent(textArea.transform, false);
        var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Type a command... (Tab to autocomplete, ↑/↓ history)";
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.fontSize = 18;
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        var placeholderRect = placeholder.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        var inputTextGO = new GameObject("Text", typeof(RectTransform));
        inputTextGO.transform.SetParent(textArea.transform, false);
        var inputText = inputTextGO.AddComponent<TextMeshProUGUI>();
        inputText.color = Color.white;
        inputText.fontSize = 18;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        var inputTextRect = inputText.GetComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = Vector2.zero;
        inputTextRect.offsetMax = Vector2.zero;

        _input = inputGO.GetComponent<TMP_InputField>();
        _input.textViewport = textAreaRect;
        _input.textComponent = inputText;
        _input.placeholder = placeholder;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.restoreOriginalTextOnEscape = false;
        _input.onValueChanged.AddListener(OnInputChanged);

        // Suggestions panel: floats above the input field
        _suggestionsPanel = CreatePanel("Suggestions", panel.transform, new Color(0.08f, 0.08f, 0.08f, 0.95f));
        var sugRect = _suggestionsPanel.GetComponent<RectTransform>();
        sugRect.anchorMin = new Vector2(0f, 0f);
        sugRect.anchorMax = new Vector2(1f, 0f);
        sugRect.pivot = new Vector2(0.5f, 0f);
        sugRect.offsetMin = new Vector2(8f, 52f);
        sugRect.offsetMax = new Vector2(-8f, 52f + (MaxSuggestions * 22f) + 8f);

        var sugLayout = _suggestionsPanel.AddComponent<VerticalLayoutGroup>();
        sugLayout.childAlignment = TextAnchor.LowerLeft;
        sugLayout.childForceExpandWidth = true;
        sugLayout.childForceExpandHeight = false;
        sugLayout.padding = new RectOffset(8, 8, 4, 4);

        for (int i = 0; i < MaxSuggestions; i++)
        {
            var itemGO = new GameObject($"Item{i}", typeof(RectTransform));
            itemGO.transform.SetParent(_suggestionsPanel.transform, false);
            var itemText = itemGO.AddComponent<TextMeshProUGUI>();
            itemText.fontSize = 16;
            itemText.alignment = TextAlignmentOptions.MidlineLeft;
            var itemLE = itemGO.AddComponent<LayoutElement>();
            itemLE.preferredHeight = 20f;
            _suggestionItems.Add(itemText);
        }
    }

    private static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return go;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }
}
