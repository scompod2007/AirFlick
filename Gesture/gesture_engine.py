"""AirFlick Final Gesture Detection & Recognition Engine.

Final Gesture Set:
1. GRAB: Close all fingers into a fist (Index, Middle, Ring, Pinky folded).
         Action: Prepare or confirm the already-selected file for sharing.
2. DROP: Thumb points downward while the other four fingers are folded.
         Action: Commit the pending share and begin automatic receiver resolution.
3. PIN1: Index finger extended; remaining fingers folded.
4. PIN2: Index and middle fingers extended; remaining fingers folded.
5. PIN3: Index, middle, and ring fingers extended; remaining fingers folded.
6. PIN4: Index, middle, ring, and pinky extended; thumb folded across palm.
7. PIN5: All five fingers extended wide, including thumb.
8. CONFIRM: Thumbs-up (thumb points upward, other 4 fingers folded).
         Action: Confirm only a specific pending confirmation when explicitly requested.
9. UNKNOWN: Ambiguous, transitioning, occluded pose -> Do nothing.

NOTE: All left-flick and right-flick gestures have been completely removed.
"""

from dataclasses import dataclass
from enum import Enum
import math
import time
from typing import Dict, List, Optional, Tuple


@dataclass
class NormalizedPoint:
    x: float
    y: float
    z: float = 0.0


@dataclass
class HandLandmarks:
    landmarks: List[NormalizedPoint]
    handedness: str
    confidence: float = 1.0


class ThumbState(str, Enum):
    EXTENDED = "EXTENDED"
    FOLDED = "FOLDED"
    UNCERTAIN = "UNCERTAIN"


def correct_handedness(mediapipe_label: str, is_mirrored_input: bool) -> str:
    clean_label = mediapipe_label.strip().capitalize()
    if not is_mirrored_input:
        return clean_label
    if clean_label == "Left":
        return "Right"
    elif clean_label == "Right":
        return "Left"
    return clean_label


def compute_euclidean_distance(p1: NormalizedPoint, p2: NormalizedPoint) -> float:
    return math.sqrt((p1.x - p2.x) ** 2 + (p1.y - p2.y) ** 2)


def get_hand_scale(landmarks: List[NormalizedPoint]) -> float:
    """Computes hand reference scale using wrist (0) to middle finger MCP (9)."""
    wrist = landmarks[0]
    middle_mcp = landmarks[9]
    return max(compute_euclidean_distance(wrist, middle_mcp), 0.001)


