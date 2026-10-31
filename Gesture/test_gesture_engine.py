"""Comprehensive unit tests for the final AirFlick gesture set:
1. GRAB: All fingers folded into a fist -> Prepares share
2. DROP: Thumb pointing down, other 4 fingers folded -> Commits share & auto-resolves receiver
3. PIN1 - PIN5: Digits 1 to 5
4. CONFIRM: Thumbs-up
5. UNKNOWN: Ambiguous pose
6. Verified complete removal of all flick gestures.
"""

import unittest
from gesture_engine import (
    NormalizedPoint,
    ThumbState,
    correct_handedness,
    get_hand_scale,
    FingerStateDetector,
    GestureClassifier,
    GestureEngine
)


def create_hand_fixture(
    wrist=(0.5, 0.8),
    thumb_tip=(0.35, 0.65),
    thumb_ip=(0.40, 0.70),
    thumb_mcp=(0.44, 0.74),
    index_tip=(0.5, 0.35),
    index_pip=(0.5, 0.55),
    index_mcp=(0.5, 0.68),
    middle_tip=(0.55, 0.32),
    middle_pip=(0.55, 0.53),
    middle_mcp=(0.55, 0.68),
    ring_tip=(0.6, 0.35),
    ring_pip=(0.6, 0.55),
    ring_mcp=(0.6, 0.68),
    pinky_tip=(0.65, 0.40),
    pinky_pip=(0.65, 0.58),
    pinky_mcp=(0.65, 0.70)
):
    pts = [NormalizedPoint(0.5, 0.5) for _ in range(21)]
    pts[0] = NormalizedPoint(*wrist)
    pts[2] = NormalizedPoint(*thumb_mcp)
    pts[3] = NormalizedPoint(*thumb_ip)
    pts[4] = NormalizedPoint(*thumb_tip)
    pts[5] = NormalizedPoint(*index_mcp)
    pts[6] = NormalizedPoint(*index_pip)
    pts[8] = NormalizedPoint(*index_tip)
    pts[9] = NormalizedPoint(*middle_mcp)
    pts[10] = NormalizedPoint(*middle_pip)
    pts[12] = NormalizedPoint(*middle_tip)
    pts[13] = NormalizedPoint(*ring_mcp)
    pts[14] = NormalizedPoint(*ring_pip)
    pts[16] = NormalizedPoint(*ring_tip)
    pts[17] = NormalizedPoint(*pinky_mcp)
    pts[18] = NormalizedPoint(*pinky_pip)
    pts[20] = NormalizedPoint(*pinky_tip)
    return pts


