"""AirFlick Gesture Diagnostic - CANCEL Test (DEPRECATED FOR GESTURE RECOGNITION).

IMPORTANT NOTICE:
Gesture-based cancellation has been DEPRECATED and REMOVED from the AirFlick gesture set.
Reason:
Previous implementations used bent finger counts (bent_count >= 3) to trigger CANCEL,
which caused severe false positives (e.g. extending 1 finger for PIN1 was mistakenly detected as CANCEL).
Cancellation is now safely handled exclusively through the application's UI "Cancel" button
and the 60-second automatic PIN expiration timer to ensure security and prevent accidental aborts.

This script runs in diagnostic mode using the production GestureEngine to demonstrate
that single-finger poses (PIN1) and fist poses (GRAB) do NOT trigger an accidental Cancel.
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
        print(f"Error: Model not found at {model_path}")
        print("Please ensure hand_landmarker.task exists in gesture/models/.")
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

    print("AirFlick Cancel Diagnostic started. Press Q to quit.")
    print("NOTE: Gesture-based cancellation is disabled. Use the AirFlick UI Cancel button.")

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
            detected_gesture = "NONE"

            if result.hand_landmarks:
                hand = result.hand_landmarks[0]
                landmarks_list = [NormalizedPoint(lm.x, lm.y, lm.z) for lm in hand]
                scale = get_hand_scale(landmarks_list)
                detected_gesture, _, _ = GestureClassifier.classify(landmarks_list, scale)

                h, w, _ = frame.shape
                for lm in hand:
                    px = int(lm.x * w)
                    py = int(lm.y * h)
                    cv2.circle(frame, (px, py), 4, (0, 255, 0), -1)

            # Informative HUD
            cv2.putText(
                frame,
                f"Production Gesture: {detected_gesture}",
                (30, 45),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                (0, 255, 0),
                2
            )

            cv2.putText(
                frame,
                "CANCEL Gesture: DEPRECATED (Use UI Cancel Button)",
                (30, 80),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (0, 165, 255),
                2
            )

            cv2.imshow("AirFlick CANCEL Diagnostic (Deprecated)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        camera.release()
        landmarker.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