class FingerStateDetector:
    """Analyzes the extension state of non-thumb fingers and the thumb."""

    @staticmethod
    def get_non_thumb_states(landmarks: List[NormalizedPoint]) -> Dict[str, bool]:
        """Returns dict mapping finger name to boolean (True = Extended, False = Folded)."""
        wrist = landmarks[0]
        finger_indices = [
            ("index", 8, 6, 5),
            ("middle", 12, 10, 9),
            ("ring", 16, 14, 13),
            ("pinky", 20, 18, 17)
        ]

        states = {}
        for name, tip_idx, pip_idx, mcp_idx in finger_indices:
            tip = landmarks[tip_idx]
            pip = landmarks[pip_idx]
            mcp = landmarks[mcp_idx]

            d_tip = compute_euclidean_distance(tip, wrist)
            d_pip = compute_euclidean_distance(pip, wrist)
            d_mcp = compute_euclidean_distance(mcp, wrist)

            is_extended = (d_tip > d_pip * 1.08) and (d_tip > d_mcp * 1.15)
            states[name] = is_extended

        return states

    @staticmethod
    def get_thumb_state(landmarks: List[NormalizedPoint], scale: float, handedness: str = "Right") -> ThumbState:
        """Determines whether the thumb is EXTENDED, FOLDED across the palm, or UNCERTAIN."""
        thumb_tip = landmarks[4]
        index_mcp = landmarks[5]
        pinky_mcp = landmarks[17]

        d_tip_index_mcp = compute_euclidean_distance(thumb_tip, index_mcp) / scale
        d_tip_pinky_mcp = compute_euclidean_distance(thumb_tip, pinky_mcp) / scale

        # Extended criteria: thumb abducted wide away from palm & index MCP
        if d_tip_index_mcp >= 0.55 and d_tip_pinky_mcp >= 0.75:
            return ThumbState.EXTENDED

        # Folded criteria: thumb tucked inward across palm
        if d_tip_index_mcp < 0.48 or d_tip_pinky_mcp < 0.65:
            return ThumbState.FOLDED

        return ThumbState.UNCERTAIN

    @staticmethod
    def is_thumb_down(landmarks: List[NormalizedPoint], scale: float) -> bool:
        """DROP: Thumb points downward while other fingers are folded.
        Thumb tip (4) is lower in the frame (larger y) than thumb IP (3), thumb MCP (2), and wrist (0).
        Also thumb tip is extended away from wrist in downward direction.
        """
        thumb_tip = landmarks[4]
        thumb_ip = landmarks[3]
        thumb_mcp = landmarks[2]
        wrist = landmarks[0]

        # In image coordinates, y increases downwards.
        # Thumb pointing down means thumb_tip.y is noticeably greater than thumb_ip.y and thumb_mcp.y
        is_pointing_down = (thumb_tip.y > thumb_ip.y + 0.04) and (thumb_tip.y > thumb_mcp.y + 0.06)
        is_extended_down = compute_euclidean_distance(thumb_tip, thumb_mcp) / scale > 0.40
        return is_pointing_down and is_extended_down

    @staticmethod
    def is_thumb_up(landmarks: List[NormalizedPoint], scale: float) -> bool:
        """CONFIRM: Thumbs-up. Thumb points upward while other fingers are folded.
        Thumb tip (4) is higher in the frame (smaller y) than thumb IP (3), thumb MCP (2), and index MCP (5).
        """
        thumb_tip = landmarks[4]
        thumb_ip = landmarks[3]
        thumb_mcp = landmarks[2]
        index_mcp = landmarks[5]

        # In image coordinates, y decreases upwards.
        # Thumb pointing up means thumb_tip.y is noticeably smaller than thumb_ip.y and thumb_mcp.y
        is_pointing_up = (thumb_tip.y < thumb_ip.y - 0.04) and (thumb_tip.y < thumb_mcp.y - 0.06)
        is_extended_up = compute_euclidean_distance(thumb_tip, index_mcp) / scale > 0.45
        return is_pointing_up and is_extended_up


class GestureClassifier:
    """Deterministic, scale-invariant hand gesture classifier for the final AirFlick gesture set."""

    @staticmethod
    def classify(
        landmarks: List[NormalizedPoint],
        scale: float,
        handedness: str = "Right"
    ) -> Tuple[str, Dict[str, bool], ThumbState]:
        """Classifies static gesture into mutually exclusive categories:
        Returns (gesture_name, non_thumb_states, thumb_state).
        """
        non_thumb = FingerStateDetector.get_non_thumb_states(landmarks)
        thumb_state = FingerStateDetector.get_thumb_state(landmarks, scale, handedness)

        all_four_folded = (
            not non_thumb["index"]
            and not non_thumb["middle"]
            and not non_thumb["ring"]
            and not non_thumb["pinky"]
        )

        all_four_extended = (
            non_thumb["index"]
            and non_thumb["middle"]
            and non_thumb["ring"]
            and non_thumb["pinky"]
        )

        # 1. Closed hand poses when all 4 non-thumb fingers are folded:
        if all_four_folded:
            # Check DROP (thumb pointing down)
            if FingerStateDetector.is_thumb_down(landmarks, scale):
                return ("DROP", non_thumb, thumb_state)

            # Check CONFIRM (thumbs-up)
            if FingerStateDetector.is_thumb_up(landmarks, scale):
                return ("CONFIRM", non_thumb, thumb_state)

            # Check GRAB: Close all fingers into a fist (thumb folded/tucked)
            if thumb_state == ThumbState.FOLDED or (not FingerStateDetector.is_thumb_down(landmarks, scale) and not FingerStateDetector.is_thumb_up(landmarks, scale)):
                return ("GRAB", non_thumb, thumb_state)

            return ("UNKNOWN", non_thumb, thumb_state)

        # 2. PIN1: Only Index extended, remaining 3 non-thumb folded
        if (
            non_thumb["index"]
            and not non_thumb["middle"]
            and not non_thumb["ring"]
            and not non_thumb["pinky"]
        ):
            return ("PIN1", non_thumb, thumb_state)

        # 3. PIN2: Index + Middle extended, Ring + Pinky folded
        if (
            non_thumb["index"]
            and non_thumb["middle"]
            and not non_thumb["ring"]
            and not non_thumb["pinky"]
        ):
            return ("PIN2", non_thumb, thumb_state)

        # 4. PIN3: Index + Middle + Ring extended, Pinky folded
        if (
            non_thumb["index"]
            and non_thumb["middle"]
            and non_thumb["ring"]
            and not non_thumb["pinky"]
        ):
            return ("PIN3", non_thumb, thumb_state)

        # 5. PIN4 vs PIN5: All 4 non-thumb extended
        if all_four_extended:
            if thumb_state == ThumbState.FOLDED:
                return ("PIN4", non_thumb, thumb_state)
            elif thumb_state == ThumbState.EXTENDED:
                return ("PIN5", non_thumb, thumb_state)
            else:
                return ("UNKNOWN", non_thumb, thumb_state)

        return ("UNKNOWN", non_thumb, thumb_state)


