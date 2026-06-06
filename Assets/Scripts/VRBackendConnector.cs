using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using System.Collections.Generic;

/// <summary>
/// VRBackendConnector - Connects the Unity Client to the FastAPI Backend
/// 
/// This script handles all REST requests to the FastAPI backend at http://localhost:8000.
/// It uses Unity's native JsonUtility to ensure zero dependencies on third-party JSON libraries.
/// </summary>
public class VRBackendConnector : MonoBehaviour
{
    [Header("Server Configuration")]
    [SerializeField] private string baseUrl = "http://localhost:8000";
    [SerializeField] private int requestTimeout = 30;

    [SerializeField] private ConnectionStatusUI connectionStatusUI;

    private string studentId;
    private string currentLessonId;
    private string currentAssessmentId;
    private bool isConnected = false;

    public bool IsConnected() => isConnected;

    // ================================================================
    // EVENTS - Subscribe to these for response handling
    // ================================================================
    public delegate void OnResponseReceived(string response);
    public delegate void OnErrorReceived(string error);

    public event OnResponseReceived OnStudentCreated;
    public event OnResponseReceived OnOnboardingStarted;
    public event OnResponseReceived OnOnboardingSubmitted;
    public event OnResponseReceived OnContentGenerated;
    public event OnResponseReceived OnResponseSubmitted;
    public event OnResponseReceived OnQuizGenerated;
    public event OnResponseReceived OnNotesGenerated;
    public event OnResponseReceived OnChatResponseReceived;
    public event OnErrorReceived OnError;

    // ================================================================
    // INITIALIZATION & SESSION CONTROL
    // ================================================================
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBoot()
    {
        var connector = FindFirstObjectByType<VRBackendConnector>();
        if (connector == null)
        {
            GameObject backendManager = new GameObject("BackendManager");
            connector = backendManager.AddComponent<VRBackendConnector>();
            DontDestroyOnLoad(backendManager);
            Debug.Log("[BackendConnector] Auto-bootstrapped BackendManager GameObject and VRBackendConnector component.");
        }
    }

    private void Start()
    {
        if (connectionStatusUI == null)
        {
            connectionStatusUI = FindObjectOfType<ConnectionStatusUI>();
        }

        if (connectionStatusUI == null)
        {
            connectionStatusUI = ConnectionStatusUI.CreateDynamic();
            Debug.Log("[BackendConnector] Auto-spawned dynamic ConnectionStatusUI elements.");
        }

        if (!string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = baseUrl.TrimEnd('/');
        }
        CheckServerHealth();
    }

    public void SetServerUrl(string url)
    {
        baseUrl = string.IsNullOrEmpty(url) ? "" : url.TrimEnd('/');
        Debug.Log($"[BackendConnector] Server URL updated to: {baseUrl}");
    }

    public string GetStudentId()
    {
        return studentId;
    }

    public void ResetSession()
    {
        studentId = null;
        currentLessonId = null;
        currentAssessmentId = null;
        Debug.Log("[BackendConnector] Session reset");
        if (connectionStatusUI != null)
            connectionStatusUI.ShowWarning("Session Reset");
    }

    // ================================================================
    // 1. STUDENT MANAGEMENT
    // ================================================================
    
    public void CreateStudent(string name, string email = "", int classNumber = 10)
    {
        StartCoroutine(CreateStudentCoroutine(name, email, classNumber));
    }

