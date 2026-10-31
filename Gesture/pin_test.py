"""AirFlick PIN Gesture Test & Diagnostic.

Tests the production PIN1 - PIN5 gesture detection:
- Uses scale-invariant thumb extension logic from gesture_engine.py to prevent PIN4 vs PIN5 confusion.
- Enforces AirFlick's three-digit PIN workflow (stops appending at 3 digits).
- Re-arms when hand returns to neutral / open / unknown.
- Uses robust model path resolution and consistent selfie mirroring.

Press 'q' in the preview window to exit.
"""

import os
import sys
import time
import cv2
import mediapipe as mp

try:
    from gesture_engine import GestureEngine, NormalizedPoint, GestureClassifier, get_hand_scale, ThumbState
except ImportError:
    from .gesture_engine import GestureEngine, NormalizedPoint, GestureClassifier, get_hand_scale, ThumbState


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
        print(f"Error: MediaPipe model not found at: {model_path}")
        return

    base_options = mp.tasks.BaseOptions(model_asset_path=model_path)
    options = mp.tasks.vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=mp.tasks.vision.RunningMode.IMAGE,
        num_hands=1
    )

    try:
        landmarker = mp.tasks.vision.HandLandmarker.create_from_options(options)
    except Exception as e:
        print(f"Failed to initialize landmarker: {e}")
        return

    camera = cv2.VideoCapture(0)
    if not camera.isOpened():
        print("Camera could not be opened.")
        landmarker.close()
        return

    print("AirFlick 3-Digit PIN Detector started. Press Q to quit.")
    print("Gestures: PIN1 (Index), PIN2 (Index+Middle), PIN3 (3 fingers), PIN4 (4 fingers, thumb in), PIN5 (5 fingers).")

    pin = ""
    candidate_digit = ""
    candidate_start_time = 0.0
    HOLD_REQUIRED_SEC = 0.8
    re_armed = True

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
            current_digit = ""
            raw_gesture = "IDLE"
            thumb_info = ""

            if result.hand_landmarks:
                hand = result.hand_landmarks[0]
                landmarks_list = [NormalizedPoint(lm.x, lm.y, lm.z) for lm in hand]
                scale = get_hand_scale(landmarks_list)
                raw_gesture, finger_states, thumb_state = GestureClassifier.classify(landmarks_list, scale)
                thumb_info = str(thumb_state.value if hasattr(thumb_state, 'value') else thumb_state)

                if raw_gesture.startswith("PIN") and len(raw_gesture) == 4:
                    current_digit = raw_gesture[3]

                h, w, _ = frame.shape
                for lm in hand:
                    px = int(lm.x * w)
                    py = int(lm.y * h)
                    cv2.circle(frame, (px, py), 4, (0, 255, 0), -1)

            current_time = time.time()

            # Stability & hold check
            if current_digit:
                if current_digit == candidate_digit:
                    held_duration = current_time - candidate_start_time
                    if held_duration >= HOLD_REQUIRED_SEC and re_armed:
                        if len(pin) < 3:
                            pin += current_digit
                            re_armed = False
                else:
                    candidate_digit = current_digit
                    candidate_start_time = current_time
            else:
                candidate_digit = ""
                candidate_start_time = current_time
                re_armed = True

            # HUD Display
            cv2.putText(
                frame,
                f"Detected: {raw_gesture} (Thumb: {thumb_info})",
                (30, 45),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.75,
                (0, 255, 0) if current_digit else (180, 180, 180),
                2
            )

            pin_color = (0, 255, 255) if len(pin) == 3 else (255, 255, 255)
            cv2.putText(
                frame,
                f"AirFlick 3-Digit PIN: [{pin.ljust(3, '_')}]",
                (30, 95),
                cv2.FONT_HERSHEY_SIMPLEX,
                1.1,
                pin_color,
                3
            )

            status_note = "PIN Complete (3 digits entered)" if len(pin) >= 3 else "Hold gesture for 0.8s to enter digit"
            cv2.putText(
                frame,
                status_note,
                (30, 135),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (200, 200, 200),
                1
            )

            cv2.imshow("AirFlick PIN Test (3-Digit Entry)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        camera.release()
        landmarker.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
