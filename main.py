from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import pandas as pd
import numpy as np
import joblib
import json
from sklearn.ensemble import RandomForestClassifier
from sklearn.feature_extraction import DictVectorizer
from sklearn.preprocessing import StandardScaler
from typing import Dict, Any, Optional
import os

app = FastAPI(title="Astronaut Survival AI API", version="1.0.0")

# Enable CORS for Unity WebGL builds
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Global storage
vectorizer = None
model = None
scaler = None
feature_names = []
training_stats = {}

# Model file paths
MODEL_PATH = "astronaut_model.pkl"
VECTORIZER_PATH = "vectorizer.pkl"
SCALER_PATH = "scaler.pkl"

class PredictionInput(BaseModel):
    data: Dict[str, Any]

class PredictionResponse(BaseModel):
    astronomer_status: str
    confidence: float
    risk_level: str
    feedback: list
    metrics: Dict[str, Any]

def generate_feedback(data: Dict[str, Any], status: str, confidence: float) -> list:
    """Generate detailed feedback based on astronaut data"""
    feedback = []
    
    # Oxygen analysis
    if "oxygen_level" in data:
        o2 = data["oxygen_level"]
        if o2 < 18:
            feedback.append(f"⚠️ CRITICAL: Oxygen at {o2}% - Immediate action required!")
        elif o2 < 19.5:
            feedback.append(f"⚠️ WARNING: Oxygen at {o2}% - Below safe minimum")
        elif o2 > 23.5:
            feedback.append(f"⚠️ WARNING: Oxygen at {o2}% - Above safe maximum")
        else:
            feedback.append(f"✓ Oxygen at {o2}% - Normal")
    
    # CO2 analysis
    if "co2_level" in data:
        co2 = data["co2_level"]
        if co2 > 1.0:
            feedback.append(f"⚠️ CRITICAL: CO2 at {co2}% - Scrubbers failing!")
        elif co2 > 0.5:
            feedback.append(f"⚠️ WARNING: CO2 at {co2}% - Monitor closely")
        else:
            feedback.append(f"✓ CO2 at {co2}% - Normal")
    
    # Temperature analysis
    if "temperature" in data or "heat" in data:
        temp = data.get("temperature", data.get("heat", 0))
        if temp < 15:
            feedback.append(f"⚠️ WARNING: Temperature {temp}°C - Too cold")
        elif temp > 30:
            feedback.append(f"⚠️ WARNING: Temperature {temp}°C - Too hot")
        else:
            feedback.append(f"✓ Temperature {temp}°C - Comfortable")
    
    # Humidity analysis
    if "humidity" in data:
        humidity = data["humidity"]
        if humidity < 30:
            feedback.append(f"⚠️ WARNING: Humidity {humidity}% - Too dry")
        elif humidity > 70:
            feedback.append(f"⚠️ WARNING: Humidity {humidity}% - Too humid")
        else:
            feedback.append(f"✓ Humidity {humidity}% - Normal")
    
    # Radiation analysis
    if "radiation" in data:
        rad = data["radiation"]
        if rad > 0.5:
            feedback.append(f"⚠️ CRITICAL: Radiation {rad} mSv - Seek shelter!")
        elif rad > 0.1:
            feedback.append(f"⚠️ WARNING: Radiation {rad} mSv - Elevated levels")
        else:
            feedback.append(f"✓ Radiation {rad} mSv - Safe")
    
    # Food supply analysis
    if "food_supply_days" in data:
        food = data["food_supply_days"]
        if food < 7:
            feedback.append(f"⚠️ CRITICAL: Food supply {food} days - Emergency!")
        elif food < 30:
            feedback.append(f"⚠️ WARNING: Food supply {food} days - Ration required")
        else:
            feedback.append(f"✓ Food supply {food} days - Adequate")
    
    # Water supply analysis
    if "water_supply_days" in data:
        water = data["water_supply_days"]
        if water < 3:
            feedback.append(f"⚠️ CRITICAL: Water supply {water} days - Emergency!")
        elif water < 14:
            feedback.append(f"⚠️ WARNING: Water supply {water} days - Ration required")
        else:
            feedback.append(f"✓ Water supply {water} days - Adequate")
    
    # Overall status
    if status == "alive" and confidence > 0.8:
        feedback.append(f"✓ Overall status: STABLE (confidence: {confidence:.1%})")
    elif status == "alive":
        feedback.append(f"⚠️ Overall status: MARGINAL (confidence: {confidence:.1%})")
    else:
        feedback.append(f"⚠️ Overall status: CRITICAL (confidence: {confidence:.1%})")
    
    return feedback if feedback else ["No specific feedback available"]

