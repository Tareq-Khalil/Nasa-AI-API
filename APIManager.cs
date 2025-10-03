using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class AstronautData
{
    public float oxygen_level;
    public float co2_level;
    public float temperature;
    public float humidity;
    public float radiation;
    public int food_supply_days;
    public int water_supply_days;
    public string crew_health;
    
    // Add any custom fields you want - the API handles them all!
    // Example: public float pressure;
    // Example: public string location;
}

[System.Serializable]
public class TrainingDataPoint
{
    // Add all your sensor fields here
    public float oxygen_level;
    public float co2_level;
    public float temperature;
    public float humidity;
    public float radiation;
    public int food_supply_days;
    public int water_supply_days;
    
    // REQUIRED: Must be "alive" or "dead"
    public string status;
}

[System.Serializable]
public class PredictionRequest
{
    public AstronautData data;
}

[System.Serializable]
public class PredictionResponse
{
    public string astronomer_status;
    public float confidence;
    public string risk_level;
    public List<string> feedback;
    public MetricsData metrics;
}

[System.Serializable]
public class MetricsData
{
    public List<string> provided_fields;
    public int field_count;
}

[System.Serializable]
public class TrainingResponse
{
    public string message;
    public TrainingStats stats;
    public string model_accuracy;
}

[System.Serializable]
public class TrainingStats
{
    public int samples;
    public int features;
    public List<string> feature_names;
    public int alive_count;
    public int dead_count;
}

public class AstronautAIManager : MonoBehaviour
{
    [Header("API Configuration")]
    [Tooltip("Your API URL. Use http://localhost:8000 for local testing")]
    public string apiUrl = "http://localhost:8000";
    
    [Header("Training")]
    [Tooltip("Path to training data JSON file (in StreamingAssets folder)")]
    public string trainingDataPath = "training_data.json";
    
    [Header("Real-time Monitoring")]
    public AstronautData currentAstronautData;
    public bool autoCheckStatus = true;
    public float checkInterval = 5f; // Check every 5 seconds
    
    [Header("Status Display")]
    public PredictionResponse lastPrediction;
    public bool isModelTrained = false;
    public string lastError = "";
    
    private float nextCheckTime = 0f;
    
    // Events you can subscribe to
    public event Action<PredictionResponse> OnStatusUpdated;
    public event Action<string> OnCriticalAlert;
    public event Action<TrainingResponse> OnTrainingComplete;
    
    void Start()
    {
        // Check if API is online
        StartCoroutine(CheckAPIHealth());
    }
    
    void Update()
    {
        if (autoCheckStatus && isModelTrained && Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            CheckAstronautStatus(currentAstronautData);
        }
    }
    
