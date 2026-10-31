# AirFlick Final Gesture System & Verification Guide

## 1. Final Gesture Specification

| Gesture | Hand Pose | Action in AirFlick |
|---|---|---|
| **`GRAB`** | Close all fingers into a fist | Prepare the selected file for sharing & start device discovery |
| **`DROP`** | Thumb points downward while other 4 fingers are folded | Commit share & automatically connect to receiver if exactly 1 eligible device exists |
| **`PIN1`** | Index extended, others folded | Enter PIN digit 1 |
| **`PIN2`** | Index + Middle extended, others folded | Enter PIN digit 2 |
| **`PIN3`** | Index + Middle + Ring extended, Pinky folded | Enter PIN digit 3 |
| **`PIN4`** | Index, Middle, Ring, Pinky extended; Thumb folded across palm | Enter PIN digit 4 |
| **`PIN5`** | All five fingers extended wide | Enter PIN digit 5 |
| **`CONFIRM`**| Thumbs-up (thumb points up, other 4 fingers folded) | Confirm pending confirmation when explicitly requested |
| **`UNKNOWN`**| Ambiguous, transitioning, or occluded pose | Do nothing |

> **Note:** All left-flick and right-flick gestures have been completely removed.
> The visible Cancel button in the UI is preserved as the reliable cancellation control.

---

## 2. Intended Grab-and-Drop Workflow

1. User selects a valid file using the AirFlick file picker.
2. User performs **`GRAB`** (fist) $\rightarrow$ AirFlick validates the file, prepares the share ticket with a 3-digit PIN, and starts scanning for nearby devices.
3. User performs **`DROP`** (thumb down) $\rightarrow$ AirFlick evaluates discovered devices:
   - **Exactly 1 non-self eligible device:** Automatically connects and enters the PIN authorization/transfer flow!
   - **0 eligible devices:** Displays clear status message and keeps discovery active.
   - **Multiple eligible devices:** Prompts user to select from the list without guessing.
4. On the receiving laptop, the user enters the 3-digit PIN (using **`PIN1`–`PIN5`** gestures or manual on-screen input).
5. The HMAC proof challenge is validated; the file is streamed in 64 KB chunks to `.part` staging.
6. SHA-256 integrity check passes; the file is safely finalized to the destination folder and the exact absolute path is displayed.

---

## 3. Running Automated Tests

```bash
cd E:\PROJECT\AirFlick\gesture
python test_gesture_engine.py
```

Verifies:
- `test_grab_gesture_all_fingers_folded_into_fist`: Verifies `GRAB` (fist).
- `test_drop_gesture_thumb_pointing_down`: Verifies `DROP` (thumb pointing down).
- `test_confirm_gesture_thumbs_up`: Verifies `CONFIRM` (thumbs-up).
- `test_pin1_through_pin5`: Verifies `PIN1` through `PIN5`.
- `test_no_flick_gestures_exist`: Verifies complete removal of flick detection.

---

## 4. Running the Live Service with Debug Overlay

```bash
cd E:\PROJECT\AirFlick\gesture
python main_gesture_service.py
```
*(Options: `--no-mirror` to disable selfie flip, `--camera 1` for external webcam, `--no-preview` for headless stdout)*
