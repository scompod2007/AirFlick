"""AirFlick Hand Landmark Visualization Test.

Tests webcam capture, selfie mirroring, and MediaPipe Hand Landmarker tracking.
Uses the same model path resolution and mirroring conventions as the production gesture service.

Press 'q' in the preview window to exit.
"""

import os
import sys
import cv2
import mediapipe as mp


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
        print(f"Error: MediaPipe HandLandmarker model file not found at: {model_path}")
        print("Please ensure hand_landmarker.task is in the gesture/models/ directory.")
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
        print(f"Failed to initialize MediaPipe HandLandmarker: {e}")
        return

    camera = None
    for idx in (0, 1, 2):
        if hasattr(cv2, "CAP_DSHOW"):
            cap = cv2.VideoCapture(idx, cv2.CAP_DSHOW)
            if cap.isOpened():
                ret, _ = cap.read()
                if ret:
                    camera = cap
                    print(f"Opened camera index {idx} using DirectShow.")
                    break
                cap.release()
        cap = cv2.VideoCapture(idx)
        if cap.isOpened():
            ret, _ = cap.read()
            if ret:
                camera = cap
                print(f"Opened camera index {idx} using Default backend.")
                break
            cap.release()

    if camera is None:
        print("Camera could not be opened. Check permissions and connection.")
        landmarker.close()
        return

    print("Hand tracking test started. Press Q in the window to quit.")

    try:
        while True:
            success, frame = camera.read()
            if not success:
                print("Failed to read camera frame.")
                break

            # Consistent selfie mirroring
            frame = cv2.flip(frame, 1)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            result = landmarker.detect(mp_image)
            hand_detected = False

            if result.hand_landmarks:
                hand_detected = True
                h, w, _ = frame.shape
                for hand in result.hand_landmarks:
                    for lm in hand:
                        px = int(lm.x * w)
                        py = int(lm.y * h)
                        cv2.circle(frame, (px, py), 4, (0, 255, 0), -1)

            status_text = "Hand Detected (Tracking Active)" if hand_detected else "No Hand in Frame"
            status_color = (0, 255, 0) if hand_detected else (0, 165, 255)
            cv2.putText(
                frame,
                status_text,
                (30, 45),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                status_color,
                2
            )

            cv2.imshow("AirFlick Hand Landmark Test", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
    finally:
        camera.release()
        landmarker.close()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
