using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace InfiniteWorld
{
    /// <summary>
    /// VRBackendUIController - Manages the 3D Holographic UI in the VR Cockpit
    /// connecting the driver to the FastAPI backend.
    /// 
    /// Usage:
    /// - Attach to a GameObject in the scene (or the BackendManager).
    /// - Press "B" key to toggle the dashboard on/off.
    /// </summary>
    public class VRBackendUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VRBackendConnector connector;
        [SerializeField] private StraightLineDriver driver;

        [Header("UI Theme Colors")]
        [SerializeField] private Color NeonCyan = new Color(0f, 0.9f, 0.9f, 1.0f);
        [SerializeField] private Color NeonOrange = new Color(1.0f, 0.4f, 0.0f, 1.0f);
        [SerializeField] private Color NeonGreen = new Color(0.2f, 0.9f, 0.3f, 1.0f);
        [SerializeField] private Color NeonRed = new Color(1.0f, 0.2f, 0.2f, 1.0f);

        // UI Pages/Menus
        private enum Page { Closed, Registration, MainMenu, OnboardingActive, ChatTutor, NotesDisplay }
        private Page currentPage = Page.Closed;

        // Visual containers
        private GameObject _activePanel;
        private List<GameObject> _panelObjects = new List<GameObject>();

        // Onboarding / Quiz state
        private string _assessmentId;
        private string[] _quizQuestions;
        private string[] _quizAnswers;
        private int _currentQuestionIndex = 0;
        private List<string> _studentAnswers = new List<string>();

        // Notes state
        private string _notesContent;
        private string _notesSummary;

        // Chat state
        private string _chatQueryText = "";
        private string _chatResponseText = "Select a query below to ask the AI Tutor...";

        private void Start()
        {
            if (connector == null) connector = FindFirstObjectByType<VRBackendConnector>();
            if (driver == null) driver = FindFirstObjectByType<StraightLineDriver>();

            // Subscribe to backend connector events
            if (connector != null)
            {
                connector.OnStudentCreated += HandleStudentCreated;
                connector.OnOnboardingStarted += HandleOnboardingStarted;
                connector.OnOnboardingSubmitted += HandleOnboardingSubmitted;
                connector.OnQuizGenerated += HandleQuizGenerated;
                connector.OnNotesGenerated += HandleNotesGenerated;
                connector.OnChatResponseReceived += HandleChatResponseReceived;
                connector.OnError += HandleError;
            }

            Debug.Log("[BackendUI] VRBackendUIController initialized. Press 'B' key to open Learning Dashboard.");
        }

        private void Update()
        {
            // Toggle UI on/off with B key using the New Input System
            var kb = Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
            {
                ToggleDashboard();
            }
        }

        private void ToggleDashboard()
        {
            if (currentPage == Page.Closed)
            {
                // Open UI. Check if student is already created
                if (string.IsNullOrEmpty(connector.GetStudentId()))
                {
                    OpenPage(Page.Registration);
                }
                else
                {
                    OpenPage(Page.MainMenu);
                }
            }
            else
            {
                // Close UI
                OpenPage(Page.Closed);
            }
        }

        private void OpenPage(Page page)
        {
            currentPage = page;
            ClearActivePanel();

            if (page == Page.Closed)
            {
                if (driver != null) driver.Paused = false; // Resume driving
                return;
            }

            if (driver != null)
            {
                driver.Paused = true; // Pause vehicle while interacting
                driver.automaticSpeedKmh = 0f;
            }

            // Create panel container parented to camera so it moves/rotates with the driver's head
            _activePanel = new GameObject("VRBackendDashboardPanel");
            _activePanel.transform.SetParent(Camera.main != null ? Camera.main.transform : driver.transform, false);
            
            // Floating 1.25 meters in front of the driver
            _activePanel.transform.localPosition = new Vector3(0f, 0.05f, 1.25f);
            _activePanel.transform.localRotation = Quaternion.identity;
            _activePanel.transform.localScale = Vector3.one * 0.7f; // Fits nicely in VR field of view

            switch (currentPage)
            {
                case Page.Registration:
                    DrawRegistrationPage();
                    break;
                case Page.MainMenu:
                    DrawMainMenuPage();
                    break;
                case Page.OnboardingActive:
                    DrawOnboardingPage();
                    break;
                case Page.ChatTutor:
                    DrawChatPage();
                    break;
                case Page.NotesDisplay:
                    DrawNotesPage();
                    break;
            }
        }

        private void ClearActivePanel()
        {
            foreach (var go in _panelObjects)
            {
                if (go != null) Destroy(go);
            }
            _panelObjects.Clear();

            if (_activePanel != null)
            {
                Destroy(_activePanel);
                _activePanel = null;
            }
        }

        // ================================================================
        // PAGE 1: REGISTRATION PAGE
        // ================================================================
        private void DrawRegistrationPage()
        {
            SpawnTitle("VR LEARNING BACKEND INTERFACE", NeonCyan);
            SpawnTextLine("Establish connection with FastAPI Backend Server", 0.40f);
            SpawnTextLine("Please choose a profile preset to log in:", 0.25f);

            // Preset 1: Student A (Class 10 Physics)
            SpawnButton("Register: Student Alpha (Grade 10)", new Vector3(0f, 0.05f, 0f), 2.2f, 0.35f, () =>
            {
                SpawnTextLine("Registering Student Alpha...", -0.35f, NeonOrange);
                connector.CreateStudent("Student Alpha", "alpha@vr.edu", 10);
            });

            // Preset 2: Student B (Class 12 Advanced Physics)
            SpawnButton("Register: Student Beta (Grade 12)", new Vector3(0f, -0.15f, 0f), 2.2f, 0.35f, () =>
            {
                SpawnTextLine("Registering Student Beta...", -0.35f, NeonOrange);
                connector.CreateStudent("Student Beta", "beta@vr.edu", 12);
            });

            // Exit button
            SpawnButton("Close Interface", new Vector3(0f, -0.50f, 0f), 1.4f, 0.30f, () => ToggleDashboard());
        }

        private void HandleStudentCreated(string response)
        {
            // Successfully created! Transition to main dashboard menu
            OpenPage(Page.MainMenu);
        }

        // ================================================================
        // PAGE 2: MAIN DASHBOARD MENU
        // ================================================================
        private void DrawMainMenuPage()
        {
            SpawnTitle("FASTAPI LEARNING DASHBOARD", NeonGreen);
            SpawnTextLine($"Connected Profile ID: {connector.GetStudentId().Substring(0, Mathf.Min(8, connector.GetStudentId().Length))}...", 0.42f, Color.gray);

            // 1. Onboarding Test Button
            SpawnButton("1. Start AI Onboarding Test", new Vector3(0f, 0.22f, 0f), 2.2f, 0.32f, () =>
            {
                connector.StartOnboarding("PHYSICS", "MOTION");
            });

            // 2. Study Notes Generator
            SpawnButton("2. Fetch Topic Study Notes", new Vector3(0f, 0.05f, 0f), 2.2f, 0.32f, () =>
            {
                connector.GenerateNotes("SPEED AND ACCELERATION", 10);
            });

            // 3. Quiz Generator
            SpawnButton("3. Fetch Speed Mini-Quiz", new Vector3(0f, -0.12f, 0f), 2.2f, 0.32f, () =>
            {
                connector.GenerateQuiz("SPEED AND ACCELERATION", 10);
            });

            // 4. AI Chat Bot Room
            SpawnButton("4. Consult AI Physics Tutor (RAG)", new Vector3(0f, -0.29f, 0f), 2.2f, 0.32f, () =>
            {
                OpenPage(Page.ChatTutor);
            });

            // Logout / Close buttons
            SpawnButton("Reset Session", new Vector3(-0.55f, -0.52f, 0f), 1.0f, 0.28f, () =>
            {
                connector.ResetSession();
                OpenPage(Page.Registration);
            });

            SpawnButton("Resume Drive", new Vector3(0.55f, -0.52f, 0f), 1.0f, 0.28f, () => ToggleDashboard());
        }

        // ================================================================
        // PAGE 3: ONBOARDING / QUIZ ACTIVE MODE
        // ================================================================
        private void DrawOnboardingPage()
        {
            if (_quizQuestions == null || _quizQuestions.Length == 0)
            {
                SpawnTitle("QUIZ ASSIGNMENT", NeonOrange);
                SpawnTextLine("No questions available in this session.", 0.1f);
                SpawnButton("Back to Menu", new Vector3(0f, -0.30f, 0f), 1.5f, 0.35f, () => OpenPage(Page.MainMenu));
                return;
            }

            SpawnTitle($"QUESTION {_currentQuestionIndex + 1} OF {_quizQuestions.Length}", NeonOrange);
            
            // Split question into multiple lines if too long
            string questionText = _quizQuestions[_currentQuestionIndex];
            SpawnTextLine(questionText, 0.25f, Color.white, FontStyle.BoldAndItalic);

            // Spawns 3 interactive options (True/False/Not sure or Multiple choices)
            // Backend outputs questions. Since we are generic, we show standard responses
            SpawnButton("Option A: Correct / Agree", new Vector3(0f, 0.0f, 0f), 2.2f, 0.30f, () => SelectAnswer("A"));
            SpawnButton("Option B: Incorrect / Disagree", new Vector3(0f, -0.15f, 0f), 2.2f, 0.30f, () => SelectAnswer("B"));
            SpawnButton("Option C: I am not sure / Undecided", new Vector3(0f, -0.30f, 0f), 2.2f, 0.30f, () => SelectAnswer("C"));

            SpawnButton("Abort Quiz", new Vector3(0f, -0.55f, 0f), 1.2f, 0.26f, () => OpenPage(Page.MainMenu));
        }

        private void HandleOnboardingStarted(string response)
        {
            var result = JsonUtility.FromJson<OnboardingResponse>(response);
            if (result != null)
            {
                _assessmentId = result.assessment_id;
                _quizQuestions = result.questions;
                _currentQuestionIndex = 0;
                _studentAnswers.Clear();
                OpenPage(Page.OnboardingActive);
            }
        }

        private void HandleQuizGenerated(string response)
        {
            var result = JsonUtility.FromJson<QuizResponse>(response);
            if (result != null)
            {
                _assessmentId = "MINI_QUIZ_SESSION";
                _quizQuestions = result.questions;
                _quizAnswers = result.answers;
                _currentQuestionIndex = 0;
                _studentAnswers.Clear();
                OpenPage(Page.OnboardingActive);
            }
        }

        private void SelectAnswer(string option)
        {
            _studentAnswers.Add(option);
            _currentQuestionIndex++;

            if (_currentQuestionIndex < _quizQuestions.Length)
            {
                // Go to next question
                OpenPage(Page.OnboardingActive);
            }
            else
            {
                // Finished all questions, submit!
                ClearActivePanel();
                _activePanel = new GameObject("VRBackendDashboardPanel");
                _activePanel.transform.SetParent(Camera.main != null ? Camera.main.transform : driver.transform, false);
                _activePanel.transform.localPosition = new Vector3(0f, 0.05f, 1.25f);
                _activePanel.transform.localScale = Vector3.one * 0.7f;

                SpawnTitle("EVALUATING QUIZ RESULTS...", NeonCyan);
                SpawnTextLine("Uploading your responses to the multi-agent grader...", 0.1f);
                
                connector.SubmitOnboardingResponse("GENERAL", "GENERAL", _quizQuestions, _studentAnswers.ToArray());
            }
        }

        private void HandleOnboardingSubmitted(string response)
        {
            // Parse evaluation/grade feedback response
            ClearActivePanel();
            _activePanel = new GameObject("VRBackendDashboardPanel");
            _activePanel.transform.SetParent(Camera.main != null ? Camera.main.transform : driver.transform, false);
            _activePanel.transform.localPosition = new Vector3(0f, 0.05f, 1.25f);
            _activePanel.transform.localScale = Vector3.one * 0.7f;

            SpawnTitle("ASSESSMENT FEEDBACK", NeonGreen);

            var feedback = JsonUtility.FromJson<FeedbackResponse>(response);
            if (feedback != null)
            {
                SpawnTextLine($"Your Graded Score: {feedback.score * 100f:F0}%", 0.25f, NeonGreen, FontStyle.Bold);
                SpawnTextLine(feedback.feedback, 0.05f);
            }
            else
            {
                SpawnTextLine("Submission complete! Grade computed successfully.", 0.25f);
            }

            SpawnButton("Return to Menu", new Vector3(0f, -0.40f, 0f), 1.6f, 0.35f, () => OpenPage(Page.MainMenu));
        }

        // ================================================================
        // PAGE 4: RAG PHYSICS CHAT / AI TUTOR
        // ================================================================
        private void DrawChatPage()
        {
            SpawnTitle("AI PHYSICS TUTOR CONSULTATION", NeonCyan);

            // Multiline scrollable-like text readout
            SpawnTextLine("Response from RAG Database:", 0.40f, Color.gray);
            
            // Format response lines (Split response to fit comfortably in layout)
            string wrappedResponse = WrapText(_chatResponseText, 55);
            SpawnTextLine(wrappedResponse, 0.18f, Color.white, FontStyle.Normal);

            SpawnTextLine("Choose an interactive query to ask the AI Tutor:", -0.15f, NeonCyan);

            // Query Options
            SpawnButton("What is the formula of Speed?", new Vector3(0f, -0.28f, 0f), 2.2f, 0.26f, () =>
            {
                _chatResponseText = "Querying RAG database...";
                OpenPage(Page.ChatTutor);
                connector.SendChatQuery("What is the definition and mathematical formula of Speed?");
            });

            SpawnButton("Explain why the SI unit is m/s", new Vector3(0f, -0.41f, 0f), 2.2f, 0.26f, () =>
            {
                _chatResponseText = "Querying RAG database...";
                OpenPage(Page.ChatTutor);
                connector.SendChatQuery("Explain why the SI Unit of speed is meters per second (m/s).");
            });

            SpawnButton("Back to Dashboard", new Vector3(0f, -0.58f, 0f), 1.5f, 0.26f, () =>
            {
                _chatResponseText = "Select a query below to ask the AI Tutor...";
                OpenPage(Page.MainMenu);
            });
        }

        private void HandleChatResponseReceived(string response)
        {
            var result = JsonUtility.FromJson<ChatResponse>(response);
            if (result != null)
            {
                _chatResponseText = result.answer;
                OpenPage(Page.ChatTutor);
            }
        }

        // ================================================================
        // PAGE 5: STUDY NOTES DISPLAY PAGE
        // ================================================================
        private void DrawNotesPage()
        {
            SpawnTitle("AI GENERATED STUDY NOTES", NeonGreen);

            SpawnTextLine("Generated Summary:", 0.38f, NeonOrange);
            string wrappedSummary = WrapText(_notesSummary, 55);
            SpawnTextLine(wrappedSummary, 0.24f, Color.white, FontStyle.BoldAndItalic);

            SpawnTextLine("Full Concept Explanations:", 0.05f, NeonCyan);
            string wrappedNotes = WrapText(_notesContent, 60);
            SpawnTextLine(wrappedNotes, -0.15f, Color.white, FontStyle.Normal);

            SpawnButton("Return to Menu", new Vector3(0f, -0.55f, 0f), 1.6f, 0.35f, () => OpenPage(Page.MainMenu));
        }

        private void HandleNotesGenerated(string response)
        {
            var result = JsonUtility.FromJson<NotesResponse>(response);
            if (result != null)
            {
                _notesContent = result.notes;
                _notesSummary = result.summary;
                OpenPage(Page.NotesDisplay);
            }
        }

        // ================================================================
        // COMMON CREATION UTILITIES
        // ================================================================

        private void SpawnTitle(string text, Color color)
        {
            var titleGo = new GameObject("TitleText");
            titleGo.transform.SetParent(_activePanel.transform, false);
            titleGo.transform.localPosition = new Vector3(0f, 0.58f, -0.01f);
            titleGo.transform.localScale = Vector3.one * 0.009f;

            var tm = titleGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = Color.white;
                titleGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tm.text = text.ToUpper();
            tm.fontSize = 54;
            tm.fontStyle = FontStyle.BoldAndItalic;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;

            _panelObjects.Add(titleGo);
        }

        private void SpawnTextLine(string text, float yPos, Color? color = null, FontStyle fontStyle = FontStyle.BoldAndItalic)
        {
            var lineGo = new GameObject("LineText_" + yPos);
            lineGo.transform.SetParent(_activePanel.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, yPos, -0.01f);
            lineGo.transform.localScale = Vector3.one * 0.007f;

            var tm = lineGo.AddComponent<TextMesh>();
            Font builtinFont = GetSafeBuiltinFont();
            if (builtinFont != null)
            {
                tm.font = builtinFont;
                var txtMat = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default"));
                txtMat.mainTexture = builtinFont.material.mainTexture;
                txtMat.color = color ?? Color.white;
                lineGo.GetComponent<MeshRenderer>().sharedMaterial = txtMat;
            }
            tm.text = text;
            tm.fontSize = 32;
            tm.fontStyle = fontStyle;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? Color.white;

            _panelObjects.Add(lineGo);
        }

        private void SpawnButton(string labelText, Vector3 localPos, float width, float height, System.Action onClickCallback)
        {
            var btnGo = new GameObject("HoloButton_" + labelText);
            btnGo.transform.SetParent(_activePanel.transform, false);
            btnGo.transform.localPosition = localPos;
            btnGo.transform.localScale = Vector3.one;

            var btn = btnGo.AddComponent<HolographicButton>();
            btn.width = width;
            btn.height = height;
            btn.buttonText = labelText;
            btn.OnClick = onClickCallback;

            _panelObjects.Add(btnGo);
        }

        private Font GetSafeBuiltinFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            return f;
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[BackendUI Error] {error}");
            // Spawn temporary error line
            SpawnTextLine("Network Error: " + error, -0.45f, NeonRed, FontStyle.Bold);
        }

        private string WrapText(string input, int lineLength)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string[] words = input.Split(' ');
            string result = "";
            string currentLine = "";

            foreach (string word in words)
            {
                if (currentLine.Length + word.Length > lineLength)
                {
                    result += currentLine + "\n";
                    currentLine = word + " ";
                }
                else
                {
                    currentLine += word + " ";
                }
            }
            result += currentLine;
            return result;
        }

        private void OnDestroy()
        {
            // Unsubscribe from connector events
            if (connector != null)
            {
                connector.OnStudentCreated -= HandleStudentCreated;
                connector.OnOnboardingStarted -= HandleOnboardingStarted;
                connector.OnOnboardingSubmitted -= HandleOnboardingSubmitted;
                connector.OnQuizGenerated -= HandleQuizGenerated;
                connector.OnNotesGenerated -= HandleNotesGenerated;
                connector.OnChatResponseReceived -= HandleChatResponseReceived;
                connector.OnError -= HandleError;
            }
        }
    }
}
