using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Enhanced Backend Connector with automatic UI feedback
/// Handles all HTTP communication with FastAPI backend
/// Automatically shows connection status popups
/// </summary>
public class EnhancedVRBackendConnector : MonoBehaviour
{
    [SerializeField] private string serverURL = "http://localhost:8000";
    [SerializeField] private int requestTimeout = 30;
    private ConnectionStatusUI connectionStatusUI;
    private string studentId = "";

    // Events
    public event Action<string> OnStudentCreated;
    public event Action<string> OnContentGenerated;
    public event Action<string> OnResponseSubmitted;
    public event Action<string> OnError;
    public event Action<string> OnConnectionEstablished;

    void Start()
    {
        // Auto-find ConnectionStatusUI if not assigned
        if (connectionStatusUI == null)
        {
            connectionStatusUI = FindObjectOfType<ConnectionStatusUI>();
        }

        CheckServerHealth();
    }

    // ==================== CONNECTION CHECK ====================

    public void CheckServerHealth()
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        StartCoroutine(HealthCheckRoutine());
    }

    private IEnumerator HealthCheckRoutine()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(serverURL + "/health"))
        {
            request.timeout = requestTimeout;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowConnected($"✅ Connected to {serverURL}");
                
                OnConnectionEstablished?.Invoke("Backend connection established");
                Debug.Log("[Backend] Connection established with " + serverURL);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Failed to connect: {request.error}");
                
                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Connection failed: " + request.error);
            }
        }
    }

    // ==================== STUDENT OPERATIONS ====================

    public void CreateStudent(string name, string email, int classNumber)
    {
        StartCoroutine(CreateStudentRoutine(name, email, classNumber));
    }

    private IEnumerator CreateStudentRoutine(string name, string email, int classNumber)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        var requestData = new StudentRequest
        {
            name = name,
            email = email,
            class_number = classNumber
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(serverURL + "/students/create", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<StudentResponse>(request.downloadHandler.text);
                studentId = response.student_id;

                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"✓ Student '{name}' Created");

                OnStudentCreated?.Invoke(studentId);
                Debug.Log("[Backend] Student created: " + studentId);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Failed to create student: {request.error}");

                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Create student failed: " + request.error);
            }
        }
    }

    // ==================== TEACHING CONTENT ====================

    public void GenerateTeachingContent(string subjectCode, string topicCode)
    {
        StartCoroutine(GenerateTeachingContentRoutine(subjectCode, topicCode));
    }

    private IEnumerator GenerateTeachingContentRoutine(string subjectCode, string topicCode)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        var requestData = new ContentRequest
        {
            subject_code = subjectCode,
            topic_code = topicCode,
            student_id = studentId
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(serverURL + "/teaching/generate-content", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"✓ Lesson Generated: {topicCode}");

                OnContentGenerated?.Invoke(request.downloadHandler.text);
                Debug.Log("[Backend] Content generated for: " + topicCode);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Content generation failed: {request.error}");

                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Content generation failed: " + request.error);
            }
        }
    }

    // ==================== QUIZ GENERATION ====================

    public void GenerateQuiz(string topic, int classLevel)
    {
        StartCoroutine(GenerateQuizRoutine(topic, classLevel));
    }

    private IEnumerator GenerateQuizRoutine(string topic, int classLevel)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        var requestData = new QuizRequest
        {
            topic = topic,
            class_level = classLevel,
            student_id = studentId
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(serverURL + "/gen/quiz", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"✓ Quiz Generated: {topic}");

                Debug.Log("[Backend] Quiz generated: " + topic);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Quiz generation failed: {request.error}");

                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Quiz generation failed: " + request.error);
            }
        }
    }

    // ==================== NOTES GENERATION ====================

    public void GenerateNotes(string topic, int classLevel)
    {
        StartCoroutine(GenerateNotesRoutine(topic, classLevel));
    }

    private IEnumerator GenerateNotesRoutine(string topic, int classLevel)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        var requestData = new NotesRequest
        {
            topic = topic,
            class_level = classLevel,
            student_id = studentId
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(serverURL + "/gen/notes", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess($"✓ Notes Generated: {topic}");

                Debug.Log("[Backend] Notes generated: " + topic);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Notes generation failed: {request.error}");

                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Notes generation failed: " + request.error);
            }
        }
    }

    // ==================== CHAT ====================

    public void SendChatQuery(string query)
    {
        StartCoroutine(SendChatQueryRoutine(query));
    }

    private IEnumerator SendChatQueryRoutine(string query)
    {
        if (connectionStatusUI != null)
            connectionStatusUI.ShowConnecting();

        var requestData = new ChatRequest
        {
            query = query,
            student_id = studentId
        };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(serverURL + "/chat", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowSuccess("✓ Chat Response Received");

                Debug.Log("[Backend] Chat response: " + request.downloadHandler.text);
            }
            else
            {
                if (connectionStatusUI != null)
                    connectionStatusUI.ShowError($"❌ Chat failed: {request.error}");

                OnError?.Invoke(request.error);
                Debug.LogError("[Backend] Chat failed: " + request.error);
            }
        }
    }

    // ==================== UTILITIES ====================

    public bool IsConnected() => !string.IsNullOrEmpty(studentId);
    public string GetStudentId() => studentId;
    public void SetStudentId(string id) => studentId = id;
    public void ResetSession() => studentId = "";

    public void SetConnectionStatusUI(ConnectionStatusUI ui)
    {
        connectionStatusUI = ui;
    }

    // ==================== DATA CLASSES ====================

    [System.Serializable]
    public class StudentRequest
    {
        public string name;
        public string email;
        public int class_number;
    }

    [System.Serializable]
    public class StudentResponse
    {
        public string student_id;
        public string name;
    }

    [System.Serializable]
    public class ContentRequest
    {
        public string subject_code;
        public string topic_code;
        public string student_id;
    }

    [System.Serializable]
    public class QuizRequest
    {
        public string topic;
        public int class_level;
        public string student_id;
    }

    [System.Serializable]
    public class NotesRequest
    {
        public string topic;
        public int class_level;
        public string student_id;
    }

    [System.Serializable]
    public class ChatRequest
    {
        public string query;
        public string student_id;
    }
}