def calculate_risk_level(data: Dict[str, Any], status: str, confidence: float) -> str:
    """Calculate overall risk level"""
    risk_score = 0
    
    # Check critical parameters
    if "oxygen_level" in data:
        if data["oxygen_level"] < 18 or data["oxygen_level"] > 25:
            risk_score += 3
        elif data["oxygen_level"] < 19.5 or data["oxygen_level"] > 23.5:
            risk_score += 1
    
    if "co2_level" in data and data["co2_level"] > 1.0:
        risk_score += 3
    
    if "radiation" in data and data["radiation"] > 0.5:
        risk_score += 3
    
    if "food_supply_days" in data and data["food_supply_days"] < 7:
        risk_score += 2
    
    if "water_supply_days" in data and data["water_supply_days"] < 3:
        risk_score += 2
    
    if status == "dead":
        return "CRITICAL"
    elif risk_score >= 5:
        return "HIGH"
    elif risk_score >= 2:
        return "MEDIUM"
    else:
        return "LOW"

@app.get("/")
async def root():
    """API health check"""
    return {
        "status": "online",
        "message": "Astronaut Survival AI API",
        "version": "1.0.0",
        "model_loaded": model is not None,
        "endpoints": ["/train", "/predict", "/predict-json", "/model-info", "/health"]
    }

@app.get("/health")
async def health():
    """Detailed health check"""
    return {
        "status": "healthy",
        "model_trained": model is not None,
        "features_count": len(feature_names) if feature_names else 0,
        "training_samples": training_stats.get("samples", 0)
    }

@app.post("/train")
async def train(file: UploadFile = File(...)):
    """
    Train the AI model on uploaded data.
    
    Expected JSON format:
    [
        {"oxygen_level": 20, "co2_level": 0.4, "temperature": 22, "status": "alive"},
        {"oxygen_level": 15, "co2_level": 2.0, "temperature": 35, "status": "dead"},
        ...
    ]
    
    The 'status' field must be either "alive" or "dead"
    All other fields are flexible - you can add any parameters
    """
    global model, vectorizer, scaler, feature_names, training_stats
    
    try:
        # Read file content
        content = await file.read()
        
        # Try to parse as JSON
        try:
            data = json.loads(content)
        except json.JSONDecodeError:
            # Try to parse as text (one JSON object per line)
            data = [json.loads(line) for line in content.decode().split('\n') if line.strip()]
        
        # Convert to DataFrame
        df = pd.DataFrame(data)
        
        # Validate required column
        if "status" not in df.columns:
            raise HTTPException(
                status_code=400,
                detail="Training data must include 'status' field with values 'alive' or 'dead'"
            )
        
        # Validate status values
        valid_statuses = df["status"].isin(["alive", "dead"])
        if not valid_statuses.all():
            invalid_count = (~valid_statuses).sum()
            raise HTTPException(
                status_code=400,
                detail=f"Found {invalid_count} invalid status values. Must be 'alive' or 'dead'"
            )
        
        # Separate features and target
        X = df.drop(columns=["status"]).to_dict(orient="records")
        y = df["status"].values
        
        # Store feature names
        feature_names = list(df.drop(columns=["status"]).columns)
        
        # Vectorize features (handles missing/extra fields automatically)
        vectorizer = DictVectorizer(sparse=False)
        X_vect = vectorizer.fit_transform(X)
        
        # Scale features
        scaler = StandardScaler()
        X_scaled = scaler.fit_transform(X_vect)
        
        # Train model
        model = RandomForestClassifier(
            n_estimators=100,
            max_depth=10,
            min_samples_split=5,
            random_state=42
        )
        model.fit(X_scaled, y)
        
        # Store training stats
        training_stats = {
            "samples": len(df),
            "features": len(feature_names),
            "feature_names": feature_names,
            "alive_count": (y == "alive").sum(),
            "dead_count": (y == "dead").sum()
        }
        
        # Save models
        joblib.dump(model, MODEL_PATH)
        joblib.dump(vectorizer, VECTORIZER_PATH)
        joblib.dump(scaler, SCALER_PATH)
        
        return {
            "message": "Training completed successfully",
            "stats": training_stats,
            "model_accuracy": f"{model.score(X_scaled, y):.2%}"
        }
        
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Training failed: {str(e)}")