    /// <summary>
    /// Check if API is online and ready
    /// </summary>
    public IEnumerator CheckAPIHealth()
    {
        using (UnityWebRequest request = UnityWebRequest.Get($"{apiUrl}/health"))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✓ API is online: " + request.downloadHandler.text);
                
                // Check if model is trained
                var response = JsonUtility.FromJson<Dictionary<string, object>>(request.downloadHandler.text);
                // Note: Unity's JsonUtility is limited, so we'll just check the response
                if (request.downloadHandler.text.Contains("\"model_trained\":true"))
                {
                    isModelTrained = true;
                    Debug.Log("✓ Model is already trained and ready");
                }
                else
                {
                    Debug.LogWarning("⚠ Model not trained yet. Call TrainModel() first.");
                }
            }
            else
            {
                lastError = $"API not reachable: {request.error}";
                Debug.LogError(lastError);
                Debug.LogError("Make sure the Python API is running: uvicorn main:app --reload");
            }
        }
    }
    
    /// <summary>
    /// Train the AI model with your data
    /// Call this once to train the model, or whenever you want to retrain
    /// </summary>
    public void TrainModel(List<TrainingDataPoint> trainingData)
    {
        StartCoroutine(TrainModelCoroutine(trainingData));
    }
    
    /// <summary>
    /// Train model from JSON file in StreamingAssets
    /// </summary>
    public void TrainModelFromFile()
    {
        StartCoroutine(TrainModelFromFileCoroutine());
    }
    
    private IEnumerator TrainModelFromFileCoroutine()
    {
        string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, trainingDataPath);
        
        if (!System.IO.File.Exists(filePath))
        {
            lastError = $"Training file not found: {filePath}";
            Debug.LogError(lastError);
            yield break;
        }
        
        byte[] fileData = System.IO.File.ReadAllBytes(filePath);
        
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormFileSection("file", fileData, "training_data.json", "application/json"));
        
        using (UnityWebRequest request = UnityWebRequest.Post($"{apiUrl}/train", formData))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(request.downloadHandler.text);
                isModelTrained = true;
                Debug.Log($"✓ Training complete! Accuracy: {response.model_accuracy}");
                Debug.Log($"Trained on {response.stats.samples} samples with {response.stats.features} features");
                
                OnTrainingComplete?.Invoke(response);
            }
            else
            {
                lastError = $"Training failed: {request.error}\n{request.downloadHandler.text}";
                Debug.LogError(lastError);
            }
        }
    }
    
    private IEnumerator TrainModelCoroutine(List<TrainingDataPoint> trainingData)
    {
        // Convert training data to JSON
        string jsonData = JsonHelper.ToJson(trainingData.ToArray(), true);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormFileSection("file", bodyRaw, "training_data.json", "application/json"));
        
        using (UnityWebRequest request = UnityWebRequest.Post($"{apiUrl}/train", formData))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(request.downloadHandler.text);
                isModelTrained = true;
                Debug.Log($"✓ Training complete! Accuracy: {response.model_accuracy}");
                
                OnTrainingComplete?.Invoke(response);
            }
            else
            {
                lastError = $"Training failed: {request.error}";
                Debug.LogError(lastError);
            }
        }
    }
    
    /// <summary>
    /// Check astronaut status with current data
    /// This is the main function you'll call repeatedly in your game
    /// </summary>
    public void CheckAstronautStatus(AstronautData data)
    {
        if (!isModelTrained)
        {
            Debug.LogWarning("Model not trained yet. Train first!");
            return;
        }
        
        StartCoroutine(CheckStatusCoroutine(data));
    }
    
    private IEnumerator CheckStatusCoroutine(AstronautData data)
    {
        // Create request wrapper
        PredictionRequest requestData = new PredictionRequest { data = data };
        string jsonData = JsonUtility.ToJson(requestData);
        
        using (UnityWebRequest request = new UnityWebRequest($"{apiUrl}/predict-json", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                PredictionResponse response = JsonUtility.FromJson<PredictionResponse>(request.downloadHandler.text);
                lastPrediction = response;
                
                // Log results
                Debug.Log($"Status: {response.astronomer_status} (Confidence: {response.confidence:P1})");
                Debug.Log($"Risk Level: {response.risk_level}");
                foreach (string feedback in response.feedback)
                {
                    Debug.Log($"  • {feedback}");
                }
                
                // Trigger events
                OnStatusUpdated?.Invoke(response);
                
                if (response.risk_level == "CRITICAL" || response.astronomer_status == "dead")
                {
                    OnCriticalAlert?.Invoke("CRITICAL STATUS DETECTED!");
                }
            }
            else
            {
                lastError = $"Prediction failed: {request.error}";
                Debug.LogError(lastError);
            }
        }
    }
    
    /// <summary>
    /// Update a specific sensor value
    /// </summary>
    public void UpdateSensor(string sensorName, float value)
    {
        switch (sensorName.ToLower())
        {
            case "oxygen":
            case "oxygen_level":
                currentAstronautData.oxygen_level = value;
                break;
            case "co2":
            case "co2_level":
                currentAstronautData.co2_level = value;
                break;
            case "temperature":
            case "temp":
                currentAstronautData.temperature = value;
                break;
            case "humidity":
                currentAstronautData.humidity = value;
                break;
            case "radiation":
            case "rad":
                currentAstronautData.radiation = value;
                break;
            default:
                Debug.LogWarning($"Unknown sensor: {sensorName}");
                break;
        }
    }
}

/// <summary>
/// Helper class for JSON array serialization (Unity's JsonUtility doesn't support arrays directly)
/// </summary>
public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
        return wrapper.Items;
    }
    
    public static string ToJson<T>(T[] array, bool prettyPrint = false)
    {
        Wrapper<T> wrapper = new Wrapper<T> { Items = array };
        return JsonUtility.ToJson(wrapper, prettyPrint);
    }
    
    [System.Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}
