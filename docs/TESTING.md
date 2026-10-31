# Testing Guide

AirFlick incorporates a comprehensive test suite covering the .NET core logic, cryptographic validation, and Python gesture classifiers.

---

## 1. Running .NET Unit Tests

The C# test project (`AirFlick.Tests\AirFlick.Tests.csproj`) contains unit tests verifying:
- PIN format enforcement (`[1, 5]` per digit, exactly 3 digits).
- 120-second expiration timing.
- Monotonic clock calculations.
- Constant-time proof comparisons.
- State machine role transitions (Sender / Receiver).
- Transfer chunking and integrity logic.

### Run All C# Tests
```powershell
dotnet test .\AirFlick.Tests\AirFlick.Tests.csproj -c Release
```

---

## 2. Running Python Gesture Tests

The Python gesture tests verify landmark geometry classification under various hand positions:

```powershell
python -m unittest discover -s gesture -p "*_test.py"
```

Individual standalone tests:
- `gesture/cancel_test.py`
- `gesture/flick_test.py`
- `gesture/grab_test.py`
- `gesture/hand_test.py`
- `gesture/pin_test.py`