    private IEnumerator CreateStudentCoroutine(string name, string email, int classNumber)
    {
        var requestData = new StudentCreateRequest
        {
            name = name,
            email = string.IsNullOrEmpty(email) ? name.ToLower().Replace(" ", "") + "@school.com" : email,
            class_number = classNumber
        };

        yield return SendPostRequest($"{baseUrl}/students/create", requestData, 
            (response) =>
            {
                var result = JsonUtility.FromJson<StudentResponse>(response);
                if (result != null)
                {
                    studentId = result.student_id;
                    
                    // Highly visible console window pop-up message
                    Debug.Log("<color=#00FFCC><b>\n========================================================================\n" +
                              "  🔌 [UNITY VR BACKEND] CONNECTION OPEN AND ESTABLISHED!\n" +
                              "  ------------------------------------------------------------------------\n" +
                              "  Status: SUCCESS | Link to FastAPI Multi-Agent server is Active.\n" +
                              "  Server Address: " + baseUrl + "\n" +
                              "  Registered Student ID: " + studentId + " (" + name + ")\n" +
                              "========================================================================\n</b></color>");
                              
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.DisplayDialog(
                        "Backend Connected",
                        $"Registered Student: {name}\nStudent ID: {studentId}\nBackend Server: {baseUrl}",
                        "OK"
                    );
#endif
                              
                    if (connectionStatusUI != null)
                        connectionStatusUI.ShowSuccess($"Student '{name}' Created");

                    OnStudentCreated?.Invoke(response);
                }
            });
    }

    // ================================================================
    // 2. ONBOARDING & ASSESSMENT
    // ================================================================
    
