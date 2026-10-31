"""AirFlick Flick Diagnostic & Deprecation Verification.

IMPORTANT NOTICE:
Left and right flick gestures have been DEPRECATED and COMPLETELY REMOVED from AirFlick.
Reason:
Flick gestures relied on noisy horizontal wrist velocity thresholds, leading to erratic triggers
when entering/exiting the camera frame. File sharing and device resolution are now handled
deterministically via the final gesture set:
  - GRAB (Closed fist): Prepares file for share & initiates discovery
  - DROP (Thumb pointing down): Commits share & auto-resolves receiver

This script demonstrates that horizontal wrist flicks produce NO event in the production engine.
Press 'q' in the preview window to exit.
"""

import os
import sys
import time
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

    print("AirFlick Flick Diagnostic started. Press Q to quit.")
    print("NOTE: Flick gestures are retired. Production engine ignores flick motions.")

    previous_x = None
    previous_time = time.time()
    legacy_flick_motion = "NONE"

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
            production_gesture = "NONE"

            if result.hand_landmarks:
                hand = result.hand_landmarks[0]
                landmarks_list = [NormalizedPoint(lm.x, lm.y, lm.z) for lm in hand]
                scale = get_hand_scale(landmarks_list)
                production_gesture, _, _ = GestureClassifier.classify(landmarks_list, scale)

                # Diagnostic legacy motion tracking (for comparison)
                wrist_x = hand[0].x
                current_time = time.time()
                if previous_x is not None:
                    dx = wrist_x - previous_x
                    dt = current_time - previous_time
                    if dt > 0:
                        speed = abs(dx) / dt
                        if speed > 0.40:
                            legacy_flick_motion = "FLICK RIGHT" if dx > 0 else "FLICK LEFT"
                        else:
                            legacy_flick_motion = "STEADY"

                previous_x = wrist_x
                previous_time = current_time

                h, w, _ = frame.shape
                for lm in hand:
                    px = int(lm.x * w)
                    py = int(lm.y * h)
                    cv2.circle(frame, (px, py), 4, (0, 255, 0), -1)

            # Informative HUD
            cv2.putText(
                frame,
                f"Production Gesture: {production_gesture}",
                (30, 45),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                (0, 255, 0),
                2
            )

            cv2.putText(
                frame,
                f"Observed Wrist Motion: {legacy_flick_motion} -> Production Action: NONE",
                (30, 80),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 165, 255),
                1
            )

            cv2.putText(
                frame,
                "FLICK Gestures REMOVED. Use GRAB (Fist) and DROP (Thumb-Down).",
                (30, 115),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (200, 200, 200),
                1
            )

            cv2.imshow("AirFlick FLICK Diagnostic (Deprecated)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        camera.release()
        landmarker.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