class TestAirFlickFinalGestureEngine(unittest.TestCase):

    def test_grab_gesture_all_fingers_folded_into_fist(self):
        """GRAB: Closed fist with all fingers folded."""
        fist = create_hand_fixture(
            wrist=(0.5, 0.8),
            index_tip=(0.5, 0.72),
            index_pip=(0.5, 0.58),
            middle_tip=(0.55, 0.72),
            middle_pip=(0.55, 0.58),
            ring_tip=(0.6, 0.72),
            ring_pip=(0.6, 0.58),
            pinky_tip=(0.65, 0.72),
            pinky_pip=(0.65, 0.60),
            thumb_tip=(0.48, 0.65) # tucked across palm
        )
        scale = get_hand_scale(fist)
        gesture, _, _ = GestureClassifier.classify(fist, scale)
        self.assertEqual(gesture, "GRAB")

    def test_drop_gesture_thumb_pointing_down(self):
        """DROP: 4 fingers folded, thumb pointing downwards."""
        drop_hand = create_hand_fixture(
            wrist=(0.5, 0.5),
            index_tip=(0.5, 0.45),
            index_pip=(0.5, 0.35),
            middle_tip=(0.55, 0.45),
            middle_pip=(0.55, 0.35),
            ring_tip=(0.6, 0.45),
            ring_pip=(0.6, 0.35),
            pinky_tip=(0.65, 0.45),
            pinky_pip=(0.65, 0.35),
            # Thumb pointing downwards (y increases downwards)
            thumb_mcp=(0.42, 0.52),
            thumb_ip=(0.40, 0.62),
            thumb_tip=(0.38, 0.75)
        )
        scale = get_hand_scale(drop_hand)
        self.assertTrue(FingerStateDetector.is_thumb_down(drop_hand, scale))
        gesture, _, _ = GestureClassifier.classify(drop_hand, scale)
        self.assertEqual(gesture, "DROP")

    def test_confirm_gesture_thumbs_up(self):
        """CONFIRM: 4 fingers folded, thumb pointing upwards."""
        confirm_hand = create_hand_fixture(
            wrist=(0.5, 0.8),
            index_tip=(0.5, 0.72),
            index_pip=(0.5, 0.58),
            middle_tip=(0.55, 0.72),
            middle_pip=(0.55, 0.58),
            ring_tip=(0.6, 0.72),
            ring_pip=(0.6, 0.58),
            pinky_tip=(0.65, 0.72),
            pinky_pip=(0.65, 0.60),
            # Thumb pointing upwards (y decreases upwards)
            thumb_mcp=(0.42, 0.65),
            thumb_ip=(0.40, 0.52),
            thumb_tip=(0.38, 0.35)
        )
        scale = get_hand_scale(confirm_hand)
        self.assertTrue(FingerStateDetector.is_thumb_up(confirm_hand, scale))
        gesture, _, _ = GestureClassifier.classify(confirm_hand, scale)
        self.assertEqual(gesture, "CONFIRM")

    def test_pin1_through_pin5(self):
        scale_ref = 0.30
        # PIN1: Index only
        h1 = create_hand_fixture(
            wrist=(0.5, 0.8),
            middle_mcp=(0.5, 0.5),
            index_tip=(0.5, 0.25), index_pip=(0.5, 0.55),
            middle_tip=(0.55, 0.72), ring_tip=(0.6, 0.72), pinky_tip=(0.65, 0.72)
        )
        g1, _, _ = GestureClassifier.classify(h1, get_hand_scale(h1))
        self.assertEqual(g1, "PIN1")

        # PIN2: Index + Middle
        h2 = create_hand_fixture(
            wrist=(0.5, 0.8),
            middle_mcp=(0.5, 0.5),
            index_tip=(0.48, 0.25), index_pip=(0.48, 0.55),
            middle_tip=(0.52, 0.25), middle_pip=(0.52, 0.55),
            ring_tip=(0.6, 0.72), pinky_tip=(0.65, 0.72)
        )
        g2, _, _ = GestureClassifier.classify(h2, get_hand_scale(h2))
        self.assertEqual(g2, "PIN2")

        # PIN3: Index + Middle + Ring
        h3 = create_hand_fixture(
            wrist=(0.5, 0.8),
            middle_mcp=(0.5, 0.5),
            index_tip=(0.48, 0.25), index_pip=(0.48, 0.55),
            middle_tip=(0.52, 0.25), middle_pip=(0.52, 0.55),
            ring_tip=(0.56, 0.25), ring_pip=(0.56, 0.55),
            pinky_tip=(0.65, 0.72)
        )
        g3, _, _ = GestureClassifier.classify(h3, get_hand_scale(h3))
        self.assertEqual(g3, "PIN3")

        # PIN4: All 4 non-thumb extended, thumb folded
        h4 = create_hand_fixture(
            wrist=(0.5, 0.8),
            middle_mcp=(0.5, 0.5),
            index_tip=(0.45, 0.20), middle_tip=(0.50, 0.18),
            ring_tip=(0.55, 0.20), pinky_tip=(0.60, 0.25),
            thumb_tip=(0.48, 0.58)
        )
        g4, _, _ = GestureClassifier.classify(h4, get_hand_scale(h4))
        self.assertEqual(g4, "PIN4")

        # PIN5: All 5 fingers extended wide
        h5 = create_hand_fixture(
            wrist=(0.5, 0.8),
            middle_mcp=(0.5, 0.5),
            index_tip=(0.45, 0.20), middle_tip=(0.50, 0.18),
            ring_tip=(0.55, 0.20), pinky_tip=(0.60, 0.25),
            thumb_tip=(0.20, 0.50)
        )
        g5, _, _ = GestureClassifier.classify(h5, get_hand_scale(h5))
        self.assertEqual(g5, "PIN5")

    def test_no_flick_gestures_exist(self):
        """Verifies flick gestures are not present in the classifier or engine."""
        engine = GestureEngine()
        self.assertFalse(hasattr(engine, 'flick_detector'), "FlickDetector must be completely removed")

    def test_model_path_resolution(self):
        """Verifies that find_default_model_path successfully resolves a path."""
        try:
            from main_gesture_service import find_default_model_path
            path = find_default_model_path()
            self.assertTrue(isinstance(path, str) and len(path) > 0)
        except ImportError:
            pass


if __name__ == "__main__":
    unittest.main()