    public void StartOnboarding(string subjectCode, string topicCode)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            OnError?.Invoke("Student ID not set. Create a student first.");
            if (connectionStatusUI != null)
                connectionStatusUI.ShowError("No student ID. Create student first.");
            return;
        }
        StartCoroutine(StartOnboardingCoroutine(subjectCode, topicCode));
    }

    private IEnumerator StartOnboardingCoroutine(string subjectCode, string topicCode)
    {
        var requestData = new OnboardingStartRequest
        {
            student_id = studentId,
            subject_code = subjectCode,
            topic_code = topicCode
        };

        yield return SendPostRequest($"{baseUrl}/onboarding/start", requestData,
            (response) =>
            {
                var result = JsonUtility.FromJson<OnboardingResponse>(response);
                if (result != null)
                {
                    currentAssessmentId = result.assessment_id;
                    Debug.Log($"[BackendConnector] Onboarding started, ID: {currentAssessmentId}");
                    
                    if (connectionStatusUI != null)
                        connectionStatusUI.ShowSuccess($"Onboarding Started: {topicCode}");

                    OnOnboardingStarted?.Invoke(response);
                }
            });
    }

    public void SubmitOnboardingResponse(string subjectCode, string topicCode, string[] questions, string[] responses)
    {
        if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(currentAssessmentId))
        {
            OnError?.Invoke("Cannot submit onboarding. Onboarding session not initialized.");
            if (connectionStatusUI != null)
                connectionStatusUI.ShowError("Cannot submit. Session not initialized.");
            return;
        }
        StartCoroutine(SubmitOnboardingCoroutine(subjectCode, topicCode, questions, responses));
    }

    private IEnumerator SubmitOnboardingCoroutine(string subjectCode, string topicCode, string[] questions, string[] responses)
    {
        var requestData = new OnboardingSubmitRequest
        {
            assessment_id = currentAssessmentId,
            student_id = studentId,
            subject_code = subjectCode,
            topic_code = topicCode,
            questions = questions,
            responses = responses
        };

        yield return SendPostRequest($"{baseUrl}/onboarding/submit", requestData,
            (response) =>
            {
                Debug.Log("[BackendConnector] Onboarding responses submitted successfully.");
                
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess("Onboarding Responses Submitted");

                OnOnboardingSubmitted?.Invoke(response);
            });
    }

    // ================================================================
    // 3. TEACHING CONTENT GENERATION
    // ================================================================
    
    public void GenerateTeachingContent(string subjectCode, string topicCode)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            OnError?.Invoke("Student ID not set. Create a student first.");
            if (connectionStatusUI != null)
                connectionStatusUI.ShowError("No student ID set");
            return;
        }
        StartCoroutine(GenerateTeachingContentCoroutine(subjectCode, topicCode));
    }

    private IEnumerator GenerateTeachingContentCoroutine(string subjectCode, string topicCode)
    {
        var requestData = new TeachingContentRequest
        {
            student_id = studentId,
            subject_code = subjectCode,
            topic_code = topicCode
        };

        yield return SendPostRequest($"{baseUrl}/teaching/generate-content", requestData,
            (response) =>
            {
                var result = JsonUtility.FromJson<TeachingContentResponse>(response);
                if (result != null)
                {
                    currentLessonId = result.scene_id; // Using scene_id as lesson_id fallback
                    Debug.Log($"[BackendConnector] Teaching content generated. Lesson ID: {currentLessonId}");
                    
                    if (connectionStatusUI != null)
                        connectionStatusUI.ShowSuccess($"Lesson Generated: {topicCode}");

                    OnContentGenerated?.Invoke(response);
                }
            });
    }

    // ================================================================
    // 4. STUDENT RESPONSE & FEEDBACK
    // ================================================================
    
    public void SubmitStudentResponse(string lessonId, string response)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            OnError?.Invoke("Student ID not set.");
            if (connectionStatusUI != null)
                connectionStatusUI.ShowError("No student ID set");
            return;
        }
        StartCoroutine(SubmitStudentResponseCoroutine(lessonId, response));
    }

    private IEnumerator SubmitStudentResponseCoroutine(string lessonId, string response)
    {
        var requestData = new SubmitResponseRequest
        {
            student_id = studentId,
            lesson_id = lessonId,
            response = response
        };

        yield return SendPostRequest($"{baseUrl}/teaching/submit-response", requestData,
            (resp) =>
            {
                Debug.Log("[BackendConnector] Student response submitted.");
                
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess("Response Submitted");

                OnResponseSubmitted?.Invoke(resp);
            });
    }

    // ================================================================
    // 5. QUIZ GENERATION
    // ================================================================
    
    public void GenerateQuiz(string topic, int classLevel = 10)
    {
        StartCoroutine(GenerateQuizCoroutine(topic, classLevel));
    }

    private IEnumerator GenerateQuizCoroutine(string topic, int classLevel)
    {
        var requestData = new QuizRequest
        {
            topic = topic,
            class_level = classLevel
        };

        yield return SendPostRequest($"{baseUrl}/gen/quiz", requestData,
            (response) =>
            {
                Debug.Log("[BackendConnector] Quiz generated.");
                
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"Quiz Generated: {topic}");

                OnQuizGenerated?.Invoke(response);
            });
    }

    // ================================================================
    // 6. NOTES GENERATION
    // ================================================================
    
    public void GenerateNotes(string topic, int classLevel = 10)
    {
        StartCoroutine(GenerateNotesCoroutine(topic, classLevel));
    }

    private IEnumerator GenerateNotesCoroutine(string topic, int classLevel)
    {
        var requestData = new NotesRequest
        {
            topic = topic,
            class_level = classLevel
        };

        yield return SendPostRequest($"{baseUrl}/gen/notes", requestData,
            (response) =>
            {
                Debug.Log("[BackendConnector] Notes generated.");
                
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"Notes Generated: {topic}");

                OnNotesGenerated?.Invoke(response);
            });
    }

    // ================================================================
    // 7. CHAT & RAG QUERY
    // ================================================================
    
    public void SendChatQuery(string query)
    {
        StartCoroutine(SendChatQueryCoroutine(query));
    }

    private IEnumerator SendChatQueryCoroutine(string query)
    {
        var requestData = new ChatRequest
        {
            query = query
        };

        yield return SendPostRequest($"{baseUrl}/chat", requestData,
            (response) =>
            {
                Debug.Log("[BackendConnector] Chat response received.");
                
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess("Chat Response Received");

                OnChatResponseReceived?.Invoke(response);
            });
    }

    // ================================================================
    // 8. HEALTH CHECK
    // ================================================================
    
    public void CheckServerHealth()
    {
        StartCoroutine(CheckServerHealthCoroutine());
    }

    private IEnumerator CheckServerHealthCoroutine()
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        using (UnityWebRequest request = UnityWebRequest.Get($"{baseUrl}/health"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                isConnected = true;
                Debug.Log("<color=#00FFCC><b>\n========================================================================\n" +
                          "  🔌 [UNITY VR BACKEND] CONNECTION OPEN AND ESTABLISHED!\n" +
                          "  ------------------------------------------------------------------------\n" +
                          "  Status: SUCCESS | Link to FastAPI Multi-Agent server is Active.\n" +
                          "  Server Address: " + baseUrl + "\n" +
                          "========================================================================\n</b></color>");
                
#if UNITY_EDITOR
                UnityEditor.EditorUtility.DisplayDialog(
                    "Backend Connected",
                    $"Successfully connected to FastAPI Backend!\n\nBackend Server: {baseUrl}",
                    "OK"
                );
#endif

                if (connectionStatusUI != null)
                    connectionStatusUI.ShowConnected($"✅ Connected to {baseUrl}");
            }
            else
            {
                isConnected = false;
                Debug.LogError("<color=#FF3366><b>\n========================================================================\n" +
                               "  ❌ [UNITY VR BACKEND] CONNECTION FAILED!\n" +
                               "  ------------------------------------------------------------------------\n" +
                               "  Status: ERROR | Could not link to FastAPI Multi-Agent server.\n" +
                               "  Server Address: " + baseUrl + "\n" +
                               "  Error: " + request.error + "\n" +
                               "========================================================================\n</b></color>");
                
#if UNITY_EDITOR
                UnityEditor.EditorUtility.DisplayDialog(
                    "Backend Connection Failed",
                    $"Unable to reach the FastAPI Backend!\n\nBackend Server: {baseUrl}\nError: {request.error}\n\nPlease check if backend server is running.",
                    "OK"
                );
#endif

                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"Backend unreachable: {baseUrl}");
            }
        }
    }

    // ================================================================
    // CORE HTTP REQUEST METHODS
    // ================================================================
    
    private IEnumerator SendPostRequest(string url, object requestData, Action<string> onSuccess = null)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        string json = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            HandleResponse(request, onSuccess);
        }
    }

    private void HandleResponse(UnityWebRequest request, Action<string> onSuccess)
    {
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            onSuccess?.Invoke(responseText);
        }
        else
        {
            string errorMsg = $"HTTP {request.responseCode}: {request.error}";
            if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
            {
                errorMsg += $" | Response: {request.downloadHandler.text}";
            }
            Debug.LogError($"[BackendConnector] Request failed: {errorMsg}");
            
            if (connectionStatusUI != null)
                connectionStatusUI.ShowError($"Request failed: {request.error}");

            OnError?.Invoke(errorMsg);
        }
    }
}

