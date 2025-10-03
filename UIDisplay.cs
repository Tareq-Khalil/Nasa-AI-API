using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Display astronaut status on screen
/// Attach this to a Canvas in your scene
/// </summary>
public class AstronautStatusUI : MonoBehaviour
{
    [Header("References")]
    public AstronautAIManager aiManager;
    
    [Header("UI Elements")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI confidenceText;
    public TextMeshProUGUI riskLevelText;
    public TextMeshProUGUI feedbackText;
    public Image statusIndicator;
    public Image riskIndicator;
    
    [Header("Sensor Displays")]
    public TextMeshProUGUI oxygenText;
    public TextMeshProUGUI co2Text;
    public TextMeshProUGUI temperatureText;
    public TextMeshProUGUI humidityText;
    public TextMeshProUGUI radiationText;
    public TextMeshProUGUI foodText;
    public TextMeshProUGUI waterText;
    
    [Header("Colors")]
    public Color aliveColor = Color.green;
    public Color deadColor = Color.red;
    public Color lowRiskColor = Color.green;
    public Color mediumRiskColor = Color.yellow;
    public Color highRiskColor = Color.red;
    public Color criticalRiskColor = new Color(0.8f, 0f, 0f);
    
    [Header("Buttons")]
    public Button trainButton;
    public Button checkStatusButton;
    
    void Start()
    {
        if (aiManager == null)
        {
            aiManager = FindObjectOfType<AstronautAIManager>();
        }
        
        // Subscribe to events
        if (aiManager != null)
        {
            aiManager.OnStatusUpdated += UpdateDisplay;
            aiManager.OnCriticalAlert += ShowCriticalAlert;
            aiManager.OnTrainingComplete += OnTrainingComplete;
        }
        
        // Setup buttons
        if (trainButton != null)
        {
            trainButton.onClick.AddListener(() => aiManager.TrainModelFromFile());
        }
        
        if (checkStatusButton != null)
        {
            checkStatusButton.onClick.AddListener(() => 
                aiManager.CheckAstronautStatus(aiManager.currentAstronautData));
        }
        
        UpdateSensorDisplay();
    }
    
    void Update()
    {
        // Update sensor displays in real-time
        UpdateSensorDisplay();
    }
    
    void UpdateSensorDisplay()
    {
        if (aiManager == null) return;
        
        var data = aiManager.currentAstronautData;
        
        if (oxygenText != null)
            oxygenText.text = $"O₂: {data.oxygen_level:F1}%";
        
        if (co2Text != null)
            co2Text.text = $"CO₂: {data.co2_level:F2}%";
        
        if (temperatureText != null)
            temperatureText.text = $"Temp: {data.temperature:F1}°C";
        
        if (humidityText != null)
            humidityText.text = $"Humidity: {data.humidity:F0}%";
        
        if (radiationText != null)
            radiationText.text = $"Radiation: {data.radiation:F3} mSv";
        
        if (foodText != null)
            foodText.text = $"Food: {data.food_supply_days} days";
        
        if (waterText != null)
            waterText.text = $"Water: {data.water_supply_days} days";
    }
    
    void UpdateDisplay(PredictionResponse response)
    {
        // Update status text
        if (statusText != null)
        {
            statusText.text = $"Status: {response.astronomer_status.ToUpper()}";
        }
        
        // Update confidence
        if (confidenceText != null)
        {
            confidenceText.text = $"Confidence: {response.confidence:P1}";
        }
        
        // Update risk level
        if (riskLevelText != null)
        {
            riskLevelText.text = $"Risk: {response.risk_level}";
        }
        
        // Update feedback
        if (feedbackText != null)
        {
            string feedbackDisplay = "<b>Feedback:</b>\n";
            foreach (string feedback in response.feedback)
            {
                feedbackDisplay += $"• {feedback}\n";
            }
            feedbackText.text = feedbackDisplay;
        }
        
        // Update status indicator color
        if (statusIndicator != null)
        {
            statusIndicator.color = response.astronomer_status == "alive" ? aliveColor : deadColor;
        }
        
        // Update risk indicator color
        if (riskIndicator != null)
        {
            switch (response.risk_level)
            {
                case "LOW":
                    riskIndicator.color = lowRiskColor;
                    break;
                case "MEDIUM":
                    riskIndicator.color = mediumRiskColor;
                    break;
                case "HIGH":
                    riskIndicator.color = highRiskColor;
                    break;
                case "CRITICAL":
                    riskIndicator.color = criticalRiskColor;
                    break;
            }
        }
    }
    
    void ShowCriticalAlert(string message)
    {
        Debug.LogError($"🚨 CRITICAL ALERT: {message}");
        
        // Add visual/audio alerts here
        // Example: Play alarm sound, flash screen red, show popup, etc.
    }
    
    void OnTrainingComplete(TrainingResponse response)
    {
        Debug.Log($"✓ Model trained successfully!");
        Debug.Log($"Accuracy: {response.model_accuracy}");
        Debug.Log($"Samples: {response.stats.samples}");
        
        if (trainButton != null)
        {
            trainButton.GetComponentInChildren<TextMeshProUGUI>().text = "Retrain Model";
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (aiManager != null)
        {
            aiManager.OnStatusUpdated -= UpdateDisplay;
            aiManager.OnCriticalAlert -= ShowCriticalAlert;
            aiManager.OnTrainingComplete -= OnTrainingComplete;
        }
    }
}

/// <summary>
/// Example game controller showing how to use the AI system
/// </summary>
public class GameController : MonoBehaviour
{
    public AstronautAIManager aiManager;
    
    void Start()
    {
        // Example: Train the model at game start
        // aiManager.TrainModelFromFile();
    }
    
    void Update()
    {
        // Example: Simulate changing sensor values
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SimulateCrisis();
        }
        
        // Example: Manual status check
        if (Input.GetKeyDown(KeyCode.C))
        {
            aiManager.CheckAstronautStatus(aiManager.currentAstronautData);
        }
    }
    
    void SimulateCrisis()
    {
        // Simulate oxygen leak
        aiManager.currentAstronautData.oxygen_level -= 2f;
        aiManager.currentAstronautData.co2_level += 0.3f;
        
        Debug.Log("⚠️ Simulating crisis - oxygen leak!");
        
        // Check status immediately
        aiManager.CheckAstronautStatus(aiManager.currentAstronautData);
    }
    
    /// <summary>
    /// Example: Connect to your habitat systems
    /// </summary>
    public void OnHabitatSystemUpdate(string systemName, float value)
    {
        aiManager.UpdateSensor(systemName, value);
    }
}