@app.post("/predict")
async def predict(file: UploadFile = File(...)):
    """
    Predict astronaut survival status from uploaded JSON file.
    
    Expected JSON format:
    {
        "oxygen_level": 19.5,
        "co2_level": 0.4,
        "temperature": 22,
        "humidity": 50,
        ...any other fields...
    }
    """
    global model, vectorizer, scaler
    
    # Load model if not in memory
    if model is None:
        if not os.path.exists(MODEL_PATH):
            raise HTTPException(
                status_code=400,
                detail="Model not trained yet. Please call /train first"
            )
        model = joblib.load(MODEL_PATH)
        vectorizer = joblib.load(VECTORIZER_PATH)
        scaler = joblib.load(SCALER_PATH)
    
    try:
        # Read and parse JSON
        content = await file.read()
        data = json.loads(content)
        
        # Transform input
        X_new = vectorizer.transform([data])
        X_scaled = scaler.transform(X_new)
        
        # Predict
        pred = model.predict(X_scaled)[0]
        proba = model.predict_proba(X_scaled)[0]
        confidence = proba.max()
        
        # Calculate risk level
        risk_level = calculate_risk_level(data, pred, confidence)
        
        # Generate feedback
        feedback = generate_feedback(data, pred, confidence)
        
        return {
            "astronomer_status": pred,
            "confidence": float(confidence),
            "risk_level": risk_level,
            "feedback": feedback,
            "metrics": {
                "provided_fields": list(data.keys()),
                "field_count": len(data),
                "prediction_probabilities": {
                    "alive": float(proba[0]) if pred == "alive" else float(proba[1]),
                    "dead": float(proba[1]) if pred == "alive" else float(proba[0])
                }
            }
        }
        
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")

@app.post("/predict-json", response_model=PredictionResponse)
async def predict_json(input_data: PredictionInput):
    """
    Predict astronaut survival status from JSON body (no file upload needed).
    Useful for Unity integration.
    
    Example request body:
    {
        "data": {
            "oxygen_level": 19.5,
            "co2_level": 0.4,
            "temperature": 22
        }
    }
    """
    global model, vectorizer, scaler
    
    # Load model if not in memory
    if model is None:
        if not os.path.exists(MODEL_PATH):
            raise HTTPException(
                status_code=400,
                detail="Model not trained yet. Please call /train first"
            )
        model = joblib.load(MODEL_PATH)
        vectorizer = joblib.load(VECTORIZER_PATH)
        scaler = joblib.load(SCALER_PATH)
    
    try:
        data = input_data.data
        
        # Transform input
        X_new = vectorizer.transform([data])
        X_scaled = scaler.transform(X_new)
        
        # Predict
        pred = model.predict(X_scaled)[0]
        proba = model.predict_proba(X_scaled)[0]
        confidence = proba.max()
        
        # Calculate risk level
        risk_level = calculate_risk_level(data, pred, confidence)
        
        # Generate feedback
        feedback = generate_feedback(data, pred, confidence)
        
        return PredictionResponse(
            astronomer_status=pred,
            confidence=float(confidence),
            risk_level=risk_level,
            feedback=feedback,
            metrics={
                "provided_fields": list(data.keys()),
                "field_count": len(data),
                "prediction_probabilities": {
                    "alive": float(proba[0]) if pred == "alive" else float(proba[1]),
                    "dead": float(proba[1]) if pred == "alive" else float(proba[0])
                }
            }
        )
        
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")

@app.get("/model-info")
async def model_info():
    """Get information about the trained model"""
    if model is None:
        return {"message": "No model trained yet"}
    
    return {
        "model_type": "RandomForestClassifier",
        "training_stats": training_stats,
        "model_params": model.get_params() if model else None,
        "feature_names": feature_names
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
