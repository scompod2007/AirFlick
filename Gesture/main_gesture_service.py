"""AirFlick Unified Live Gesture Recognition Service with Real-Time Debug Overlay.

Final Gesture Set:
- GRAB (Fist: all fingers folded) -> Prepares share
- DROP (Thumb pointing down, 4 fingers folded) -> Commits share & auto-resolves receiver
- PIN1 to PIN5 (PIN entry digits 1 to 5)
- CONFIRM (Thumbs up) -> Confirms pending action
"""

import argparse
import os
import sys
import time
from typing import Optional, Tuple

import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision

# Ensure line-buffering across all platforms when stdout is piped to C# backend
try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(line_buffering=True)
except Exception:
    pass

try:
    from gesture_engine import GestureEngine, NormalizedPoint
except ImportError:
    from .gesture_engine import GestureEngine, NormalizedPoint


def find_default_model_path():
    candidates = [
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "models", "hand_landmarker.task"),
        os.path.join(os.getcwd(), "gesture", "models", "hand_landmarker.task"),
        os.path.join(os.getcwd(), "models", "hand_landmarker.task"),
    ]
    for p in candidates:
        if os.path.exists(p):
            return os.path.abspath(p)
    return os.path.abspath(candidates[0])


DEFAULT_MODEL_PATH = find_default_model_path()


def probe_and_open_camera(
    requested_camera_id: Optional[int] = None,
    desired_width: int = 640,
    desired_height: int = 480
) -> Tuple[Optional[cv2.VideoCapture], int, str]:
    """Tries to find and open a working camera that actually yields valid physical frames.

    Tests multiple backends (DirectShow, MSMF, Default) and multiple indices (0, 1, 2)
    to handle Windows Hello IR cameras, virtual cameras, and multi-camera laptops.
    Verifies that cap.read() actually yields a non-empty frame.
    """
    backends = []
    # On Windows, cv2.CAP_DSHOW (700) is the most reliable for physical webcams
    if hasattr(cv2, "CAP_DSHOW"):
        backends.append((cv2.CAP_DSHOW, "DirectShow"))
    if hasattr(cv2, "CAP_MSMF"):
        backends.append((cv2.CAP_MSMF, "MediaFoundation"))
    backends.append((cv2.CAP_ANY, "Default"))

    # Build probe candidate list
    candidates = []
    if requested_camera_id is not None and requested_camera_id >= 0:
        for b_id, b_name in backends:
            candidates.append((requested_camera_id, b_id, b_name))

    # Fallback search across standard indices 0, 1, 2
    for idx in (0, 1, 2):
        for b_id, b_name in backends:
            entry = (idx, b_id, b_name)
            if entry not in candidates:
                candidates.append(entry)

    print(f"[AIRFLICK_CAMERA_PROBE] Starting camera discovery across {len(candidates)} candidates...", flush=True)

    for idx, b_id, b_name in candidates:
        try:
            print(f"[AIRFLICK_CAMERA_PROBE] Probing camera index {idx} with backend {b_name}...", flush=True)
            cap = cv2.VideoCapture(idx, b_id)
            if not cap.isOpened():
                cap.release()
                continue

            cap.set(cv2.CAP_PROP_FRAME_WIDTH, desired_width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, desired_height)
            if hasattr(cv2, "CAP_PROP_BUFFERSIZE"):
                cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

            # Test physical frame read
            ret, frame = cap.read()
            if ret and frame is not None and frame.size > 0:
                h, w = frame.shape[:2]
                print(f"[AIRFLICK_CAMERA_ACTIVE] index={idx} backend={b_name} resolution={w}x{h}", flush=True)
                return cap, idx, b_name
            else:
                print(f"[AIRFLICK_CAMERA_PROBE] Camera index {idx} ({b_name}) opened but returned empty frame.", flush=True)
                cap.release()
        except Exception as ex:
            print(f"[AIRFLICK_CAMERA_PROBE_ERROR] Exception probing index {idx} ({b_name}): {ex}", flush=True)

    print(
        "[AIRFLICK_CAMERA_ERROR] No functional physical webcam could be opened or read frames.\n"
        "Please check:\n"
        "1. Another app (Teams, Zoom, Camera app) is not holding the webcam exclusively.\n"
        "2. Windows Camera Privacy Settings allow desktop apps to access the camera "
        "(Settings -> Privacy & Security -> Camera -> 'Let desktop apps access your camera' is ON).\n"
        "3. Laptop physical camera privacy slider/shutter is open.",
        flush=True
    )
    return None, -1, "None"