@dataclass
class AirFlickGestureState:
    confirmed_gesture: str = "IDLE"
    last_emitted_gesture: str = "IDLE"
    last_trigger_time: float = 0.0
    cooldown_seconds: float = 1.0
    candidate_gesture: str = "IDLE"
    candidate_frames: int = 0
    required_frames: int = 4


class GestureEngine:
    def __init__(self, cooldown_seconds: float = 1.0, required_frames: int = 4):
        self.state = AirFlickGestureState(
            cooldown_seconds=cooldown_seconds,
            required_frames=required_frames
        )
        self.last_seen_time = 0.0

    def process_hand(
        self,
        landmarks: Optional[List[NormalizedPoint]],
        handedness_label: Optional[str],
        current_time: float,
        is_mirrored_input: bool = True
    ) -> Dict[str, object]:
        if not landmarks or len(landmarks) < 21:
            self.state.candidate_gesture = "IDLE"
            self.state.candidate_frames = 0
            self.state.confirmed_gesture = "IDLE"
            self.state.last_emitted_gesture = "IDLE"
            return {
                "raw_gesture": "NONE",
                "confirmed_gesture": "IDLE",
                "handedness": "None",
                "trigger_event": None,
                "finger_states": {},
                "thumb_state": "NONE",
                "emit_status": "NO_HAND"
            }

        self.last_seen_time = current_time
        real_handedness = correct_handedness(handedness_label or "Right", is_mirrored_input)
        scale = get_hand_scale(landmarks)

        raw_gesture, finger_states, thumb_state = GestureClassifier.classify(
            landmarks, scale, real_handedness
        )

        # Temporal Smoothing
        if raw_gesture == self.state.candidate_gesture:
            self.state.candidate_frames += 1
        else:
            self.state.candidate_gesture = raw_gesture
            self.state.candidate_frames = 1

        trigger_event: Optional[str] = None
        emit_status = "STABILIZING"

        threshold = self.state.required_frames

        if self.state.candidate_frames >= threshold:
            self.state.confirmed_gesture = raw_gesture

            # Re-arm on neutral or IDLE/UNKNOWN
            if raw_gesture in ("IDLE", "UNKNOWN"):
                self.state.last_emitted_gesture = "IDLE"
                emit_status = "IDLE/UNKNOWN"
            else:
                # Hold suppression
                if raw_gesture == self.state.last_emitted_gesture:
                    emit_status = "HELD (SUPPRESSED)"
                else:
                    time_since_trigger = current_time - self.state.last_trigger_time
                    if time_since_trigger >= self.state.cooldown_seconds:
                        trigger_event = raw_gesture
                        self.state.last_emitted_gesture = raw_gesture
                        self.state.last_trigger_time = current_time
                        emit_status = "EMITTED"
                    else:
                        emit_status = f"COOLDOWN ({self.state.cooldown_seconds - time_since_trigger:.1f}s)"
        else:
            emit_status = f"STABILIZING ({self.state.candidate_frames}/{threshold})"

        return {
            "raw_gesture": raw_gesture,
            "confirmed_gesture": self.state.confirmed_gesture,
            "handedness": real_handedness,
            "trigger_event": trigger_event,
            "finger_states": finger_states,
            "thumb_state": str(thumb_state.value if hasattr(thumb_state, 'value') else thumb_state),
            "emit_status": emit_status
        }
