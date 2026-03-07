using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.Settings;
using DotCraft.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UObject = UnityEngine.Object;

namespace DotCraft.Editor.Window
{
    /// <summary>
    /// Main DotCraft Editor Window using UIElements.
    /// </summary>
    public sealed class DotCraftEditorWindow : EditorWindow
    {
        private const string WindowTitle = "DotCraft";

        // UI Elements
        private VisualElement _root;
        private VisualElement _statusIndicator;
        private Label _statusLabel;
        private Button _connectButton;
        private VisualElement _chatContainer;
        private ScrollView _messageList;
        private VisualElement _attachmentsContainer;
        private TextField _inputField;
        private Button _sendButton;
        private Button _stopButton;
        private PopupField<string> _modePopup;
        private PopupField<string> _modelPopup;

        // New UI elements for AI Assistant-style upgrade
        private VisualElement _welcomePanel;
        private VisualElement _typingIndicator;
        private Button _settingsButton;
        private Button _sessionButton;
        private VisualElement _modeSelector;

        // State
        private AcpClient _client;
        private ChatPanel _chatPanel;
        private List<AcpSessionUpdate> _messages = new();
        private readonly List<UObject> _attachedAssets = new();
        private readonly List<string> _availableModes = new() { "(none)" };
        private int _selectedModeIndex;
        private readonly List<string> _availableModels = new() { "(none)" };
        private int _selectedModelIndex;

        // Authentication state
        private VisualElement _authPanel;
        private AuthMethod[] _pendingAuthMethods;
        private Action<AuthMethod> _authResponseHandler;

        // Slash commands state (kept for future autocomplete support)
        private List<AcpSlashCommand> _availableCommands = new();

        // Workspace validation banner
        private VisualElement _workspaceBanner;
        private Label _workspaceBannerLabel;

        // Session management state
        private VisualElement _sessionPopupLayer;
        private VisualElement _sessionPopupPanel;
        private ScrollView _sessionListContainer;
        private Button _newSessionButton;
        private List<SessionListEntry> _sessionList = new();

        // Coalesced scroll: set to true on any session update; the update loop scrolls once per frame
        private bool _scrollDirty;