// ================================================================
// DEDICATED REQUEST DATA STRUCTURES FOR JSONUTILITY
// ================================================================

[System.Serializable]
public class StudentCreateRequest
{
    public string name;
    public string email;
    public int class_number;
}

[System.Serializable]
public class OnboardingStartRequest
{
    public string student_id;
    public string subject_code;
    public string topic_code;
}

[System.Serializable]
public class OnboardingSubmitRequest
{
    public string assessment_id;
    public string student_id;
    public string subject_code;
    public string topic_code;
    public string[] questions;
    public string[] responses;
}

[System.Serializable]
public class TeachingContentRequest
{
    public string student_id;
    public string subject_code;
    public string topic_code;
}

[System.Serializable]
public class SubmitResponseRequest
{
    public string student_id;
    public string lesson_id;
    public string response;
}

[System.Serializable]
public class QuizRequest
{
    public string topic;
    public int class_level;
}

[System.Serializable]
public class NotesRequest
{
    public string topic;
    public int class_level;
}

[System.Serializable]
public class ChatRequest
{
    public string query;
}

// ================================================================
// DEDICATED RESPONSE DATA STRUCTURES FOR JSONUTILITY
// ================================================================

[System.Serializable]
public class StudentResponse
{
    public string student_id;
    public string message;
}

[System.Serializable]
public class OnboardingResponse
{
    public string assessment_id;
    public string[] questions;
}

[System.Serializable]
public class TeachingContentResponse
{
    public string scene_id;
    public string lesson;
    public string vr_script;
}

[System.Serializable]
public class QuizResponse
{
    public string[] questions;
    public string[] answers;
}

[System.Serializable]
public class NotesResponse
{
    public string notes;
    public string summary;
}

[System.Serializable]
public class ChatResponse
{
    public string answer;
    public string[] sources;
}

[System.Serializable]
public class FeedbackResponse
{
    public string feedback;
    public float score;
}