def run_gesture_service(
    camera_id: int = 0,
    model_path: str = DEFAULT_MODEL_PATH,
    mirror_camera: bool = True,
    show_preview: bool = True
):
    if not os.path.exists(model_path):
        err = f"MediaPipe HandLandmarker model file not found at: {model_path}"
        print(f"[AIRFLICK_CAMERA_ERROR] {err}", flush=True)
        return

    base_options = python.BaseOptions(model_asset_path=model_path)
    options = vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.VIDEO,
        num_hands=1,
        min_hand_detection_confidence=0.5,
        min_hand_presence_confidence=0.5,
        min_tracking_confidence=0.5
    )

    try:
        landmarker = vision.HandLandmarker.create_from_options(options)
    except Exception as e:
        err = f"Failed to initialize MediaPipe HandLandmarker: {e}"
        print(f"[AIRFLICK_CAMERA_ERROR] {err}", flush=True)
        return

    cap, actual_idx, backend_name = probe_and_open_camera(requested_camera_id=camera_id)
    if cap is None:
        landmarker.close()
        return

    engine = GestureEngine(cooldown_seconds=1.0, required_frames=4)
    start_time = time.time()
    frame_count = 0
    last_fps_time = time.time()
    fps = 0.0

    print(f"AirFlick Gesture Recognition running on camera {actual_idx} ({backend_name}).", flush=True)
    print("Gestures: GRAB (Fist), DROP (Thumb Down), PIN1-PIN5, CONFIRM (Thumbs Up).", flush=True)
    if show_preview:
        print("Live preview window enabled. Press 'q' in the preview window to exit.", flush=True)

    try:
        while True:
            success, frame = cap.read()
            if not success or frame is None or frame.size == 0:
                print("[AIRFLICK_CAMERA_ERROR] Camera stream interrupted: read() returned empty frame.", flush=True)
                break

            frame_count += 1
            if mirror_camera:
                frame = cv2.flip(frame, 1)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            timestamp_ms = int((time.time() - start_time) * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            landmarks_list = None
            raw_handedness = "Right"

            if result.hand_landmarks and len(result.hand_landmarks) > 0:
                raw_landmarks = result.hand_landmarks[0]
                landmarks_list = [NormalizedPoint(lm.x, lm.y, lm.z) for lm in raw_landmarks]

                if result.handedness and len(result.handedness) > 0:
                    raw_handedness = result.handedness[0][0].category_name

            current_time = time.time()
            data = engine.process_hand(
                landmarks=landmarks_list,
                handedness_label=raw_handedness,
                current_time=current_time,
                is_mirrored_input=mirror_camera
            )

            # Heartbeat reporting every 30 frames (~1 sec)
            if frame_count % 30 == 0:
                now = time.time()
                fps = 30.0 / max(now - last_fps_time, 0.001)
                last_fps_time = now
                has_hand = 1 if landmarks_list else 0
                print(
                    f"[AIRFLICK_CAMERA_HEARTBEAT] frames={frame_count} fps={fps:.1f} hand={has_hand} "
                    f"gesture={data['confirmed_gesture']}",
                    flush=True
                )

            trigger_event = data["trigger_event"]
            if trigger_event:
                print(f"[AIRFLICK_GESTURE_EVENT] {trigger_event} (Hand: {data['handedness']})", flush=True)

            if show_preview:
                h, w, _ = frame.shape

                # 1. Draw Landmarks
                if landmarks_list:
                    for pt in landmarks_list:
                        px = int(pt.x * w)
                        py = int(pt.y * h)
                        cv2.circle(frame, (px, py), 4, (0, 255, 0), -1)

                # 2. Debug Information Overlay
                overlay = frame.copy()
                cv2.rectangle(overlay, (15, 15), (460, 160), (20, 20, 20), -1)
                cv2.addWeighted(overlay, 0.7, frame, 0.3, 0, frame)

                conf_color = (0, 255, 0) if data["confirmed_gesture"] not in ("IDLE", "UNKNOWN") else (180, 180, 180)
                cv2.putText(
                    frame,
                    f"Confirmed: {data['confirmed_gesture']}",
                    (25, 45),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.85,
                    conf_color,
                    2
                )

                cv2.putText(
                    frame,
                    f"Raw: {data['raw_gesture']} | Status: {data['emit_status']}",
                    (25, 75),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (220, 220, 220),
                    1
                )

                fs = data["finger_states"]
                if fs:
                    f_str = f"I:{int(fs.get('index',0))} M:{int(fs.get('middle',0))} R:{int(fs.get('ring',0))} P:{int(fs.get('pinky',0))}"
                else:
                    f_str = "None"

                thumb_state = data.get("thumb_state", "NONE")
                thumb_color = (0, 255, 255) if thumb_state == "EXTENDED" else ((255, 100, 100) if thumb_state == "FOLDED" else (200, 200, 200))
                cv2.putText(
                    frame,
                    f"Fingers: {f_str} | Thumb: {thumb_state}",
                    (25, 105),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    thumb_color,
                    1
                )

                cv2.putText(
                    frame,
                    f"Hand: {data['handedness']} | Cam: #{actual_idx} ({backend_name})",
                    (25, 135),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (255, 255, 255),
                    1
                )

                cv2.imshow("AirFlick Live Camera & Gesture Tracking", frame)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break

    finally:
        cap.release()
        landmarker.close()
        if show_preview:
            cv2.destroyAllWindows()
        print("[AIRFLICK_CAMERA_STOPPED] Gesture service stopped cleanly.", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="AirFlick Gesture Recognition Service")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default 0)")
    parser.add_argument("--model", type=str, default=DEFAULT_MODEL_PATH, help="Path to hand_landmarker.task")
    parser.add_argument("--no-mirror", action="store_true", help="Disable selfie mirroring")
    parser.add_argument("--no-preview", action="store_true", help="Run headless without OpenCV window")

    args = parser.parse_args()
    run_gesture_service(
        camera_id=args.camera,
        model_path=args.model,
        mirror_camera=not args.no_mirror,
        show_preview=not args.no_preview
    )
