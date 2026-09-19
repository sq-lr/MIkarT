from __future__ import annotations

from types import SimpleNamespace

import pytest

from app.services.vision_service import ClaudeVisionService, DetectedObject, MockVisionService, SceneUnderstanding
from tests.conftest import make_png


class StubMessages:
    def __init__(self, response):
        self.response = response
        self.calls: list[dict] = []

    def parse(self, **kwargs):
        self.calls.append(kwargs)
        return self.response


def _stub_client(response):
    messages = StubMessages(response)
    return SimpleNamespace(beta=SimpleNamespace(messages=messages)), messages


def test_mock_vision_is_deterministic_and_detects_objects():
    service = MockVisionService()
    a = service.analyze_image(b"same bytes", "x")
    b = service.analyze_image(b"same bytes", "y")
    assert a == b
    assert a.detected_objects


def test_claude_vision_sends_image_and_truncates_to_max_objects():
    scene = SceneUnderstanding(
        dominant_colors=["#111111"],
        brightness=0.4,
        tags=["night"],
        detected_objects=[DetectedObject(label=f"obj_{i}", bbox=[0.1, 0.1, 0.2, 0.2], prominence=0.5) for i in range(6)],
    )
    client, messages = _stub_client(SimpleNamespace(stop_reason="end_turn", parsed_output=scene))
    service = ClaudeVisionService(max_objects=2, client=client)

    result = service.analyze_image(make_png(), "a spooky night")

    assert [o.label for o in result.detected_objects] == ["obj_0", "obj_1"]
    call = messages.calls[0]
    assert call["model"] == "claude-opus-5"
    assert call["output_format"] is SceneUnderstanding
    assert "2" in call["system"]  # max_objects is templated into the prompt
    content = call["messages"][0]["content"]
    assert content[0]["type"] == "image" and content[0]["source"]["media_type"] == "image/png"
    assert "a spooky night" in content[1]["text"]


def test_claude_vision_refusal_raises():
    client, _ = _stub_client(SimpleNamespace(stop_reason="refusal", parsed_output=None))
    service = ClaudeVisionService(max_objects=4, client=client)
    with pytest.raises(RuntimeError):
        service.analyze_image(make_png(), "x")


def test_detected_object_bbox_validation():
    with pytest.raises(ValueError):
        DetectedObject(label="x", bbox=[1.2, 0.0, 0.1, 0.1], prominence=0.5)
    with pytest.raises(ValueError):
        DetectedObject(label="x", bbox=[0.5, 0.5, 0.0, 0.1], prominence=0.5)
    # Overhanging boxes are clipped to the unit square, not rejected.
    assert DetectedObject(label="x", bbox=[0.8, 0.8, 0.5, 0.5], prominence=0.5).bbox == [0.8, 0.8, pytest.approx(0.2), pytest.approx(0.2)]
