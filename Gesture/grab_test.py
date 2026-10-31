"""AirFlick GRAB Gesture Test & Diagnostic.

Tests the production GRAB gesture:
- Gesture: Close all fingers into a fist (Index, Middle, Ring, Pinky folded).
- Action in AirFlick: Prepares the selected file for sharing and initiates device discovery.
- Uses the scale-invariant classifier from gesture_engine.py (not unnormalized pinch distance).

Press 'q' in the preview window to exit.
"""

import os
import sys
import cv2
import mediapipe as mp

try:
    from gesture_engine import GestureEngine, NormalizedPoint, GestureClassifier, get_hand_scale
except ImportError:
    from .gesture_engine import GestureEngine, NormalizedPoint, GestureClassifier, get_hand_scale


def get_model_path():
    candidates = [
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "models", "hand_landmarker.task"),
        os.path.join(os.getcwd(), "gesture", "models", "hand_landmarker.task"),
        os.path.join(os.getcwd(), "models", "hand_landmarker.task"),
    ]
    for p in candidates:
        if os.path.exists(p):
            return p
    return candidates[0]


def main():
    model_path = get_model_path()
    if not os.path.exists(model_path):
        print(f"Error: Model file not found at {model_path}")
        return

    base_options = mp.tasks.BaseOptions(model_asset_path=model_path)
    options = mp.tasks.vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=mp.tasks.vision.RunningMode.IMAGE,
        num_hands=1
    )

    landmarker = mp.tasks.vision.HandLandmarker.create_from_options(options)
    camera = cv2.VideoCapture(0)

    if not camera.isOpened():
        print("Camera could not be opened.")
        return

    print("AirFlick GRAB Test started. Form a closed fist to test GRAB. Press Q to quit.")

    try:
        while True:
            success, frame = camera.read()
            if not success:
                break

            # Consistent selfie mirroring
            frame = cv2.flip(frame, 1)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            result = landmarker.detect(mp_image)
            gesture = "OPEN"
            is_grab = False

            if result.hand_landmarks:
                hand = result.hand_landmarks[0]
                landmarks_list = [NormalizedPoint(lm.x, lm.y, lm.z) for lm in hand]
                scale = get_hand_scale(landmarks_list)
                gesture, finger_states, thumb_state = GestureClassifier.classify(landmarks_list, scale)
                is_grab = (gesture == "GRAB")

                h, w, _ = frame.shape
                dot_color = (0, 0, 255) if is_grab else (0, 255, 0)
                for lm in hand:
                    px = int(lm.x * w)
                    py = int(lm.y * h)
                    cv2.circle(frame, (px, py), 4, dot_color, -1)

            # HUD
            color = (0, 255, 0) if is_grab else (220, 220, 220)
            cv2.putText(
                frame,
                f"Gesture: {gesture}",
                (30, 50),
                cv2.FONT_HERSHEY_SIMPLEX,
                1.0,
                color,
                2
            )

            status_text = "GRAB ACTIVE: Prepares share & starts scan" if is_grab else "Show Fist for GRAB"
            cv2.putText(
                frame,
                status_text,
                (30, 90),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (255, 255, 255),
                1
            )

            cv2.imshow("AirFlick GRAB Test (Fist Detection)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        camera.release()
        landmarker.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