        [MenuItem("Tools/DotCraft Assistant")]
        public static void ShowWindow()
        {
            var window = GetWindow<DotCraftEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        public void CreateGUI()
        {
            _root = rootVisualElement;

            // Load UXML and styles
            var uxml = DotCraftResources.LoadUxml("DotCraftWindow");
            if (uxml != null)
            {
                uxml.CloneTree(_root);
            }
            else
            {
                CreateFallbackUI();
            }

            LoadStyles();
            BindUIElements();
            SetupEventHandlers();
            InitializeClient();
            CreateWorkspaceBanner();
            RefreshWorkspaceBanner();

            // Single per-frame scroll handler; cheaper than scheduling a delayCall per chunk
            EditorApplication.update += ProcessScrollDirty;

            // Register for session restore
            DomainReloadHandler.OnRestoreSession += HandleSessionRestore;

            // Check for pending session restore
            if (DotCraftSettings.Instance.AutoReconnect && DomainReloadHandler.WasConnected())
            {
                var sessionId = DomainReloadHandler.GetSavedSessionId();
                if (!string.IsNullOrEmpty(sessionId))
                {
                    ConnectAsync(true, sessionId).Forget();
                }
            }
        }

        private void HandleSessionRestore(string sessionId)
        {
            ConnectAsync(true, sessionId).Forget();
        }

        private void LoadStyles()
        {
            var styles = DotCraftResources.LoadAllStyleSheets();
            foreach (var style in styles)
            {
                if (style != null)
                {
                    _root.styleSheets.Add(style);
                }
            }
        }

        private void BindUIElements()
        {
            _statusIndicator = _root.Q<VisualElement>("status-indicator");
            _statusLabel = _root.Q<Label>("status-label");
            _connectButton = _root.Q<Button>("connect-button");
            _chatContainer = _root.Q<VisualElement>("chat-container");
            _messageList = _root.Q<ScrollView>("message-list");
            _attachmentsContainer = _root.Q<VisualElement>("attachments-container");
            _inputField = _root.Q<TextField>("input-field");
            _sendButton = _root.Q<Button>("send-button");
            _stopButton = _root.Q<Button>("stop-button");

            if (_inputField != null)
            {
                // Enable word-wrap and ensure sufficient padding so tall CJK characters are not clipped
                var textInput = _inputField.Q(className: "unity-base-text-field__input");
                if (textInput != null)
                {
                    textInput.style.whiteSpace = WhiteSpace.Normal;
                    textInput.style.paddingTop = 5;
                    textInput.style.paddingBottom = 5;
                    textInput.style.paddingLeft = 4;
                    textInput.style.paddingRight = 4;
                }

                // Resize once the layout is resolved for the first time
                _inputField.RegisterCallback<GeometryChangedEvent>(_ => ResizeInputField());
            }

            // Create PopupFields dynamically since they cannot be used in UXML
            // Initialize with placeholder items to avoid ArgumentOutOfRangeException on empty lists
            var configRow = _root.Q<VisualElement>("config-row");
            if (configRow != null)
            {
                _modePopup = new PopupField<string>("Mode", _availableModes, 0);
                _modePopup.name = "mode-popup";
                _modePopup.AddToClassList("config-popup");
                _modePopup.RegisterValueChangedCallback(OnModeChanged);
                configRow.Add(_modePopup);

                _modelPopup = new PopupField<string>("Model", _availableModels, 0);
                _modelPopup.name = "model-popup";
                _modelPopup.AddToClassList("config-popup");
                _modelPopup.style.display = DisplayStyle.None;
                _modelPopup.RegisterValueChangedCallback(OnModelChanged);
                configRow.Add(_modelPopup);
            }

            // Bind new UI elements (welcome panel, typing indicator, settings button, mode selector)
            _welcomePanel = _root.Q<VisualElement>("welcome-panel");
            _typingIndicator = _root.Q<VisualElement>("typing-indicator");
            _settingsButton = _root.Q<Button>("settings-button");
            _sessionButton = _root.Q<Button>("session-button");
            _newSessionButton = _root.Q<Button>("new-session-button");
            _modeSelector = _root.Q<VisualElement>("mode-selector");

            // Initialize chat panel
            if (_messageList != null)
            {
                _chatPanel = new ChatPanel(_messageList);

                // Wire welcome panel and typing indicator to ChatPanel
                if (_welcomePanel != null)
                    _chatPanel.SetWelcomePanel(_welcomePanel);
                if (_typingIndicator != null)
                    _chatPanel.SetTypingIndicator(_typingIndicator);
            }

            InitializeSessionPopup();
            UpdateSessionManagementUI();
        }

        private void SetupEventHandlers()
        {
            if (_connectButton != null)
            {
                _connectButton.clicked += () => ConnectAsync().Forget();
            }

            if (_sendButton != null)
            {
                _sendButton.clicked += SendPromptAsync;
            }

            if (_stopButton != null)
            {
                _stopButton.clicked += () => _client?.Cancel();
            }

            // Settings gear button → opens DotCraft project settings
            if (_settingsButton != null)
            {
                _settingsButton.clicked += () => SettingsService.OpenProjectSettings("Project/DotCraft");
            }

            if (_sessionButton != null)
            {
                _sessionButton.clicked += ToggleSessionPopup;
            }

            if (_newSessionButton != null)
            {
                _newSessionButton.clicked += OnNewSession;
            }

            // Welcome panel suggested prompt buttons
            // "Explain this scene" uses a dedicated handler that gathers scene context
            var explainBtn = _root.Q<Button>("suggest-explain");
            if (explainBtn != null)
                explainBtn.clicked += OnExplainScenePrompt;

            BindSuggestedPrompt("suggest-fix", "Fix console errors");
            BindSuggestedPrompt("suggest-optimize", "Optimize performance");
            BindSuggestedPrompt("suggest-script", "Help me with a script");

            // Drag and drop for assets
            _root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _root.RegisterCallback<DragPerformEvent>(OnDragPerform);

            // Input field keyboard shortcut and auto-resize
            if (_inputField != null)
            {
                _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
                _inputField.RegisterValueChangedCallback(_ => ResizeInputField());
            }

            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && IsSessionPopupVisible())
            {
                HideSessionPopup();
                evt.StopPropagation();
            }
        }

        private void ToggleSessionPopup()
        {
            if (IsSessionPopupVisible())
            {
                HideSessionPopup();
            }
            else
            {
                ShowSessionPopup();
            }
        }

        private void InitializeSessionPopup()
        {
            _sessionPopupLayer?.RemoveFromHierarchy();

            _sessionPopupLayer = new VisualElement
            {
                name = "session-popup-layer",
                pickingMode = PickingMode.Position
            };
            _sessionPopupLayer.AddToClassList("session-popup-layer");
            _sessionPopupLayer.style.display = DisplayStyle.None;
            _sessionPopupLayer.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.target == _sessionPopupLayer)
                {
                    HideSessionPopup();
                    evt.StopPropagation();
                }
            });

            _sessionPopupPanel = new VisualElement
            {
                name = "session-popup-panel",
                pickingMode = PickingMode.Position
            };
            _sessionPopupPanel.AddToClassList("session-popup-panel");
            _sessionPopupPanel.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            var sessionPopupHeader = new VisualElement();
            sessionPopupHeader.AddToClassList("session-popup-header");

            var sessionPopupTitle = new Label("Sessions");
            sessionPopupTitle.AddToClassList("session-popup-title");
            sessionPopupHeader.Add(sessionPopupTitle);

            _sessionListContainer = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "session-list-container",
                pickingMode = PickingMode.Position
            };
            _sessionListContainer.AddToClassList("session-list-container");

            _sessionPopupPanel.Add(sessionPopupHeader);
            _sessionPopupPanel.Add(_sessionListContainer);
            _sessionPopupLayer.Add(_sessionPopupPanel);
            _root.Add(_sessionPopupLayer);
        }

        private void ShowSessionPopup()
        {
            if (_sessionPopupLayer == null)
                return;

            RefreshSessionList();
            _sessionPopupLayer.BringToFront();
            _sessionPopupPanel?.BringToFront();
            _sessionPopupLayer.style.display = DisplayStyle.Flex;
            _sessionButton?.AddToClassList("selected");
        }

        private void HideSessionPopup()
        {
            if (_sessionPopupLayer == null)
                return;

            _sessionPopupLayer.style.display = DisplayStyle.None;
            _sessionButton?.RemoveFromClassList("selected");
        }

        private bool IsSessionPopupVisible()
        {
            return _sessionPopupLayer != null && _sessionPopupLayer.resolvedStyle.display != DisplayStyle.None;
        }

        /// <summary>
        /// Binds a welcome-panel suggestion button to auto-fill and send a prompt.
        /// </summary>
        private void BindSuggestedPrompt(string buttonName, string promptText)
        {
            var btn = _root.Q<Button>(buttonName);
            if (btn != null)
            {
                btn.clicked += () => OnSuggestedPrompt(promptText);
            }
        }

        /// <summary>
        /// Handles clicking a suggested prompt from the welcome panel.
        /// </summary>
        private void OnSuggestedPrompt(string promptText)
        {
            if (_inputField != null)
                _inputField.value = promptText;
            SendPromptAsync();
        }

        /// <summary>
        /// Handles the "Explain this scene" suggested prompt by gathering the active
        /// scene hierarchy and attaching it as context so the agent can answer meaningfully.
        /// </summary>
        private void OnExplainScenePrompt()
        {
            var enrichedPrompt = BuildSceneContextPrompt();
            if (_inputField != null)
                _inputField.value = enrichedPrompt;
            SendPromptAsync();
        }

        /// <summary>
        /// Builds a prompt string that includes the active scene name, path, and a summary
        /// of root GameObjects with their components. This gives the agent enough context
        /// to explain the scene without requiring a separate tool call round-trip.
        /// </summary>
        private static string BuildSceneContextPrompt()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Explain this scene: {scene.name}");
            sb.AppendLine($"Scene path: {scene.path}");
            return sb.ToString();
        }

        private void CreateWorkspaceBanner()
        {
            _workspaceBanner = new VisualElement();
            _workspaceBanner.name = "workspace-banner";
            _workspaceBanner.AddToClassList("workspace-banner");

            _workspaceBannerLabel = new Label();
            _workspaceBannerLabel.AddToClassList("workspace-banner-label");
            _workspaceBannerLabel.style.whiteSpace = WhiteSpace.Normal;
            _workspaceBanner.Add(_workspaceBannerLabel);

            var bannerButtons = new VisualElement();
            bannerButtons.AddToClassList("workspace-banner-buttons");
            bannerButtons.style.flexDirection = FlexDirection.Row;
            bannerButtons.style.marginTop = 6;

            var openSettingsBtn = new Button(() => SettingsService.OpenProjectSettings("Project/DotCraft"))
            {
                text = "Open Project Settings"
            };
            openSettingsBtn.AddToClassList("workspace-banner-action");
            bannerButtons.Add(openSettingsBtn);

            var retryBtn = new Button(RefreshWorkspaceBanner) { text = "Retry" };
            retryBtn.AddToClassList("workspace-banner-action");
            bannerButtons.Add(retryBtn);

            _workspaceBanner.Add(bannerButtons);

            // Insert at top of the root so it is always visible regardless of window state
            _root.Insert(0, _workspaceBanner);
        }

        private void RefreshWorkspaceBanner()
        {
            if (_workspaceBanner == null) return;

            if (DotCraftSettings.Instance.ValidateWorkspace(out var errorMessage))
            {
                _workspaceBanner.style.display = DisplayStyle.None;
            }
            else
            {
                _workspaceBannerLabel.text = errorMessage;
                _workspaceBanner.style.display = DisplayStyle.Flex;
            }
        }

        private void InitializeClient()
        {
            _client = new AcpClient();
            _client.OnSessionUpdate += HandleSessionUpdate;
            _client.OnPermissionRequest += HandlePermissionRequest;
            _client.OnConnectionStateChanged += HandleConnectionStateChanged;
            _client.OnError += HandleError;
            _client.OnProcessExited += HandleProcessExited;
            _client.OnAuthenticationRequired += HandleAuthenticationRequired;
            _client.OnAvailableCommandsUpdate += HandleAvailableCommandsUpdate;
            _client.OnConfigOptionsUpdate += HandleConfigOptionsUpdate;
        }

        private async Task ConnectAsync(bool reconnect = false, string sessionId = null)
        {
            RefreshWorkspaceBanner();

            if (!DotCraftSettings.Instance.ValidateWorkspace(out var workspaceError))
            {
                Debug.LogError($"[DotCraft] Cannot connect: {workspaceError}");
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
                return;
            }

            UpdateConnectionStatus(ConnectionStatus.Connecting);

            bool success;
            if (reconnect && !string.IsNullOrEmpty(sessionId))
            {
                success = await _client.ReconnectAsync(sessionId);
            }
            else
            {
                success = await _client.ConnectAsync();
            }

            if (success)
            {
                UpdateConnectionStatus(ConnectionStatus.Connected);
                DomainReloadHandler.SaveSessionState(_client.SessionId);
                PopulateModelAndModeSelectors();
                RefreshSessionList();

                // After reconnect, history notifications are still being drained from the
                // MainThreadDispatcher queue (processed via EditorApplication.update).
                // Defer FinalizeStreaming to the next editor tick so it runs after all
                // history chunks have been applied and any last message gets markdown-rendered.
                if (reconnect)
                {
                    EditorApplication.delayCall += () => _chatPanel?.FinalizeStreaming();
                }
            }
            else
            {
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
            }
        }

        private async void SendPromptAsync()
        {
            var text = _inputField?.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (!_client?.IsConnected ?? true) return;

            var prompt = new List<AcpContentBlock>
            {
                new() { Type = "text", Text = text }
            };

            // Add attached assets
            foreach (var asset in _attachedAssets)
            {
                var resourceBlock = ContextAttachment.CreateResourceBlock(asset);
                if (resourceBlock != null)
                {
                    prompt.Add(resourceBlock);
                }
            }

            // Hide welcome panel and show typing indicator
            _chatPanel?.ShowTypingIndicator(true);

            // Add user message to chat
            _chatPanel?.AddUserMessage(text, _attachedAssets);
            _attachedAssets.Clear();
            UpdateAttachmentsUI();
            _scrollDirty = true;

            _inputField.value = "";
            UpdateRunningState(true);

            try
            {
                var success = await _client.PromptAsync(prompt);
            }
            finally
            {
                // Finalize any in-progress streaming message so it gets full markdown rendering
                _chatPanel?.FinalizeStreaming();

                UpdateRunningState(false);
            }
        }

        private void HandleSessionUpdate(AcpSessionUpdate update)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                // Refresh popups when config options change
                if (update.SessionUpdate == AcpUpdateKind.ConfigOptionsUpdate)
                {
                    PopulateConfigPopups(_client?.ConfigOptions);
                }
                else if (update.SessionUpdate == AcpUpdateKind.CurrentModeUpdate)
                {
                    HandleModeUpdate(update);
                }

                _chatPanel?.HandleSessionUpdate(update);

                // Mark scroll dirty; ProcessScrollDirty will scroll once per editor frame
                // instead of scheduling a separate delayCall for every chunk.
                _scrollDirty = true;
            });
        }

        private void ProcessScrollDirty()
        {
            if (!_scrollDirty) return;
            _scrollDirty = false;

            var child = _messageList?.Children().LastOrDefault();
            if (child != null)
            {
                _messageList.ScrollTo(child);
            }
        }

        private void HandleModeUpdate(AcpSessionUpdate update)
        {
            string modeText = null;
            if (update.Content is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("text", out var textProp))
                {
                    modeText = textProp.GetString();
                }
                else if (json.ValueKind == JsonValueKind.String)
                {
                    modeText = json.GetString();
                }
            }
            else if (update.Content is string text)
            {
                modeText = text;
            }

            if (string.IsNullOrEmpty(modeText)) return;

            // Update the mode config option's current value and refresh the popup
            var modeOption = _client?.ConfigOptions?.Find(o => o.Id == "mode");
            if (modeOption != null)
            {
                modeOption.CurrentValue = modeText;

                if (_modePopup != null && modeOption.Options != null)
                {
                    var idx = modeOption.Options.FindIndex(o => o.Value == modeText);
                    if (idx >= 0)
                    {
                        _modePopup.UnregisterValueChangedCallback(OnModeChanged);
                        _modePopup.index = idx;
                        _modePopup.RegisterValueChangedCallback(OnModeChanged);
                    }
                }
            }
        }

        private void HandlePermissionRequest(RequestPermissionParams request, Action<RequestPermissionResult> responseHandler)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                // Use inline approval card in the chat flow
                _chatPanel?.HandleApprovalRequest(request, responseHandler);
                _scrollDirty = true;

                // Bring window to front
                EditorApplication.delayCall += Focus;
            });
        }

        private void HandleConnectionStateChanged(bool connected)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                UpdateConnectionStatus(connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected);
            });
        }

        private static void HandleError(string error)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                Debug.LogError($"[DotCraft] {error}");
            });
        }

        private void HandleProcessExited()
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                UpdateConnectionStatus(ConnectionStatus.Disconnected);
            });
        }

        private void HandleAuthenticationRequired(AuthMethod[] methods, Action<AuthMethod> callback)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                _pendingAuthMethods = methods;
                _authResponseHandler = callback;
                ShowAuthPanel();
            });
        }

        private void HandleAvailableCommandsUpdate(List<AcpSlashCommand> commands)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                _availableCommands = commands ?? new List<AcpSlashCommand>();
            });
        }

        private void HandleConfigOptionsUpdate(List<ConfigOption> configOptions)
        {
            MainThreadDispatcher.RunOrEnqueue(() =>
            {
                PopulateConfigPopups(configOptions);
            });
        }

        private void ShowAuthPanel()
        {
            if (_authPanel == null)
            {
                _authPanel = UIHelper.CreateElement("auth-panel");
                _root.Add(_authPanel);
            }

            _authPanel.Clear();
            _authPanel.style.display = DisplayStyle.Flex;
            _authPanel.Add(UIHelper.CreateLabel("Authentication Required", "auth-title"));

            foreach (var method in _pendingAuthMethods)
            {
                var methodElement = UIHelper.CreateElement("auth-method");
                methodElement.Add(UIHelper.CreateLabel(method.Name, "auth-method-name"));

                if (!string.IsNullOrEmpty(method.Description))
                {
                    methodElement.Add(UIHelper.CreateLabel(method.Description, "auth-method-description"));
                }

                var selectBtn = UIHelper.CreateButton("Select", "auth-select-button");
                var capturedMethod = method;
                selectBtn.clicked += () =>
                {
                    _authResponseHandler?.Invoke(capturedMethod);
                    _pendingAuthMethods = null;
                    _authResponseHandler = null;
                    _authPanel.style.display = DisplayStyle.None;
                };
                methodElement.Add(selectBtn);
                _authPanel.Add(methodElement);
            }
        }

        private void PopulateModelAndModeSelectors()
        {
            PopulateConfigPopups(_client?.ConfigOptions);
        }

        private void PopulateConfigPopups(List<ConfigOption> configOptions)
        {
            if (configOptions == null) return;

            foreach (var option in configOptions)
            {
                if (option.Type != "select" || option.Options == null) continue;

                PopupField<string> popup;
                EventCallback<ChangeEvent<string>> callback;

                if (option.Id == "mode")
                {
                    popup = _modePopup;
                    callback = OnModeChanged;
                }
                else if (option.Id == "model")
                {
                    popup = _modelPopup;
                    callback = OnModelChanged;
                }
                else continue;

                if (popup == null) continue;

                // Unregister before changing choices/index to avoid triggering
                // spurious session/set_config_option requests during the update.
                popup.UnregisterValueChangedCallback(callback);

                var newChoices = new List<string>();
                foreach (var opt in option.Options)
                    newChoices.Add(opt.Name ?? opt.Value);

                // Assigning to .choices triggers an internal UI refresh,
                // whereas mutating the backing list in-place does not.
                popup.choices = newChoices;

                var currentIdx = option.Options.FindIndex(o => o.Value == option.CurrentValue);
                popup.index = currentIdx >= 0 ? currentIdx : 0;

                popup.RegisterValueChangedCallback(callback);
            }

            UpdateConfigPopupVisibility(configOptions);
        }

        private void UpdateConfigPopupVisibility(List<ConfigOption> configOptions)
        {
            if (_modelPopup == null) return;

            var hasModel = configOptions?.Exists(o => o.Id == "model") ?? false;
            _modelPopup.style.display = hasModel ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnModelChanged(ChangeEvent<string> evt)
        {
            HandleConfigOptionChange("model", _modelPopup);
        }

        private void OnModeChanged(ChangeEvent<string> evt)
        {
            HandleConfigOptionChange("mode", _modePopup);
        }

        private void HandleConfigOptionChange(string configId, PopupField<string> popup)
        {
            if (_client?.IsConnected != true || popup == null) return;

            var option = _client.ConfigOptions?.Find(o => o.Id == configId);
            if (option?.Options == null) return;

            var idx = popup.index;
            if (idx >= 0 && idx < option.Options.Count)
            {
                var value = option.Options[idx].Value;
                _client.SetConfigOptionAsync(configId, value).Forget();
            }
        }

        private void UpdateConnectionStatus(ConnectionStatus status)
        {
            if (_statusIndicator == null) return;

            _statusIndicator.RemoveFromClassList("connected");
            _statusIndicator.RemoveFromClassList("connecting");
            _statusIndicator.RemoveFromClassList("disconnected");

            switch (status)
            {
                case ConnectionStatus.Connected:
                    _statusIndicator.AddToClassList("connected");
                    _statusLabel.text = $"Connected ({_client?.AgentInfo?.Name ?? "DotCraft"})";
                    _connectButton.text = "Disconnect";
                    _connectButton.clicked -= HandleConnectClick;
                    _connectButton.clicked += HandleDisconnectClick;
                    break;
                case ConnectionStatus.Connecting:
                    _statusIndicator.AddToClassList("connecting");
                    _statusLabel.text = "Connecting...";
                    _connectButton.SetEnabled(false);
                    break;
                case ConnectionStatus.Disconnected:
                    _statusIndicator.AddToClassList("disconnected");
                    _statusLabel.text = "Disconnected";
                    _connectButton.text = "Connect";
                    _connectButton.SetEnabled(true);
                    _connectButton.clicked -= HandleDisconnectClick;
                    _connectButton.clicked += HandleConnectClick;
                    _sessionList.Clear();
                    HideSessionPopup();
                    break;
            }

            UpdateSessionManagementUI();
        }

        private void HandleConnectClick() => ConnectAsync().Forget();

        private void HandleDisconnectClick() => _client?.DisconnectAsync().Forget();

        private void ResizeInputField()
        {
            if (_inputField == null) return;

            var text = _inputField.value ?? string.Empty;
            float fieldWidth = _inputField.resolvedStyle.width;

            // Estimate characters per line based on field width (~7.5px per character at default font size)
            int charsPerLine = fieldWidth > 0 ? Mathf.Max(1, Mathf.FloorToInt(fieldWidth / 7.5f)) : 60;

            int totalLines = 0;
            foreach (var line in text.Split('\n'))
                totalLines += Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(1, line.Length) / charsPerLine));

            totalLines = Mathf.Max(1, totalLines);

            // 18px per line accommodates CJK characters; 20px accounts for top+bottom padding (5px each + 10px buffer)
            const float lineHeight = 18f;
            const float verticalPadding = 20f;
            const float minHeight = 48f;

            _inputField.style.height = Mathf.Max(minHeight, totalLines * lineHeight + verticalPadding);
        }

        private void UpdateRunningState(bool running)
        {
            if (_sendButton != null) _sendButton.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            if (_stopButton != null) _stopButton.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateAttachmentsUI()
        {
            if (_attachmentsContainer == null) return;

            _attachmentsContainer.Clear();

            foreach (var asset in _attachedAssets)
            {
                var item = UIHelper.CreateElement("attachment-item");
                item.Add(UIHelper.CreateLabel(asset.name, "attachment-name"));

                var removeBtn = UIHelper.CreateButton("×", "remove-attachment");
                removeBtn.clicked += () =>
                {
                    _attachedAssets.Remove(asset);
                    UpdateAttachmentsUI();
                };
                item.Add(removeBtn);

                _attachmentsContainer.Add(item);
            }
        }

        #region Drag and Drop

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.objectReferences.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();

            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj != null && !_attachedAssets.Contains(obj))
                {
                    _attachedAssets.Add(obj);
                }
            }

            UpdateAttachmentsUI();
        }

        #endregion

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
            {
                evt.StopImmediatePropagation();
                SendPromptAsync();
            }
        }

        private void CreateFallbackUI()
        {
            // Create basic UI if UXML not found
            _root.Clear();

            // Status bar
            var statusBar = UIHelper.CreateElement("connection-status");
            _statusIndicator = UIHelper.CreateElement("status-indicator", "disconnected");
            _statusLabel = UIHelper.CreateLabel("Disconnected", "status-label");
            _connectButton = new Button { text = "Connect" };
            _connectButton.style.marginLeft = Length.Auto();
            statusBar.Add(_statusIndicator);
            statusBar.Add(_statusLabel);
            statusBar.Add(_connectButton);
            _root.Add(statusBar);

            // Chat container
            _chatContainer = UIHelper.CreateElement("chat-container");
            _messageList = UIHelper.CreateScrollView("message-list");
            _chatContainer.Add(_messageList);
            _root.Add(_chatContainer);

            // Input area
            var inputArea = UIHelper.CreateElement("input-area");
            _attachmentsContainer = UIHelper.CreateElement("attachments-container");
            inputArea.Add(_attachmentsContainer);

            _inputField = UIHelper.CreateTextField(true, "input-field");
            _inputField.style.minHeight = 60;
            inputArea.Add(_inputField);

            var btnRow = UIHelper.CreateElement("button-row");
            _sendButton = UIHelper.CreateButton("Send", "send-button");
            _stopButton = UIHelper.CreateButton("Stop", "stop-button");
            _stopButton.style.display = DisplayStyle.None;
            btnRow.Add(_sendButton);
            btnRow.Add(_stopButton);
            inputArea.Add(btnRow);
            _root.Add(inputArea);

            _chatPanel = new ChatPanel(_messageList);
        }

        #region Domain Reload Handling

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            // Save session ID for recovery
            if (_client?.IsConnected ?? false)
            {
                DomainReloadHandler.SaveSessionState(_client.SessionId);
            }

            // Kill and dispose synchronously; Dispose now kills the process
            // first so there is no deadlock with the reader loop.
            _client?.Dispose();
        }

        #region Session Management

        private async void RefreshSessionList()
        {
            if (_client?.IsConnected != true)
            {
                _sessionList.Clear();
                UpdateSessionManagementUI();
                return;
            }

            try
            {
                var sessions = await _client.ListSessionsAsync();
                _sessionList = sessions ?? new List<SessionListEntry>();
                UpdateSessionManagementUI();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotCraft] Failed to refresh session list: {ex.Message}");
                _sessionList = new List<SessionListEntry>();
                UpdateSessionManagementUI();
            }
        }

        private void UpdateSessionManagementUI()
        {
            if (_sessionListContainer == null)
            {
                return;
            }

            _sessionListContainer.Clear();

            var isConnected = _client?.IsConnected == true;
            _sessionButton?.SetEnabled(true);
            _newSessionButton?.SetEnabled(isConnected);

            if (!isConnected)
            {
                _sessionListContainer.Add(CreateSessionInfoLabel("Connect to manage sessions."));
                return;
            }

            var visibleSessions = GetVisibleSessionEntries();
            if (visibleSessions.Count == 0)
            {
                _sessionListContainer.Add(CreateSessionInfoLabel("No saved sessions yet."));
                return;
            }

            foreach (var session in visibleSessions)
            {
                _sessionListContainer.Add(CreateSessionRow(session));
            }
        }

        private List<SessionListEntry> GetVisibleSessionEntries()
        {
            var visibleSessions = _sessionList != null
                ? new List<SessionListEntry>(_sessionList)
                : new List<SessionListEntry>();

            var currentSessionId = _client?.SessionId;
            if (!string.IsNullOrEmpty(currentSessionId) && visibleSessions.All(s => s.SessionId != currentSessionId))
            {
                visibleSessions.Insert(0, new SessionListEntry { SessionId = currentSessionId });
            }

            return visibleSessions;
        }

        private VisualElement CreateSessionRow(SessionListEntry session)
        {
            var row = new VisualElement();
            row.AddToClassList("session-row");

            var isCurrent = session.SessionId == _client?.SessionId;
            if (isCurrent)
            {
                row.AddToClassList("selected");
            }

            var selectButton = new Button(() =>
            {
                if (!isCurrent)
                {
                    HideSessionPopup();
                    SwitchSessionAsync(session.SessionId).Forget();
                }
            })
            {
                text = GetSessionDisplayName(session),
                tooltip = session.SessionId
            };
            selectButton.AddToClassList("session-select-button");
            if (isCurrent)
            {
                selectButton.AddToClassList("selected");
            }

            row.Add(selectButton);

            if (_client?.SupportsSessionDelete == true)
            {
                var deleteButton = new Button(() => DeleteSessionAsync(session).Forget())
                {
                    text = "Delete",
                    tooltip = $"Delete session {session.SessionId}"
                };
                deleteButton.AddToClassList("session-delete-button");
                row.Add(deleteButton);
            }

            return row;
        }

        private Label CreateSessionInfoLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("session-info-label");
            return label;
        }

        private static string GetSessionDisplayName(SessionListEntry session)
        {
            if (!string.IsNullOrEmpty(session?.Title))
            {
                return session.Title;
            }

            if (string.IsNullOrEmpty(session?.SessionId))
            {
                return "(unknown)";
            }

            return session.SessionId.Length > 12
                ? $"...{session.SessionId[^12..]}"
                : session.SessionId;
        }

        private async void OnNewSession()
        {
            if (_client?.IsConnected != true) return;

            try
            {
                HideSessionPopup();

                // Disconnect current session
                await _client.DisconnectAsync();

                // Clear chat
                _chatPanel?.Clear();

                // Create new session
                await ConnectAsync(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to create new session: {ex.Message}");
            }
        }

        private async Task DeleteSessionAsync(SessionListEntry session)
        {
            if (_client?.IsConnected != true || session == null || string.IsNullOrWhiteSpace(session.SessionId))
                return;

            if (_client.SupportsSessionDelete != true)
                return;

            var isCurrentSession = session.SessionId == _client.SessionId;
            var targetName = GetSessionDisplayName(session);
            var prompt = isCurrentSession
                ? $"Delete the current session '{targetName}'?\n\nThis cannot be undone."
                : $"Delete session '{targetName}'?\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog("Delete Session", prompt, "Delete", "Cancel"))
                return;

            HideSessionPopup();

            var nextSessionId = isCurrentSession
                ? GetVisibleSessionEntries()
                    .FirstOrDefault(entry => entry.SessionId != session.SessionId)?
                    .SessionId
                : null;

            var deleted = await _client.DeleteSessionAsync(session.SessionId);
            if (!deleted)
            {
                Debug.LogWarning($"[DotCraft] Failed to delete session: {session.SessionId}");
                return;
            }

            if (!isCurrentSession)
            {
                RefreshSessionList();
                return;
            }

            DomainReloadHandler.ClearSessionState();
            await _client.DisconnectAsync();
            _chatPanel?.Clear();

            if (!string.IsNullOrEmpty(nextSessionId))
            {
                await ConnectAsync(true, nextSessionId);
            }
            else
            {
                await ConnectAsync(false);
            }
        }

        private async Task SwitchSessionAsync(string sessionId)
        {
            try
            {
                // Disconnect current session
                await _client.DisconnectAsync();

                // Clear chat
                _chatPanel?.Clear();

                // Reconnect to the specified session
                await ConnectAsync(true, sessionId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to switch session: {ex.Message}");
            }
        }

        #endregion

        private void OnDestroy()
        {
            EditorApplication.update -= ProcessScrollDirty;
            DomainReloadHandler.OnRestoreSession -= HandleSessionRestore;
            _client?.Dispose();
        }

        #endregion
    }

    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected
    }
}
